# 02 核心库分析 — Casbin.Persist.Adapter.EFCore

> **NuGet 包名**: `Casbin.NET.Adapter.EFCore`  
> **许可协议**: Apache-2.0  
> **仓库地址**: <https://github.com/casbin-net/efcore-adapter>  
> **代码行数**: 1,448 行（12 个 .cs 文件）  
> **目标框架**: net9.0, net8.0, net7.0, net6.0, net5.0, netcoreapp3.1  
> **依赖**: `Casbin.NET` ≥ 2.19.1, `Microsoft.EntityFrameworkCore` + `Microsoft.EntityFrameworkCore.Relational`（版本随 TFM 变化）

---

## 📂 1. 文件清单

| 文件 | 行数 | 作用 |
|------|------|------|
| `EFCoreAdapter.cs` | 878 | **核心适配器**：Load/Save/Add/Remove/Update 策略，多上下文事务管理 |
| `EFCoreAdapter.Internal.cs` | 241 | **内部实现**（partial class）：CRUD 操作内部方法 + 虚方法钩子 |
| `CasbinDbContext.cs` | 61 | **数据库上下文**：EF Core DbContext，管理 `casbin_rule` 表 |
| `DefaultPersistPolicyEntityTypeConfiguration.cs` | 48 | **实体配置**：表名、列映射、索引配置 |
| `IPersistPolicy.cs` | 8 | **实体接口**：持久化策略实体的最小接口 |
| `ICasbinDbContextProvider.cs` | 48 | **多上下文提供器接口**：策略类型路由到不同 DbContext |
| `SingleContextProvider.cs` | 58 | **单上下文提供器**：向后兼容的默认实现 |
| `Entities/EFCorePersistPolicy.cs` | 8 | **默认实体类**：继承 `PersistPolicy`，实现 `IEFCorePersistPolicy<TKey>` |
| `Extensions/ServiceCollectionExtensions.cs` | 59 | **DI 扩展方法**：注册 `IAdapter` 到服务容器 |
| `Extensions/PolicyStoreExtension.cs` | 39 | **策略存储扩展**：策略模型 ↔ 数据库实体转换 |
| `Casbin.Persist.Adapter.EFCore.csproj` | 57 | **项目配置**：多目标框架、NuGet 元数据、包依赖 |
| `casbin.png` | - | NuGet 包图标 |

---

## 🏗️ 2. 类层次结构

### 2.1 泛型继承链

```
EFCoreAdapter<TKey>  (简化版)
  └─ EFCoreAdapter<TKey, TPersistPolicy>  (指定策略实体)
       └─ EFCoreAdapter<TKey, TPersistPolicy, TDbContext>  (完整版，实际实现类)
            ├─ implements IAdapter
            └─ implements IFilteredAdapter
```

### 2.2 类型参数约束

| 泛型参数 | 约束 | 说明 |
|----------|------|------|
| `TKey` | `IEquatable<TKey>` | 主键类型，常见值：int, Guid, string |
| `TPersistPolicy` | `class, IEFCorePersistPolicy<TKey>, new()` | 持久化实体类型 |
| `TDbContext` | `DbContext` | EF Core 数据库上下文类型 |

---

## 📋 3. 核心类详解

### 3.1 `EFCoreAdapter<TKey, TPersistPolicy, TDbContext>` (partial class)

**文件**: `EFCoreAdapter.cs` (878行) + `EFCoreAdapter.Internal.cs` (241行)

#### 3.1.1 字段与属性

| 成员 | 类型 | 可见性 | 说明 |
|------|------|--------|------|
| `DbContext` | `TDbContext` | `protected` | 当前数据库上下文（向后兼容） |
| `PersistPolicies` | `DbSet<TPersistPolicy>` | `protected` | 缓存的 DbSet（向后兼容） |
| `_contextProvider` | `ICasbinDbContextProvider<TKey>` | `private readonly` | 多上下文提供器 |
| `_persistPoliciesByContext` | `Dictionary<(DbContext, string), DbSet<TPersistPolicy>>` | `private readonly` | **DbSet 缓存字典**，键为 (上下文, 策略类型) 组合 |
| `IsFiltered` | `bool` | `public` | 是否处于过滤加载状态 |

#### 3.1.2 构造函数（3 个入口）

| 构造函数 | 参数 | 场景 |
|----------|------|------|
| `EFCoreAdapter(TDbContext context)` | 直接传入 DbContext | **单上下文**（向后兼容，最常用） |
| `EFCoreAdapter(IServiceProvider serviceProvider)` | ASP.NET DI 容器 | **依赖注入**场景，每次操作从容器解析 DbContext |
| `EFCoreAdapter(ICasbinDbContextProvider<TKey> provider)` | 自定义上下文提供器 | **多上下文**场景，策略按类型路由到不同 DbContext |

#### 3.1.3 公共方法 — LoadPolicy

| 方法 | 签名 | 说明 |
|------|------|------|
| `LoadPolicy` | `void LoadPolicy(IPolicyStore store)` | 从所有上下文加载策略，调用 `OnLoadPolicy` 虚方法过滤 |
| `LoadPolicyAsync` | `Task LoadPolicyAsync(IPolicyStore store)` | 异步版本 |
| `LoadFilteredPolicy` | `void LoadFilteredPolicy(IPolicyStore, IPolicyFilter)` | 按过滤条件加载策略，设置 `IsFiltered = true` |
| `LoadFilteredPolicyAsync` | `Task LoadFilteredPolicyAsync(IPolicyStore, IPolicyFilter)` | 异步过滤加载 |

**加载流程**:
1. 遍历 `_contextProvider.GetAllContexts().Distinct()`
2. 对每个上下文调用 `GetCasbinRuleDbSet(context, null).AsNoTracking().ToList()`
3. 合并所有策略，传给 `OnLoadPolicy()` 虚方法
4. 调用 `store.LoadPolicyFromPersistPolicy()`

#### 3.1.4 公共方法 — SavePolicy

| 方法 | 签名 | 说明 |
|------|------|------|
| `SavePolicy` | `void SavePolicy(IPolicyStore store)` | 保存策略，**清空旧数据后写入新数据** |
| `SavePolicyAsync` | `Task SavePolicyAsync(IPolicyStore store)` | 异步版本 |

**保存流程（3 种事务策略）**:

```
SavePolicy()
  ├─ 从 IPolicyStore 读取所有策略类型
  ├─ 按目标上下文分组: GroupBy(p => _contextProvider.GetContextForPolicyType(p.Type))
  │
  ├─ [情况1] 单上下文 OR 共享连接 → SavePolicyWithSharedTransaction()
  │    ├─ 有共享连接 → 连接级事务 (connection.BeginTransaction())
  │    │    ├─ UseTransaction(transaction) 登记所有上下文
  │    │    ├─ ExecuteDelete() / RemoveRange() 清空
  │    │    ├─ AddRange() 写入新数据
  │    │    ├─ Commit() 或 Rollback()
  │    │    └─ UseTransaction(null) 清除事务状态（防止 SAVEPOINT 错误）
  │    └─ 无共享连接 → 上下文级事务（主上下文 BeginTransaction）
  │
  └─ [情况2] 多上下文+分离连接 → SavePolicyWithIndividualTransactions()
       └─ 每个上下文独立的 BeginTransaction（**非原子性**）
```

#### 3.1.5 公共方法 — Add/Remove/Update Policy（AutoSave 模式）

| 方法 | 说明 |
|------|------|
| `AddPolicy(section, type, values)` | 添加单条策略（**去重检查**：已存在则跳过） |
| `AddPolicyAsync(...)` | 异步添加 |
| `AddPolicies(section, type, valuesList)` | 批量添加 |
| `AddPoliciesAsync(...)` | 异步批量添加 |
| `RemovePolicy(section, type, values)` | 删除匹配策略 |
| `RemovePolicyAsync(...)` | 异步删除 |
| `RemoveFilteredPolicy(section, type, fieldIndex, fieldValues)` | 按字段条件删除 |
| `RemoveFilteredPolicyAsync(...)` | 异步过滤删除 |
| `RemovePolicies(section, type, valuesList)` | 批量删除 |
| `RemovePoliciesAsync(...)` | 异步批量删除 |
| `UpdatePolicy(section, type, oldValues, newValues)` | 更新策略（= Remove + Add） |
| `UpdatePolicyAsync(...)` | 异步更新 |
| `UpdatePolicies(section, type, oldList, newList)` | 批量更新 |
| `UpdatePoliciesAsync(...)` | 异步批量更新 |

> **AutoSave 说明**: Add/Remove/Update 方法**不创建显式事务**，依赖 EF Core 的隐式事务（`SaveChanges()` 自动创建）。这样可以避免多次操作连续调用时的 SAVEPOINT 错误。

#### 3.1.6 虚方法钩子（Override Points）

| 虚方法 | 触发时机 | 默认行为 | 用途 |
|--------|----------|----------|------|
| `OnLoadPolicy(store, policies)` | LoadPolicy 时 | 直接返回 policies | 加载后过滤/转换 |
| `OnSavePolicy(store, policies)` | SavePolicy 写库前 | 直接返回 policies | 保存前处理 |
| `OnAddPolicy(section, type, values, policy)` | AddPolicy 时 | 直接返回 policy | 添加前修改策略 |
| `OnAddPolicies(section, type, addList, policies)` | AddPolicies 时 | 直接返回 policies | 批量添加前处理 |
| `OnRemoveFilteredPolicy(section, type, fieldIndex, values, policies)` | RemoveFiltered 时 | 直接返回 policies | 删除前过滤 |
| `GetCasbinRuleDbSet(TDbContext)` | ⚠️ 已废弃 | 委托到新重载 | 向后兼容 |
| `GetCasbinRuleDbSet(DbContext, policyType)` | 获取 DbSet 时 | `dbContext.Set<TPersistPolicy>()` | 自定义 DbSet 获取逻辑 |

#### 3.1.7 私有辅助方法

| 方法 | 说明 |
|------|------|
| `CanShareTransaction(List<DbContext>)` | 检查所有上下文是否共享**同一个** `DbConnection` 对象（引用相等） |
| `GetContextForPolicyType(string)` | 根据策略类型获取目标 DbContext |
| `GetCasbinRuleDbSetForPolicyType(DbContext, string)` | 获取或缓存 DbSet，使用 (上下文, 策略类型) 作为**复合键** |

---

### 3.2 `CasbinDbContext<TKey>` — 数据库上下文

**文件**: `CasbinDbContext.cs` (61行)

| 成员 | 说明 |
|------|------|
| `Policies` | `DbSet<EFCorePersistPolicy<TKey>>`，默认表名 `casbin_rule` |
| `_casbinModelConfig` | 实体配置（`IEntityTypeConfiguration`） |
| `_schemaName` | PostgreSQL schema 名称（可选） |

#### 构造函数重载

| 构造函数 | 说明 |
|----------|------|
| `CasbinDbContext()` | 无参构造，使用默认表名 |
| `CasbinDbContext(DbContextOptions, schemaName, tableName)` | 指定选项、schema、表名 |
| `CasbinDbContext(DbContextOptions, IEntityTypeConfiguration, schemaName)` | 自定义实体配置 |

#### `OnModelCreating` 逻辑

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    if (!string.IsNullOrWhiteSpace(_schemaName))
        modelBuilder.HasDefaultSchema(_schemaName);    // PostgreSQL 多 schema 支持
    if (_casbinModelConfig is not null)
        modelBuilder.ApplyConfiguration(_casbinModelConfig);  // 列映射、索引
}
```

---

### 3.3 `DefaultPersistPolicyEntityTypeConfiguration<TKey>` — 默认实体配置

**文件**: `DefaultPersistPolicyEntityTypeConfiguration.cs` (48行)

#### 数据库列映射

| C# 属性 | 数据库列名 | 说明 |
|---------|-----------|------|
| `Id` | `id` | 主键 |
| `Section` | ⚠️ **忽略**（不映射） | Section 由 Type 首字母推断 |
| `Type` | `ptype` | 策略类型（p, p2, g, g2, ...） |
| `Value1` ~ `Value14` | `v0` ~ `v13` | 策略值，共 14 个字段 |

#### 索引配置

在 `ptype`, `v0`, `v1`, `v2`, `v3`, `v4`, `v5` 上建立索引。

#### 可配置项

| 配置项 | 默认值 | 配置方式 |
|--------|--------|----------|
| 表名 | `casbin_rule` | 构造函数 `tableName` 参数 |
| Schema 名 | `null` | `CasbinDbContext` 构造函数 `schemaName` 参数 |

---

### 3.4 `IPersistPolicy` / `IEFCorePersistPolicy<TKey>` — 实体接口

**文件**: `IPersistPolicy.cs` (8行)

```csharp
// 最小接口：需要一个主键 Id
public interface IEFCorePersistPolicy<TKey> : IPersistPolicy where TKey : IEquatable<TKey>
{
    TKey Id { get; set; }
}
```

### 3.5 `EFCorePersistPolicy<TKey>` — 默认实体类

**文件**: `Entities/EFCorePersistPolicy.cs` (8行)

```csharp
public class EFCorePersistPolicy<TKey> : PersistPolicy, IEFCorePersistPolicy<TKey>
    where TKey : IEquatable<TKey>
{
    public TKey Id { get; set; }
}
```

- 继承 `Casbin.Persist.PersistPolicy`（来自 Casbin.NET 库），该基类包含 `Type`, `Section`, `Value1~Value14` 等属性
- 仅新增 `Id` 属性作为主键

---

### 3.6 `ICasbinDbContextProvider<TKey>` — 多上下文提供器接口

**文件**: `ICasbinDbContextProvider.cs` (48行)

| 方法 | 说明 |
|------|------|
| `GetContextForPolicyType(string policyType)` | 根据策略类型返回对应的 DbContext |
| `GetAllContexts()` | 返回所有唯一的 DbContext 实例 |
| `GetSharedConnection()` | 返回共享的 `DbConnection`，若无则返回 `null` |

> **GetSharedConnection 的语义**:
> - 返回非 null → 适配器使用**连接级事务**（`connection.BeginTransaction()`），实现多上下文原子提交
> - 返回 null → 适配器使用**上下文级事务**（`context.Database.BeginTransaction()`），每个上下文独立事务

---

### 3.7 `SingleContextProvider<TKey>` — 默认单上下文实现

**文件**: `SingleContextProvider.cs` (58行)

| 方法 | 行为 |
|------|------|
| `GetContextForPolicyType(string)` | **忽略参数**，始终返回唯一的上下文 |
| `GetAllContexts()` | 返回包含唯一上下文的集合 |
| `GetSharedConnection()` | 始终返回 `null` |

---

### 3.8 `PolicyStoreExtension` — 策略存储扩展

**文件**: `Extensions/PolicyStoreExtension.cs` (39行)

| 方法 | 方向 | 说明 |
|------|------|------|
| `LoadPolicyFromPersistPolicy<TPersistPolicy>(store, policies)` | DB → Model | 将数据库实体加载到 Casbin 内存模型 |
| `ReadPolicyFromCasbinModel<TPersistPolicy>(list, store)` | Model → DB | 从 Casbin 内存模型读取策略到实体列表 |

#### 加载逻辑细节

```csharp
// 如果 Section 为空，从 Type 首字母推断（"p" → "p", "g" → "g"）
if (string.IsNullOrWhiteSpace(policy.Section))
    policy.Section = policy.Type.Substring(0, 1);
```

---

### 3.9 `ServiceCollectionExtensions` — DI 注册扩展

**文件**: `Extensions/ServiceCollectionExtensions.cs` (59行)

| 方法 | 说明 |
|------|------|
| `AddEFCoreAdapter<TKey>(services, lifetime)` | 注册默认 `EFCoreAdapter<TKey>` 为 `IAdapter`，默认 `Scoped` |
| `AddEFCoreAdapter<TKey, TPersistPolicy>(services, lifetime)` | 注册自定义持久化实体的适配器 |

**关键实现细节**:
- 使用 `ServiceDescriptor` + lambda 工厂：`sp => new EFCoreAdapter<TKey>(sp)`
- 使用 `IServiceProvider` 构造函数，**每次操作从容器解析 DbContext**，避免跨 Scope 的 `ObjectDisposedException`
- 使用 `TryAdd` 避免重复注册

---

## ⚙️ 4. 编译条件

| 条件符号 | 影响的方法 | 说明 |
|----------|-----------|------|
| `NET7_0_OR_GREATER` | SavePolicy, SavePolicyAsync（删除部分） | 使用 `ExecuteDelete()` / `ExecuteDeleteAsync()` 进行集合级删除 |
| 非 NET7 | SavePolicy, SavePolicyAsync（删除部分） | 回退到 `ToList()` + `RemoveRange()` + `SaveChanges()` |

---

## 🔑 5. 关键设计决策

### 5.1 DbSet 缓存键设计

```csharp
// 缓存键为 (DbContext, string policyType) 复合键，而非仅 DbContext
private readonly Dictionary<(DbContext context, string policyType), DbSet<TPersistPolicy>> _persistPoliciesByContext;
```

**原因**: 多上下文场景下，同一个 DbContext 可能需要返回不同的 DbSet。仅用 context 作为键会导致不同策略类型共享错误的 DbSet。

### 5.2 事务隔离策略

| 场景 | 策略 | 原子性 |
|------|------|--------|
| 单上下文 | 上下文级事务 | ✅ 原子 |
| 多上下文 + 共享连接 | 连接级事务 | ✅ 原子 |
| 多上下文 + 分离连接（如不同 SQLite 文件） | 各自独立事务 | ❌ 非原子 |

### 5.3 AutoSave 模式下的 SAVEPOINT 防护

Add/Remove/Update 方法**不创建显式事务**，依赖 EF Core 的隐式事务。SavePolicy 在提交后调用 `UseTransaction(null)` 清除事务状态，防止后续 `SaveChanges()` 产生 SAVEPOINT 错误（PostgreSQL 特有）。

---

## 📊 6. 配置项汇总

| 配置项 | 位置 | 默认值 | 说明 |
|--------|------|--------|------|
| 表名 | `CasbinDbContext` 构造函数 | `"casbin_rule"` | 策略存储表名 |
| Schema 名 | `CasbinDbContext` 构造函数 | `null` | PostgreSQL schema 名 |
| 列名映射 | `DefaultPersistPolicyEntityTypeConfiguration` | `v0`~`v13`, `ptype` | 数据库列名 |
| 索引字段 | `DefaultPersistPolicyEntityTypeConfiguration` | `ptype`, `v0`~`v5` | 自动创建索引的字段 |
| DI 生命周期 | `AddEFCoreAdapter()` | `ServiceLifetime.Scoped` | 适配器的 DI 注册生命周期 |
| 目标框架 | `.csproj` | net9.0~netcoreapp3.1 | 6 个 TFM |
| EF Core 版本 | `.csproj` | 3.1.32~9.0.0 | 按 TFM 分别指定 |
| Casbin.NET 版本 | `.csproj` | 2.19.1 | 核心依赖版本 |
