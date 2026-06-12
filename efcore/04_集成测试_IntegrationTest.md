# 04 集成测试分析 — Casbin.Persist.Adapter.EFCore.IntegrationTest

> **代码行数**: 2,171 行（5 个 .cs 文件 + 示例文件）  
> **测试框架**: xUnit（顺序执行，禁止并行）  
> **数据库**: PostgreSQL（localhost:5432）  
> **连接字符串**: `Host=localhost;Database=casbin_integration_test;Username=postgres;Password=postgres4all!`  
> **测试分类**: `[Trait("Category", "Integration")]`，CI/CD 排除，本地手动运行  
> **运行命令**: `dotnet test --filter "Category=Integration"`

---

## 📂 1. 文件清单

| 文件 | 行数 | 作用 |
|------|------|------|
| `Integration/AutoSaveTests.cs` | 984 | **AutoSave 行为测试**（12 个测试） |
| `Integration/TransactionIntegrityTests.cs` | 598 | **事务完整性测试**（7 个测试） |
| `Integration/SchemaDistributionTests.cs` | 342 | **Schema 分发测试**（2 个测试） |
| `Integration/TransactionIntegrityTestFixture.cs` | 227 | **PostgreSQL 测试夹具** |
| `Integration/IntegrationTestCollection.cs` | 20 | xUnit 集合定义（顺序执行） |
| `Integration/README.md` | - | 集成测试说明文档 |
| `examples/multi_context_model.conf` | 16 | 多上下文模型（含 g2 角色定义） |
| `Casbin.Persist.Adapter.EFCore.IntegrationTest.csproj` | - | 测试项目配置 |
| `xunit.runner.json` | - | xUnit 运行配置 |

---

## 🏗️ 2. 测试架构

### 2.1 三段式 Schema 设计

集成测试在 **同一个 PostgreSQL 数据库** 中创建 **3 个 Schema**，模拟多上下文场景：

| Schema | 用途 | 策略类型 |
|--------|------|----------|
| `casbin_policies` | 权限策略 | `p`, `p2`, `p3`... |
| `casbin_groupings` | 角色分组 | `g` |
| `casbin_roles` | 扩展角色 | `g2`, `g3`, `g4`... |

```
casbin_integration_test (数据库)
├── casbin_policies.casbin_rule    ← p  策略
├── casbin_groupings.casbin_rule   ← g  分组
└── casbin_roles.casbin_rule        ← g2 角色
```

### 2.2 导出的上下文类

所有测试文件共享 3 个派生上下文类：

```csharp
// 三个类结构相同，仅用于区分 Schema
public class TestCasbinDbContext1 : CasbinDbContext<int> { }  // → casbin_policies
public class TestCasbinDbContext2 : CasbinDbContext<int> { }  // → casbin_groupings
public class TestCasbinDbContext3 : CasbinDbContext<int> { }  // → casbin_roles
```

### 2.3 共享连接 vs 分离连接

```
共享连接模式:                         分离连接模式:
┌─────────────────────┐              ┌──────────┐ ┌──────────┐ ┌──────────┐
│ NpgsqlConnection #1 │              │ Conn #1   │ │ Conn #2   │ │ Conn #3   │
├─────────────────────┤              ├──────────┤ ├──────────┤ ├──────────┤
│  ctx_policies       │              │ policies │  │groupings │  │ roles    │
│  ctx_groupings      │              └──────────┘ └──────────┘ └──────────┘
│  ctx_roles          │               ✅ 独立提交   ✅ 独立提交   ❌ 失败
└─────────────────────┘               ❌ 不原子（数据残留）
✅ 原子提交 / 原子回滚
```

---

## 📋 3. `TransactionIntegrityTestFixture` — PostgreSQL 测试夹具

**文件**: `Integration/TransactionIntegrityTestFixture.cs` (227行)

### 3.1 配置项

| 配置项 | 值 | 位置 |
|--------|-----|------|
| 连接字符串 | `Host=localhost;Database=casbin_integration_test;Username=postgres;Password=postgres4all!` | 构造函数 |
| Policies Schema | `casbin_policies` | 常量 |
| Groupings Schema | `casbin_groupings` | 常量 |
| Roles Schema | `casbin_roles` | 常量 |

### 3.2 方法清单

| 方法 | 可见性 | 说明 |
|------|--------|------|
| `InitializeAsync()` | public | 创建 3 个 Schema + 运行迁移 |
| `DisposeAsync()` | public | 清理（当前已注释 DROP，保留表用于调试） |
| `CreateSchemasAsync()` | private | `CREATE SCHEMA IF NOT EXISTS` |
| `RunMigrationsAsync()` | public | 在每个 Schema 中创建 `casbin_rule` 表 + 索引 |
| `DropSchemasAsync()` | private | `DROP TABLE/Schema CASCADE` |
| `ClearAllPoliciesAsync()` | public | `DELETE FROM {schema}.casbin_rule` |
| `CountPoliciesInSchemaAsync(schema, ptype?)` | public | `SELECT COUNT(*) FROM {schema}.casbin_rule [WHERE ptype=?]` |
| `InsertPolicyDirectlyAsync(schema, ptype, values)` | public | 直接 INSERT 到指定 Schema |
| `DropTableAsync(schema)` | public | `DROP TABLE IF EXISTS {schema}.casbin_rule CASCADE` |

### 3.3 数据表结构

```sql
CREATE TABLE {schema}.casbin_rule (
    id    SERIAL PRIMARY KEY,
    ptype VARCHAR(254) NOT NULL,
    v0    VARCHAR(254), v1  VARCHAR(254), v2  VARCHAR(254), v3  VARCHAR(254),
    v4    VARCHAR(254), v5  VARCHAR(254), v6  VARCHAR(254), v7  VARCHAR(254),
    v8    VARCHAR(254), v9  VARCHAR(254), v10 VARCHAR(254), v11 VARCHAR(254),
    v12   VARCHAR(254), v13 VARCHAR(254)
);
CREATE INDEX ix_casbin_rule_ptype ON {schema}.casbin_rule (ptype);
```

---

## 📋 4. `TransactionIntegrityTests` — 事务完整性测试（7 个测试）

**文件**: `Integration/TransactionIntegrityTests.cs` (598行)

内部类 `ThreeWayPolicyTypeProvider`：
- 实现 `ICasbinDbContextProvider<int>`
- p → policyContext, g → groupingContext, g2/g3/g4 → roleContext

### 测试用例

| # | 测试方法 | 关键断言 |
|---|----------|----------|
| 1 | `SavePolicy_WithSharedConnection_ShouldWriteToAllContextsAtomically` | 3 个 Schema 各 1 条策略 |
| 2 | `MultiContextSetup_WithSharedConnection_ShouldShareSamePhysicalConnection` | **引用相等**：`Assert.Same(conn1, conn2)` |
| 3 | `SavePolicy_WhenTableDroppedInOneContext_ShouldRollbackAllContexts` | ⭐ **回滚验证**：3 个 Schema 均为 0（全部回滚） |
| 4 | `SavePolicy_WhenTableMissingInOneContext_ShouldRollbackAllContexts` | ⭐ 同上，另一个场景 |
| 5 | `MultipleSaveOperations_WithSharedConnection_ShouldMaintainDataConsistency` | 3 次 Save 后：3 条 p + 3 条 g |
| 6 | `SavePolicy_WithSeparateConnections_ShouldNotBeAtomic` | ⚠️ **非原子性证明**：p=1, g=1, g2=0（残留数据） |
| 7 | `SavePolicy_ShouldReflectDatabaseStateNotCasbinMemory` | 防重复检查：重复添加返回 false |

### 关键验证逻辑 — 测试 3（原子回滚）

```
1. 创建 3 个共享连接的上下文（policies, groupings, roles）
2. EnableAutoSave(false) — 策略暂存内存
3. AddPolicy + AddGroupingPolicy + AddNamedGroupingPolicy(g2)
4. 删除 roles schema 的 casbin_rule 表（模拟故障）
5. 调用 SavePolicyAsync() → 期望抛出异常
6. 重新创建表并查询所有 schema
7. 断言：3 个 schema 均为 0 条策略 ✅（证明全部回滚）
```

---

## 📋 5. `SchemaDistributionTests` — Schema 分发测试（2 个测试）

**文件**: `Integration/SchemaDistributionTests.cs` (342行)

内部类 `ThreeWayContextProvider`：
- 使用 `switch` 表达式路由：`"p"` → policies, `"g"` → groupings, `"g2"` → roles

| # | 测试方法 | 关键发现 |
|---|----------|----------|
| 1 | `SavePolicy_SeparateConnections_ShouldDistributeAcrossSchemas` | ✅ **基线测试**：HasDefaultSchema() 在分离连接下正确分发 |
| 2 | `SavePolicy_SharedConnection_ShouldDistributeAcrossSchemas` | ⭐ **关键测试**：HasDefaultSchema() 在共享连接下是否仍正确分发？ |

**测试 2 的目的**:
- 如果 PASS → `SET search_path` 方法**不需要**
- 如果 FAIL → 需要 `SET search_path` 显式设置
- 结果由 `ITestOutputHelper` 输出到 xUnit 日志

---

## 📋 6. `AutoSaveTests` — AutoSave 行为测试（12 个测试）

**文件**: `Integration/AutoSaveTests.cs` (984行)

### 6.1 内部类

| 类 | 说明 |
|-----|------|
| `TestCasbinDbContext1/2/3` | 三个 Schema 的派生上下文 |
| `ThreeWayContextProvider` | ICasbinDbContextProvider 实现（switch 路由） |

### 6.2 测试用例 — 单上下文

| # | 测试方法 | AutoSave | 核心验证 |
|---|----------|----------|----------|
| 1 | `TestPolicyAutoSaveOn` | ON (默认) | Add/Remove/Update/Filter/Batch 全流程，每个操作后立即持久化 |
| 2 | `TestPolicyAutoSaveOnAsync` | ON | 异步版本快速验证 |
| 3 | `TestPolicyAutoSaveOff` | OFF | ⭐ **验证 AutoSave OFF 正确行为**：Add 后不保存，SavePolicy 后才提交 |
| 4 | `TestPolicyAutoSaveOffAsync` | OFF | 异步版本 |
| 5 | `TestGroupingPolicyAutoSaveOn` | ON | 分组策略立即保存 |
| 6 | `TestGroupingPolicyAutoSaveOff` | OFF | ⚠️ **分组策略 AutoSave OFF 行为**（已知 Casbin.NET bug） |
| 7 | `TestGroupingPolicyAutoSaveOffAsync` | OFF | ⚠️ 异步版本（已知 bug） |

### 6.3 测试用例 — 多上下文

| # | 测试方法 | AutoSave | 核心验证 |
|---|----------|----------|----------|
| 8 | `TestAutoSaveOff_MultiContext_RollbackOnFailure` | OFF | ⭐ **多上下文原子回滚**：Drop 表后 SavePolicy 应该全部回滚 |
| 9 | `TestAutoSaveOn_MultiContext_IndividualCommits` | ON | 每个 Add 独立提交，失败上下文不影响已提交的 |
| 10 | `TestAutoSaveOff_MultiContext_BatchedCommit` | OFF | 成功路径：批量提交 6 条策略到 3 个 Schema |

### 6.4 `InitPolicyAsync()` — 测试数据初始化

```csharp
private static async Task InitPolicyAsync(CasbinDbContext<int> context)
{
    // 清空旧数据（使用 AsNoTracking 避免并发异常）
    // 添加 5 条测试策略（同单元测试）
}
```

---

## 📋 7. `IntegrationTestCollection` — 集合定义

**文件**: `Integration/IntegrationTestCollection.cs` (20行)

```csharp
[CollectionDefinition("IntegrationTests", DisableParallelization = true)]
public class IntegrationTestCollection : ICollectionFixture<TransactionIntegrityTestFixture>
```

- `DisableParallelization = true` — **强制顺序执行**，防止 Schema 冲突
- 所有 `[Collection("IntegrationTests")]` 类共享同一个 `TransactionIntegrityTestFixture` 实例

---

## 📊 8. 示例文件

### 8.1 `multi_context_model.conf`

```
[request_definition]  r = sub, obj, act
[policy_definition]   p = sub, obj, act
[role_definition]     g = _, _
                       g2 = _, _    ← 额外的角色定义
[policy_effect]       e = some(where (p.eft == allow))
[matchers]            m = g(r.sub, p.sub) && r.obj == p.obj && r.act == p.act
```

与单元测试的 `rbac_model.conf` 相比，多了 `g2` 角色定义，用于测试多上下文中的三个策略类型路由。

---

## 🔑 9. 关键发现摘要

| 发现 | 测试来源 | 影响 |
|------|----------|------|
| 共享 DbConnection 对象可实现多上下文原子事务 | TransactionIntegrityTests #1-4 | 架构关键约束 |
| 仅连接字符串相同**不足以保证原子性**（需引用相等） | TransactionIntegrityTests #6 | 分库场景注意事项 |
| HasDefaultSchema() 在共享连接下需要进一步验证 | SchemaDistributionTests #2 | Schema 路由实现 |
| EnableAutoSave(false) 正确处理策略批处理 | AutoSaveTests #3,4 | Casbin.NET 2.19.1+ 已修复 |
| 分组策略 AutoSave OFF 行为可能异常 | AutoSaveTests #6,7 | ⚠️ 已知 Casbin.NET bug |
| PostgreSQL SAVEPOINT 错误通过 `UseTransaction(null)` 清除 | 核心库 SavePolicyAsync | 事务状态管理 |
