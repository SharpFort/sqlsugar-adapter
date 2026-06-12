# 03 单元测试分析 — Casbin.Persist.Adapter.EFCore.UnitTest

> **代码行数**: 1,489 行（13 个 .cs 文件 + 示例文件）  
> **测试框架**: xUnit  
> **数据库**: SQLite（内存/文件）  
> **被测组件**: `Casbin.Persist.Adapter.EFCore` 核心库

---

## 📂 1. 文件清单

| 文件 | 行数 | 作用 |
|------|------|------|
| **测试主体** | | |
| `AutoTest.cs` | 63 | 基础适配器测试框架（`EFCoreAdapterTest`） |
| `BackwardCompatibilityTest.cs` | 290 | 单上下文向后兼容性测试（9 个测试） |
| `DependencyInjectionTest.cs` | 90 | 依赖注入生命周期测试（5 个测试） |
| `MultiContextTest.cs` | 490 | 多上下文功能测试（13 个测试） |
| `SpecialPolicyTest.cs` | 121 | 边界情况测试（2 个测试） |
| `TestUtil.cs` | 167 | 测试工具类（断言辅助方法） |
| **Fixtures/** | | |
| `TestHostFixture.cs` | 29 | ASP.NET TestServer DI 容器夹具 |
| `DbContextProviderFixture.cs` | 17 | SQLite DbContext 工厂 |
| `ModelProvideFixture.cs` | 14 | RBAC 模型提供器 |
| `MultiContextProviderFixture.cs` | 84 | 多上下文测试夹具 |
| `PolicyTypeContextProvider.cs` | 52 | 测试用策略类型→上下文路由器 |
| `SimpleFieldFilter.cs` | 36 | 测试用字段过滤器 |
| **Extensions/** | | |
| `CasbinDbContextExtension.cs` | 36 | `Clear()` 扩展方法 |
| **examples/** | | |
| `rbac_model.conf` | 14 | RBAC 模型定义 |
| `rbac_policy.csv` | 5 | 测试策略数据 |

---

## 🏗️ 2. 类层次

```
TestUtil (基类，提供断言方法)
├── EFCoreAdapterTest          - 基础适配器测试
├── BackwardCompatibilityTest   - 向后兼容测试
├── MultiContextTest            - 多上下文测试
│    └── DbSetCachingTestAdapter (内部子类)
├── SpecialPolicyTest           - 边界情况测试

DependencyInjectionTest         - DI 测试（不继承 TestUtil）
```

---

## 📋 3. 测试详细分析

### 3.1 `TestUtil` — 测试工具基类

| 方法 | 说明 |
|------|------|
| `AsList(params T[])` / `AsList(params string[])` | 快速创建 `List<T>` |
| `SetEquals(List<string>, IEnumerable<string>)` | 集合相等比较（排序后对比） |
| `ArrayEquals(List<string>, IEnumerable<string>)` | 数组顺序相等比较 |
| `Array2DEquals(List<List<string>>, IEnumerable<IEnumerable<string>>)` | 二维数组相等比较 |
| `TestEnforce(e, sub, obj, act, res)` | 执行 Enforce 并断言结果 |
| `TestEnforceWithoutUsers(e, obj, act, res)` | 无用户 Enforce |
| `TestDomainEnforce(e, sub, dom, obj, act, res)` | 域 Enforce |
| `TestGetPolicy(e, res)` | 获取策略并断言 |
| `TestGetFilteredPolicy(e, fieldIndex, res, fieldValues)` | 过滤策略并断言 |
| `TestGetGroupingPolicy(e, res)` | 获取分组策略 |
| `TestGetFilteredGroupingPolicy(e, fieldIndex, res, values)` | 过滤分组策略 |
| `TestHasPolicy(e, policy, res)` | 检查策略存在性 |
| `TestHasGroupingPolicy(e, policy, res)` | 检查分组策略存在性 |
| `TestGetRoles(e, name, res)` | 获取用户角色 |
| `TestGetUsers(e, name, res)` | 获取角色用户 |
| `TestHasRole(e, name, role, res)` | 检查用户拥有角色 |
| `TestGetPermissions(e, name, res)` | 获取用户权限 |
| `TestHasPermission(e, name, permission, res)` | 检查用户拥有权限 |
| `TestGetRolesInDomain(e, name, domain, res)` | 域内角色 |
| `TestGetPermissionsInDomain(e, name, domain, res)` | 域内权限 |

---

### 3.2 `EFCoreAdapterTest` (AutoTest.cs)

**依赖夹具**: `ModelProvideFixture`, `DbContextProviderFixture`

| 测试方法 | 测试内容 |
|----------|----------|
| 🔲 仅框架，无 `[Fact]` 测试 | 定义了 `InitPolicy()` 静态方法供其他测试初始化数据 |

`InitPolicy()` 初始化 5 条策略：
```
p, alice, data1, read         (政策)
p, bob, data2, write           (政策)
p, data2_admin, data2, read    (政策)
p, data2_admin, data2, write   (政策)
g, alice, data2_admin          (分组)
```

---

### 3.3 `BackwardCompatibilityTest` (290行)

**目标**: 验证多上下文改动不影响原有单上下文用法。

| # | 测试方法 | 验证内容 |
|---|----------|----------|
| 1 | `TestSingleContextConstructorStillWorks` | `new EFCoreAdapter<int>(context)` 构造函数正常 |
| 2 | `TestSingleContextAsyncOperationsStillWork` | 异步操作正常 |
| 3 | `TestSingleContextLoadAndSave` | Load/Save 完整流程 |
| 4 | `TestSingleContextWithExistingTests` | 与原有测试模式一致 |
| 5 | `TestSingleContextRemoveOperations` | RemovePolicy 正常 |
| 6 | `TestSingleContextUpdateOperations` | UpdatePolicy 正常 |
| 7 | `TestSingleContextBatchOperations` | AddPolicies/RemovePolicies 批量操作 |
| 8 | `TestSingleContextFilteredLoading` | LoadFilteredPolicy + IsFiltered |
| 9 | `TestSingleContextProviderWrapping` | `SingleContextProvider` 包装行为一致 |
| 10 | `TestSingleContextProviderGetAllContexts` | GetAllContexts 返回单上下文 |
| 11 | `TestSingleContextProviderGetContextForPolicyType` | 所有策略类型返回同一上下文 |

---

### 3.4 `DependencyInjectionTest` (90行)

**依赖夹具**: `TestHostFixture`, `ModelProvideFixture`

| # | 测试方法 | 验证内容 |
|---|----------|----------|
| 1 | `ShouldResolveCasbinDbContext` | DI 容器能解析 `CasbinDbContext<int>` |
| 2 | `ShouldResolveEfCoreAdapter` | DI 容器能解析 `IAdapter` |
| 3 | `ShouldUseAdapterAcrossMultipleScopesWithDbContextDirectly` | ⚠️ **直接传 DbContext 的适配器在跨 Scope 使用时会抛 `ObjectDisposedException`** |
| 4 | `ShouldUseAdapterAcrossMultipleScopesWithServiceProvider` | ✅ **使用 IServiceProvider 构造的适配器可跨 Scope 安全使用** |
| 5 | `ShouldResolveAdapterRegisteredWithExtensionMethod` | `AddEFCoreAdapter<int>()` 注册的适配器可正常使用 |

---

### 3.5 `MultiContextTest` (490行)

**依赖夹具**: `ModelProvideFixture`, `MultiContextProviderFixture`

#### 核心测试

| # | 测试方法 | 验证内容 |
|---|----------|----------|
| 1 | `TestMultiContextAddPolicy` | p 策略 → policyContext, g 策略 → groupingContext |
| 2 | `TestMultiContextAddPolicyAsync` | 异步版本 |
| 3 | `TestMultiContextRemovePolicy` | 从各自上下文删除 |
| 4 | `TestMultiContextLoadPolicy` | 从两个上下文加载并合并策略 |
| 5 | `TestMultiContextLoadPolicyAsync` | 异步加载 |
| 6 | `TestMultiContextSavePolicy` | Save 分发策略到各自上下文 |
| 7 | `TestMultiContextSavePolicyAsync` | 异步保存 |
| 8 | `TestMultiContextBatchOperations` | 批量添加/删除 |
| 9 | `TestMultiContextLoadFilteredPolicy` | 过滤加载后验证 |
| 10 | `TestMultiContextUpdatePolicyNoException` | UpdatePolicy 不抛异常 |
| 11 | `TestMultiContextProviderGetAllContexts` | 返回 2 个上下文 |
| 12 | `TestMultiContextProviderGetContextForPolicyType` | p/p2→同一上下文, g/g2→同一上下文, p≠g |
| 13 | `TestDbSetCachingByPolicyType` | **验证 DbSet 缓存使用 (context, policyType) 复合键** |

#### 内部类: `DbSetCachingTestAdapter`

```csharp
internal class DbSetCachingTestAdapter : EFCoreAdapter<int>
{
    private readonly Dictionary<string, int> _callTracker;

    protected override DbSet<EFCorePersistPolicy<int>> GetCasbinRuleDbSet(
        DbContext dbContext, string policyType)
    {
        if (policyType != null) _callTracker[policyType]++;
        return base.GetCasbinRuleDbSet(dbContext, policyType);
    }
}
```

- 重写 `GetCasbinRuleDbSet` 追踪调用次数
- 验证 p 类型调用 1 次（后续缓存命中），g 类型调用 1 次

---

### 3.6 `SpecialPolicyTest` (121行)

| # | 测试方法 | 验证内容 |
|---|----------|----------|
| 1 | `TestCommaPolicy` | 策略值包含逗号的边界情况（使用 `eval()` 函数） |
| 2 | `TestUnexpectedPolicy` | 字段数量不一致的策略（null 值、多余字段） |

**TestUnexpectedPolicy 验证逻辑**:
- Value3=null 的策略 → 加载为 `"a1", "a2", ""`
- Value4 多余的策略 → 截断为 3 个字段：`"b1", "b2", "b3"`

---

## 🧪 4. Fixtures（测试夹具）

### 4.1 `TestHostFixture`

**文件**: `Fixtures/TestHostFixture.cs` (29行)

| 配置项 | 值 |
|--------|-----|
| 数据库 | SQLite 文件（`CasbinHostTest_{Guid}.db`） |
| 注册服务 | `AddDbContext<CasbinDbContext<int>>` + `AddEFCoreAdapter<int>()` |
| 容器 | `ServiceCollection → BuildServiceProvider()` |
| 测试服务器 | `TestServer`（ASP.NET Core） |

### 4.2 `DbContextProviderFixture`

**文件**: `Fixtures/DbContextProviderFixture.cs` (17行)

```csharp
public CasbinDbContext<TKey> GetContext<TKey>(string name)
{
    var options = new DbContextOptionsBuilder<CasbinDbContext<TKey>>()
        .UseSqlite($"Data Source={name}.db")
        .Options;
    var context = new CasbinDbContext<TKey>(options);
    context.Database.EnsureCreated();
    return context;
}
```

- 通用 SQLite 上下文工厂
- 每个测试使用独立的 `{name}.db` 文件隔离

### 4.3 `ModelProvideFixture`

**文件**: `Fixtures/ModelProvideFixture.cs` (14行)

- 从 `examples/rbac_model.conf` 读取 RBAC 模型文本
- 每次调用 `GetNewRbacModel()` 返回新的 `IModel` 实例

### 4.4 `MultiContextProviderFixture`

**文件**: `Fixtures/MultiContextProviderFixture.cs` (84行)

| 方法 | 说明 |
|------|------|
| `GetMultiContextProvider(string testName)` | 创建 `PolicyTypeContextProvider`，使用 2 个独立 SQLite 文件 |
| `GetSeparateContexts(string testName)` | 返回 `(policyContext, groupingContext)` 元组，指向同一数据库的新上下文实例 |

**数据库文件命名规则**: `MultiContext_{testName}_policy.db` / `MultiContext_{testName}_grouping.db`

### 4.5 `PolicyTypeContextProvider`

**文件**: `Fixtures/PolicyTypeContextProvider.cs` (52行)

实现 `ICasbinDbContextProvider<int>`：

| 方法 | 路由规则 |
|------|----------|
| `GetContextForPolicyType(type)` | 以 `p` 开头 → policyContext；其他 → groupingContext |
| `GetAllContexts()` | 返回 `[policyContext, groupingContext]` |
| `GetSharedConnection()` | 返回 `null`（SQLite 分离连接） |

### 4.6 `SimpleFieldFilter`

**文件**: `Fixtures/SimpleFieldFilter.cs` (36行)

- 实现 `IPolicyFilter`
- 内部使用 `PolicyFilter(policyType, fieldIndex, values)` 实现字段级过滤
- 用于 `LoadFilteredPolicy` 测试

---

## 🔧 5. Extensions（测试扩展）

### 5.1 `CasbinDbContextExtension.Clear<TKey>()`

**文件**: `Extensions/CasbinDbContextExtension.cs` (36行)

```csharp
internal static void Clear<TKey>(this CasbinDbContext<TKey> dbContext)
```

**逻辑**:
1. 强制初始化模型 (`_ = dbContext.Model`)
2. `EnsureCreated()` 确保表存在
3. 读取并删除所有策略
4. 若 SQLite 表仍不存在，执行 `EnsureDeleted() → EnsureCreated()` 重试

---

## 📊 6. 示例文件

### 6.1 `rbac_model.conf`

```
[request_definition]  r = sub, obj, act
[policy_definition]   p = sub, obj, act
[role_definition]     g = _, _
[policy_effect]       e = some(where (p.eft == allow))
[matchers]            m = g(r.sub, p.sub) && r.obj == p.obj && r.act == p.act
```

标准 RBAC 模型：用户通过 `g` 角色绑定获取 `p` 策略权限。

### 6.2 `rbac_policy.csv`

```
p, alice, data1, read
p, bob, data2, write
p, data2_admin, data2, read
p, data2_admin, data2, write
g, alice, data2_admin
```

- alice 属于 data2_admin 角色 → 可读写 data2
- bob 只有 data2 写入权限

---

## 🔑 7. 测试覆盖矩阵

| 功能域 | 覆盖测试 | 测试数 |
|--------|----------|--------|
| 单上下文 - 构造 | BackwardCompatibilityTest | 3 |
| 单上下文 - CRUD | BackwardCompatibilityTest | 4 |
| 单上下文 - 过滤 | BackwardCompatibilityTest | 1 |
| 单上下文 - Provider | BackwardCompatibilityTest | 3 |
| 多上下文 - 添加 | MultiContextTest | 2 |
| 多上下文 - 删除 | MultiContextTest | 1 |
| 多上下文 - 加载 | MultiContextTest | 2 |
| 多上下文 - 保存 | MultiContextTest | 2 |
| 多上下文 - 批量 | MultiContextTest | 1 |
| 多上下文 - 过滤 | MultiContextTest | 1 |
| 多上下文 - 更新 | MultiContextTest | 1 |
| 多上下文 - Provider | MultiContextTest | 3 |
| 多上下文 - 缓存 | MultiContextTest | 1 |
| DI 生命周期 | DependencyInjectionTest | 5 |
| 边界 - 逗号策略 | SpecialPolicyTest | 1 |
| 边界 - 字段不一致 | SpecialPolicyTest | 1 |
| **总计** | | **31** |
