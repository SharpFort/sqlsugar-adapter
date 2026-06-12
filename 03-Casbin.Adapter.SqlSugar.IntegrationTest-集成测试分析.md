# 03 — Casbin.Adapter.SqlSugar.IntegrationTest 集成测试分析

> **项目名称:** Casbin.NET.Adapter.SqlSugar  
> **版本:** 1.0.3  
> **分析子模块:** `Casbin.Adapter.SqlSugar.IntegrationTest/`  
> **分析日期:** 2026-06-12  
> **分析方式:** 基于源代码的完整自动化提取（类、方法、配置项全覆盖）  
> **测试总数:** 19 个（TransactionIntegrityTests × 7 + AutoSaveTests × 10 + SchemaDistributionTests × 2）

---

## 📑 目录

- [1. 项目概览](#1-项目概览)
- [2. 项目配置](#2-项目配置)
  - [2.1 csproj 配置](#21-csproj-配置)
  - [2.2 xunit.runner.json](#22-xunitrunnerjson)
  - [2.3 Casbin 模型配置](#23-casbin-模型配置)
- [3. 测试基础设施（Fixture）](#3-测试基础设施fixture)
  - [3.1 TransactionIntegrityTestFixture](#31-transactionintegritytestfixture)
  - [3.2 IntegrationTestCollection](#32-integrationtestcollection)
- [4. 测试类详细分析](#4-测试类详细分析)
  - [4.1 TransactionIntegrityTests（7个测试）](#41-transactionintegritytests7个测试)
  - [4.2 SchemaDistributionTests（2个测试）](#42-schemadistributiontests2个测试)
  - [4.3 AutoSaveTests（10个测试）](#43-autosavetests10个测试)
- [5. 测试架构设计思想](#5-测试架构设计思想)
- [6. 测试覆盖率矩阵](#6-测试覆盖率矩阵)
- [7. 关键设计与实现细节](#7-关键设计与实现细节)
- [8. 运行方式与故障排除](#8-运行方式与故障排除)
- [9. CI/CD 排除原因](#9-cicd-排除原因)
- [10. 优化建议](#10-优化建议)

---

## 1. 项目概览

### 1.1 定位与目标

`Casbin.Adapter.SqlSugar.IntegrationTest` 是一个**独立的集成测试项目**，专门用于验证 SqlSugar 适配器在多上下文（Multi-Context）场景下的事务一致性保证。

| 维度 | 说明 |
|------|------|
| **测试类型** | 集成测试（需要真实 PostgreSQL 实例） |
| **独立项目原因** | 需单独的并行度配置，避免与单元测试的 PostgreSQL 数据库冲突 |
| **核心验证目标** | 当多个 `SqlSugarClient` 共享同一 `DbConnection` 时，跨上下文操作具有**原子性** |
| **数据库依赖** | PostgreSQL（本地 localhost:5432） |
| **CI/CD 状态** | 排除在 CI/CD 之外（仅用于本地验证） |

### 1.2 项目结构

```
Casbin.Adapter.SqlSugar.IntegrationTest/
├── Casbin.Adapter.SqlSugar.IntegrationTest.csproj    ← 项目配置
├── xunit.runner.json                                  ← xUnit 运行器配置
├── examples/
│   └── multi_context_model.conf                       ← Casbin 多上下文模型
└── Integration/
    ├── IntegrationTestCollection.cs                   ← 测试集合定义（串行执行）
    ├── TransactionIntegrityTestFixture.cs              ← 测试夹具（Fixture）
    ├── TransactionIntegrityTests.cs                    ← 事务完整性测试（7个）
    ├── SchemaDistributionTests.cs                      ← Schema 分布测试（2个）
    └── AutoSaveTests.cs                                ← AutoSave 行为测试（10个）
```

---

## 2. 项目配置

### 2.1 csproj 配置

| 配置项 | 值 | 说明 |
|--------|-----|------|
| `TargetFrameworks` | `net10.0;net9.0;net8.0` | 多目标框架支持 |
| `LangVersion` | `11` | C# 11 语言版本 |
| `IsPackable` | `false` | 不打包为 NuGet |
| `TestTfmsInParallel` | `false` | **关键：框架间顺序执行**，防止多 TFM 并行导致的数据库冲突 |
| `NoWarn` | `NU1701` | 忽略包兼容性警告 |

#### NuGet 依赖

| 包名 | 版本 | 用途 |
|------|------|------|
| `Casbin.NET` | 2.19.1 | Casbin 权限管理核心引擎 |
| `Npgsql` | 10.0.1 | PostgreSQL ADO.NET 驱动 |
| `Microsoft.NET.Test.Sdk` | 18.0.1 | .NET 测试 SDK |
| `xunit` | 2.9.3 | xUnit 测试框架 |
| `xunit.runner.visualstudio` | 3.1.5 | Visual Studio 测试运行器 |
| `SqlSugarCore` | 5.1.4.211 | SqlSugar ORM 核心库（按 TFM 条件引用） |

#### 项目引用与共享文件

| 类型 | 路径 | 说明 |
|------|------|------|
| `ProjectReference` | `..\Casbin.Adapter.SqlSugar\` | 引用主适配器库 |
| `Compile Link` | `Fixtures\ModelProvideFixture.cs` | 从 UnitTest 项目共享 |
| `Compile Link` | `Fixtures\PolicyTypeContextProvider.cs` | 从 UnitTest 项目共享 |
| `Compile Link` | `TestUtil.cs` | 从 UnitTest 项目共享测试工具类 |
| `None Link` | `examples\rbac_model.conf` | Casbin RBAC 模型 |
| `None Link` | `examples\rbac_policy.csv` | 测试策略数据 |

> **设计要点**: IntegrationTest 项目通过 `<Compile Include="..." Link="..." />` 机制共享 UnitTest 项目中的 Fixture 和工具类，避免代码重复，同时保持项目分离。

### 2.2 xunit.runner.json

```json
{
  "parallelizeTestCollections": true,
  "maxParallelThreads": -1
}
```

| 配置项 | 值 | 说明 |
|--------|-----|------|
| `parallelizeTestCollections` | `true` | 允许测试集合间并行执行 |
| `maxParallelThreads` | `-1` | 线程数无限制（使用 CPU 核心数） |

> ⚠️ **注意**: 虽然 xunit.runner.json 允许集合间并行，但 `IntegrationTestCollection` 通过 `[CollectionDefinition(..., DisableParallelization = true)]` 强制其内部的测试**串行执行**。这形成了一种"集合间并行、集合内串行"的执行模型。

### 2.3 Casbin 模型配置

`examples/multi_context_model.conf`:

```ini
[request_definition]
r = sub, obj, act

[policy_definition]
p = sub, obj, act

[role_definition]
g = _, _
g2 = _, _

[policy_effect]
e = some(where (p.eft == allow))

[matchers]
m = g(r.sub, p.sub) && r.obj == p.obj && r.act == p.act
```

| 配置段 | 说明 |
|--------|------|
| `request_definition` | 定义访问请求的三要素：主体(sub)、对象(obj)、动作(act) |
| `policy_definition` | 策略元组定义（sub, obj, act） |
| `role_definition` | 定义两组角色关系：`g` 和 `g2`，均为二元关系 |
| `policy_effect` | 策略效果：只要存在一条 allow 规则即放行 |
| `matchers` | 匹配器：验证 `r.sub` 通过 `g` 关系持有 `p.sub` 角色，且对象和动作匹配 |

> **设计说明**: 为什么定义 `g` 和 `g2` 两组角色关系？因为集成测试需要验证三路 Schema 路由——`g` 路由到 `groupings` Schema，`g2` 路由到 `roles` Schema。这是多上下文隔离场景的核心。

---

## 3. 测试基础设施（Fixture）

### 3.1 TransactionIntegrityTestFixture

**文件:** `Integration/TransactionIntegrityTestFixture.cs`（246行）

#### 3.1.1 类定义

```csharp
public class TransactionIntegrityTestFixture : IAsyncLifetime
```

实现 `IAsyncLifetime` 接口，使得 Fixture 具有异步初始化/清理生命周期。

#### 3.1.2 数据库配置（硬编码）

| 配置项 | 值 |
|--------|-----|
| Host | `localhost:5432` |
| Database | `casbin_integration_sqlsugar` |
| Username | `postgres` |
| Password | `postgres4all!` |

#### 3.1.3 Schema 定义（三个 PostgreSQL Schema）

```csharp
public const string PoliciesSchema   = "casbin_policies";
public const string GroupingsSchema  = "casbin_groupings";
public const string RolesSchema      = "casbin_roles";
```

#### 3.1.4 表结构 `casbin_rule`

```sql
CREATE TABLE {schema}.casbin_rule (
    id      SERIAL PRIMARY KEY,
    ptype   VARCHAR(254) NOT NULL,
    v0      VARCHAR(254),
    v1      VARCHAR(254),
    v2      VARCHAR(254),
    v3      VARCHAR(254),
    v4      VARCHAR(254),
    v5      VARCHAR(254),
    v6      VARCHAR(254),
    v7      VARCHAR(254),
    v8      VARCHAR(254),
    v9      VARCHAR(254),
    v10     VARCHAR(254),
    v11     VARCHAR(254),
    v12     VARCHAR(254),
    v13     VARCHAR(254),
    v14     VARCHAR(254)
);
CREATE INDEX ix_casbin_rule_ptype ON {schema}.casbin_rule (ptype);
```

| 字段 | 说明 |
|------|------|
| `id` | 自增主键，用于唯一标识每条策略记录 |
| `ptype` | 策略类型标识（`p`/`g`/`g2` 等），有索引加速查询 |
| `v0~v14` | 15个通用值字段（VARCHAR 254），对应 Casbin 策略的不同参数位置 |

#### 3.1.5 方法清单

| 方法 | 可见性 | 功能 |
|------|--------|------|
| `InitializeAsync()` | public | 创建 Schema → 运行迁移（建表） |
| `DisposeAsync()` | public | 清理（**已注释**，保留表结构供检查） |
| `RunMigrationsAsync()` | public | 为3个Schema创建 `casbin_rule` 表（先 DROP 再 CREATE） |
| `ClearAllPoliciesAsync()` | public | 使用 `TRUNCATE ... RESTART IDENTITY CASCADE` 清空所有数据 |
| `CountPoliciesInSchemaAsync(schemaName, policyType?)` | public | 统计指定 Schema 中某类型策略行数 |
| `InsertPolicyDirectlyAsync(schemaName, ptype, values[])` | public | 绕过 ORM 直接插入策略（冲突模拟用） |
| `DropTableAsync(schemaName)` | public | 删除指定 Schema 中的 `casbin_rule` 表（故障模拟用） |
| `CreateSchemasAsync()` | private | 创建3个 PostgreSQL Schema |
| `DropSchemasAsync()` | private | 删除3个 Schema 及其表 |

#### 3.1.6 生命周期

```
┌─────────────────────────────────────────────────────────────────┐
│                   Fixture 生命周期                              │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  InitializeAsync() ──→ CreateSchemasAsync()                    │
│                    ──→ RunMigrationsAsync() (DROP + CREATE × 3)│
│                                                                 │
│  每个测试前: ClearAllPoliciesAsync() (TRUNCATE × 3)            │
│                                                                 │
│  每个测试后: DisposeAsync() → RunMigrationsAsync() (恢复表结构) │
│                                                                 │
│  所有测试后: DisposeAsync() → (清理已注释)                      │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

> **关键设计决策**: 为什么使用 `TRUNCATE` 而不是 `DELETE`？
> 1. `TRUNCATE` 是 DDL 命令，速度更快（不记录每行删除）
> 2. `RESTART IDENTITY` 重置 SERIAL 自增计数器
> 3. `CASCADE` 处理潜在的外键依赖
> 4. 确保每个测试从完全干净的状态开始

### 3.2 IntegrationTestCollection

**文件:** `Integration/IntegrationTestCollection.cs`（21行）

```csharp
[CollectionDefinition("IntegrationTests", DisableParallelization = true)]
public class IntegrationTestCollection : ICollectionFixture<TransactionIntegrityTestFixture>
{
    // 此类无代码，仅用于应用 CollectionDefinition 特性
}
```

| 特性 | 说明 |
|------|------|
| `DisableParallelization = true` | **强制串行执行**，防止并行测试间的 Schema 冲突和竞争条件 |
| `ICollectionFixture<T>` | 所有标记 `[Collection("IntegrationTests")]` 的测试类**共享同一个** Fixture 实例 |

> **设计意图**: 三个测试类（TransactionIntegrityTests、SchemaDistributionTests、AutoSaveTests）共享同一个 Fixture，避免重复创建/销毁数据库资源，同时通过串行执行防止测试间的数据污染。

---

## 4. 测试类详细分析

### 4.1 TransactionIntegrityTests（7个测试）

**文件:** `Integration/TransactionIntegrityTests.cs`（766行）  
**命名空间:** `Casbin.Adapter.SqlSugar.UnitTest.Integration`

#### 4.1.1 类声明

```csharp
[Trait("Category", "Integration")]
[Collection("IntegrationTests")]
public class TransactionIntegrityTests : TestUtil, IClassFixture<TransactionIntegrityTestFixture>, IAsyncLifetime
```

| 基类/接口 | 作用 |
|-----------|------|
| `TestUtil` | 测试工具基类（共享自 UnitTest），提供 `AsList()`, `TestGetPolicy()` 等辅助方法 |
| `IClassFixture<T>` | 依赖注入 Fixture 实例 |
| `IAsyncLifetime` | 每个测试前后执行 `InitializeAsync`（清空数据）和 `DisposeAsync`（恢复表结构） |

#### 4.1.2 内部类定义

| 内部类 | 说明 |
|--------|------|
| `TestCasbinSqlSugarClient1` | 继承 `SqlSugarClient`，用于标识 Policy Schema 上下文 |
| `TestCasbinSqlSugarClient2` | 继承 `SqlSugarClient`，用于标识 Grouping Schema 上下文 |
| `TestCasbinSqlSugarClient3` | 继承 `SqlSugarClient`，用于标识 Role Schema 上下文 |
| `ThreeWayPolicyTypeProvider` | 实现 `ISqlSugarClientProvider`，三路策略类型路由 |

#### 4.1.3 ThreeWayPolicyTypeProvider — 策略路由核心

**路由规则:**

```
┌──────────────────┬──────────────────────────────┐
│  策略类型         │  目标 Schema                 │
├──────────────────┼──────────────────────────────┤
│  p, p2, p3...    │  casbin_policies.casbin_rule │
│  g               │  casbin_groupings.casbin_rule│
│  g2, g3, g4...   │  casbin_roles.casbin_rule    │
│  空/null          │  casbin_policies.casbin_rule │
└──────────────────┴──────────────────────────────┘
```

**关键方法:**

| 方法 | 返回值 | 说明 |
|------|--------|------|
| `GetClientForPolicyType(policyType)` | `ISqlSugarClient` | 根据策略类型返回对应 SQL Sugar 客户端 |
| `GetAllClients()` | `IEnumerable<ISqlSugarClient>` | 返回所有3个客户端（用于批量操作） |
| `GetSharedConnection()` | `DbConnection?` | 返回共享连接对象 |
| `GetTableNameForPolicyType(policyType)` | `string?` | **关键新增方法**（2024/12/21），返回完全限定的表名（`schema.table`），使 SqlSugar 的 `Insertable/Deleteable` 操作能通过 `.AS(tableName)` 动态指定目标表 |

#### 4.1.4 辅助方法

| 方法 | 说明 |
|------|------|
| `CreateEnforcerWithSharedConnectionAsync(clearPolicy)` | 创建共享连接的 Enforcer — 一个 `NpgsqlConnection` 被3个 `SqlSugarClient` 共享，通过 `client.Ado.Connection = connection` 注入 |
| `CreateEnforcerWithSeparateConnectionsAsync()` | 创建独立连接的 Enforcer — 3个客户端各有独立的 `NpgsqlConnection`，用于负面测试 |
| `CreateClient(connection, schemaName)` | 创建配置了 Schema 映射的 `SqlSugarClient`，并注入外部连接 |
| `CreateSchemaConfig(schemaName)` | 创建 `ConnectionConfig`，通过 `ConfigureExternalServices.EntityService` 设置 Schema 表名映射 |

#### 4.1.5 测试详情

---

##### 测试 1: `SavePolicy_WithSharedConnection_ShouldWriteToAllContextsAtomically`

| 属性 | 内容 |
|------|------|
| **测试目标** | 验证共享连接下的原子写入 |
| **测试类型** | 正向测试（Happy Path） |
| **测试步骤** | 1. 创建共享连接 Enforcer<br>2. 添加 p/g/g2 三种策略到内存<br>3. 调用 `SavePolicyAsync()`<br>4. 验证三个 Schema 各有1条对应策略 |
| **预期结果** | `casbin_policies(p)=1`, `casbin_groupings(g)=1`, `casbin_roles(g2)=1` |
| **验证方式** | `_fixture.CountPoliciesInSchemaAsync()` 直接 SQL 查询 |

---

##### 测试 2: `MultiContextSetup_WithSharedConnection_ShouldShareSamePhysicalConnection`

| 属性 | 内容 |
|------|------|
| **测试目标** | 验证三个 SqlSugar 客户端引用了同一个 DbConnection 对象 |
| **测试类型** | 基础设施验证 |
| **测试步骤** | 1. 创建共享连接<br>2. 创建3个客户端并注入同一连接<br>3. 获取各自 `Ado.Connection`<br>4. 验证引用相等性 |
| **预期结果** | `Assert.Same(connection, policyConn)` — 所有连接引用同一个对象 |
| **关键断言** | `Assert.Same(policyConn, groupingConn); Assert.Same(groupingConn, roleConn)` |

---

##### 测试 3: `SavePolicy_WhenTableDroppedInOneContext_ShouldRollbackAllContexts` ⭐

| 属性 | 内容 |
|------|------|
| **测试目标** | 验证表被删除后的原子回滚 |
| **测试类型** | **关键回滚测试** |
| **重要性** | 🔴 **最关键的测试之一** |
| **测试步骤** | 1. 创建共享连接 Enforcer<br>2. **`enforcer.EnableAutoSave(false)`** ← 关键<br>3. 添加策略到内存<br>4. **删除 roles 表**（制造故障）<br>5. 调用 `SavePolicyAsync()`（预期抛异常）<br>6. 恢复表结构<br>7. 验证所有 Schema 数据为0 |
| **预期结果** | 异常被捕获，`policiesCount=0, groupingsCount=0, rolesCount=0` |
| **关键机制** | AutoSave OFF → 策略仅在内存 → SavePolicy 时开启事务 → roles 表缺失引发异常 → **整个事务原子回滚** |

> ⚠️ **为什么必须 `EnableAutoSave(false)`？**  
> 如果 AutoSave 为 ON（默认），`AddPolicyAsync()` 会立即将策略提交到数据库，后续 `SavePolicyAsync()` 失败时只能回滚 DELETE 操作，而无法回滚已提交的 INSERT。只有关闭 AutoSave，所有操作才在同一事务内。

---

##### 测试 4: `SavePolicy_WhenTableMissingInOneContext_ShouldRollbackAllContexts`

| 属性 | 内容 |
|------|------|
| **测试目标** | 验证动态表缺失场景的原子回滚 |
| **测试类型** | 回滚验证（与测试3类似但强调"运行时动态缺失"） |
| **关键区别** | 强调故障发生在 Enforcer 创建**之后**、SavePolicy 调用**之前**的动态场景 |
| **预期结果** | 同测试3：全部回滚，所有 Schema 计数为0 |

---

##### 测试 5: `MultipleSaveOperations_WithSharedConnection_ShouldMaintainDataConsistency`

| 属性 | 内容 |
|------|------|
| **测试目标** | 验证多次增量保存操作的数据一致性 |
| **测试类型** | 一致性验证 |
| **测试步骤** | 1. 第1次保存：alice 的 p + g 策略<br>2. 第2次保存：bob 的 p + g 策略<br>3. 第3次保存：charlie 的 p + g 策略<br>4. 验证最终 p=3, g=3<br>5. 验证 `Enforce()` 权限判定正确 |
| **预期结果** | 3条 p 策略 + 3条 g 策略，权限判定全部通过 |

---

##### 测试 6: `SavePolicy_WithSeparateConnections_ShouldNotBeAtomic`

| 属性 | 内容 |
|------|------|
| **测试目标** | 证明独立连接**不具备**原子性 |
| **测试类型** | 🔴 **负面测试（Negative Test）** |
| **测试步骤** | 1. 创建独立连接的 Enforcer<br>2. 添加策略到内存<br>3. 删除 roles 表<br>4. 调用 `SavePolicyAsync()`（预期抛异常）<br>5. 验证前两个 Schema 的数据**未被回滚** |
| **预期结果** | `policiesCount=1, groupingsCount=1, rolesCount=0` — 产生"部分提交" |
| **核心结论** | **共享 `DbConnection` 对象是实现跨 Schema 原子性的必要条件** |

---

##### 测试 7: `SavePolicy_ShouldReflectDatabaseStateNotCasbinMemory`

| 属性 | 内容 |
|------|------|
| **测试目标** | 验证 Casbin 内存状态正确反映数据库持久化状态 |
| **测试类型** | 状态一致性验证 |
| **测试步骤** | 1. 第一阶段：Enforcer1 写入策略并关闭<br>2. 第二阶段：Enforcer2 从数据库加载策略<br>3. 尝试重复添加相同策略<br>4. 验证 Casbin 内存检查返回 false<br>5. 验证数据库计数不变 |
| **预期结果** | `addedPolicy=false, addedGrouping=false`, `policiesCount=1, groupingsCount=1` |

---

### 4.2 SchemaDistributionTests（2个测试）

**文件:** `Integration/SchemaDistributionTests.cs`（392行）

#### 4.2.1 类声明

```csharp
[Trait("Category", "Integration")]
[Collection("IntegrationTests")]
public class SchemaDistributionTests : TestUtil, IClassFixture<TransactionIntegrityTestFixture>, IAsyncLifetime
```

#### 4.2.2 内部类定义

| 内部类 | 说明 |
|--------|------|
| `TestCasbinSqlSugarClient` | 继承 `SqlSugarClient`，用作多 Schema 测试上下文标识 |
| `ThreeWaySqlSugarClientProvider` | 实现 `ISqlSugarClientProvider`，使用 `switch` 表达式进行三路路由 |

#### 4.2.3 ThreeWaySqlSugarClientProvider — 路由规则（switch 表达式版本）

```csharp
public ISqlSugarClient GetClientForPolicyType(string policyType) => policyType switch
{
    "p"  => _policyClient,
    "g"  => _groupingClient,
    "g2" => _roleClient,
    _    => _policyClient
};

public string? GetTableNameForPolicyType(string policyType) => policyType switch
{
    "p"  => "casbin_policies.casbin_rule",
    "g"  => "casbin_groupings.casbin_rule",
    "g2" => "casbin_roles.casbin_rule",
    _    => "casbin_policies.casbin_rule"
};
```

> **与 TransactionIntegrityTests 中 Provider 的差异**: SchemaDistributionTests 使用的是 `switch` 表达式（精确匹配），而 TransactionIntegrityTests 使用的是 `StartsWith` 前缀匹配（支持 `p2`/`p3`/`g3`/`g4` 等扩展类型）。

#### 4.2.4 测试详情

---

##### 测试 1: `SavePolicy_SeparateConnections_ShouldDistributeAcrossSchemas`

| 属性 | 内容 |
|------|------|
| **测试目标** | 基准测试：独立连接下策略正确分布 |
| **测试类型** | 基准/对照测试（Baseline） |
| **测试步骤** | 1. 创建3个独立连接的 SqlSugar 客户端<br>2. 验证连接对象不同（`Assert.False(ReferenceEquals(...))`）<br>3. 添加 p/g/g2 策略<br>4. 调用 `SavePolicyAsync()`<br>5. 验证各 Schema 各有1条对应策略 |
| **预期结果** | `casbin_policies=1, casbin_groupings=1, casbin_roles=1` |
| **数据库验证** | 直接 SQL 查询各 Schema 中的策略分布 |

---

##### 测试 2: `SavePolicy_SharedConnection_ShouldDistributeAcrossSchemas` ⭐

| 属性 | 内容 |
|------|------|
| **测试目标** | **关键测试：共享连接下策略仍正确分布到各自 Schema** |
| **测试类型** | 关键验证测试 |
| **测试步骤** | 1. 创建共享 `NpgsqlConnection`<br>2. 创建3个客户端并注入同一连接<br>3. 验证引用相等性（`Assert.True(ReferenceEquals(...))`）<br>4. 添加 p/g/g2 策略<br>5. 调用 `SavePolicyAsync()`<br>6. 验证正确的 Schema 分布 |
| **预期结果** | 每个 Schema 各有1条策略，无混合/泄漏 |
| **核心结论** | Schema 限定的表名（`schema.table`）在共享连接下工作正常，共享连接不会破坏 Schema 隔离 |

---

### 4.3 AutoSaveTests（10个测试）

**文件:** `Integration/AutoSaveTests.cs`（1219行）

#### 4.3.1 类声明

```csharp
[Trait("Category", "Integration")]
[Collection("IntegrationTests")]
public class AutoSaveTests : TestUtil, IAsyncLifetime
```

> **注意**: AutoSaveTests 通过构造函数注入 Fixture（而非 `IClassFixture<T>` 接口），它与 TransactionIntegrityTests 共享同一个 Fixture 实例。

#### 4.3.2 配套类定义

| 类 | 说明 |
|----|------|
| `TestCasbinSqlSugarContext` (abstract) | SqlSugar 上下文基类，封装 `ConnectionConfig` 生成逻辑 |
| `TestCasbinDbContext1/2/3` | 继承 `TestCasbinSqlSugarContext`，用作三个不同 Schema 的标识 |
| `ThreeWaySqlSugarClientProvider` | 实现 `ISqlSugarClientProvider`，多上下文 AutoSave 测试的 Provider |

#### 4.3.3 辅助方法

| 方法 | 说明 |
|------|------|
| `CreateConfig(connection, schema)` | 创建带共享连接的 `ConnectionConfig`，设置 Schema 表名映射 |
| `CreateClientWithSchema(connectionString, schemaName)` | 创建带 searchpath 的客户端（Tier A 策略） |
| `InitPolicyAsync(db)` | 静态方法：清空并初始化4条 p 策略 + 1条 g 策略的测试数据 |

#### 4.3.4 测试详情

---

##### 测试 1: `TestPolicyAutoSaveOn`

| 属性 | 内容 |
|------|------|
| **测试目标** | 验证 AutoSave ON（默认）时策略立即持久化 |
| **测试类型** | 功能验证 |
| **覆盖操作** | Load → Add → Remove → RemoveFiltered → Update → AddPolicies(批量) → RemovePolicies(批量) |
| **验证方式** | 每个操作后同时验证 Casbin 内存状态（`TestGetPolicy`）和数据库行数（`db.Queryable<CasbinRule>().CountAsync()`） |
| **缓存行数变化** | 5 → 6 → 5 → 3 → 3 → 5 → 3 |

---

##### 测试 2: `TestPolicyAutoSaveOnAsync`

| 属性 | 内容 |
|------|------|
| **测试目标** | 验证 AutoSave ON 时异步操作立即持久化 |
| **覆盖操作** | Load(同步) → AddAsync → RemoveAsync |
| **验证方式** | 每个异步操作后验证数据库行数 |

---

##### 测试 3: `TestPolicyAutoSaveOff`

| 属性 | 内容 |
|------|------|
| **测试目标** | 验证 AutoSave OFF 时策略延迟到 `SavePolicy` 才提交 |
| **测试步骤** | 1. 初始化5条策略<br>2. `EnableAutoSave(false)`<br>3. `AddPolicy("charlie", "data3", "read")`<br>4. 验证数据库仍为5条<br>5. 验证 charlie 不在数据库中<br>6. `SavePolicy()`<br>7. 验证数据库变为6条<br>8. 验证 charlie 已存在 |
| **预期结果** | AutoSave OFF 使策略批量在内存中，SavePolicy 时一次性提交 |

---

##### 测试 4: `TestPolicyAutoSaveOffAsync`

| 属性 | 内容 |
|------|------|
| **测试目标** | 验证 AutoSave OFF 时异步操作延迟提交 |
| **与测试3区别** | 使用 `AddPolicyAsync` 而非 `AddPolicy` |

---

##### 测试 5: `TestGroupingPolicyAutoSaveOn`

| 属性 | 内容 |
|------|------|
| **测试目标** | 验证分组策略在 AutoSave ON 时立即持久化 |
| **数据 Schema** | `casbin_groupings` |
| **测试步骤** | 1. 加载5条策略（含1条 g）<br>2. `AddGroupingPolicyAsync("bob", "data2_admin")`<br>3. 验证内存和数据库均变为6条<br>4. 验证 bob 的 g 策略存在 |

---

##### 测试 6: `TestGroupingPolicyAutoSaveOff`

| 属性 | 内容 |
|------|------|
| **测试目标** | 验证分组策略在 AutoSave OFF 时延迟提交 |
| **关键验证** | 同时添加 p 和 g 策略（均不提交），SavePolicy 时两者一起提交 |
| **版本依赖** | 需要 Casbin.NET ≥ 2.19.1（修复了 AutoSave bug） |
| **预期结果** | 数据库从5 → 5（未提交）→ 7（SavePolicy 后） |

---

##### 测试 7: `TestGroupingPolicyAutoSaveOffAsync`

| 属性 | 内容 |
|------|------|
| **测试目标** | 验证分组策略在 AutoSave OFF 时异步操作延迟提交 |
| **与测试6区别** | 使用 `AddPolicyAsync` 和 `AddGroupingPolicyAsync` |

---

##### 测试 8: `TestAutoSaveOff_MultiContext_RollbackOnFailure` ⭐

| 属性 | 内容 |
|------|------|
| **测试目标** | 验证 AutoSave OFF + 多上下文时的**原子回滚** |
| **测试类型** | 关键回滚测试 |
| **测试步骤** | 1. 创建共享连接的3个 SqlSugar 客户端<br>2. 创建 Enforcer，`EnableAutoSave(false)`<br>3. 添加2条 p + 2条 g + 2条 g2 策略（内存）<br>4. 验证数据库全为0<br>5. 删除 roles 表<br>6. `SavePolicyAsync()` → 预期异常<br>7. 验证前两个 Schema 数据为0（已回滚） |
| **预期结果** | `policiesCount=0, groupingsCount=0` |
| **核心结论** | AutoSave OFF 是实现原子回滚测试**的必要条件** |

---

##### 测试 9: `TestAutoSaveOn_MultiContext_IndividualCommits`

| 属性 | 内容 |
|------|------|
| **测试目标** | 验证 AutoSave ON 时**每个操作独立提交**，无跨上下文原子性 |
| **测试类型** | 负面/对比测试 |
| **测试步骤** | 1. 创建共享连接的3个客户端<br>2. 创建 Enforcer（AutoSave ON 默认），调用 `LoadPolicyAsync()` 激活<br>3. 添加2条 p 策略 → 验证已提交<br>4. 添加2条 g 策略 → 验证已提交<br>5. 删除 roles 表<br>6. 添加 g2 策略 → 预期异常<br>7. 验证前两个 Schema 数据**未被回滚** |
| **预期结果** | `policiesCount=2, groupingsCount=2`（数据已独立提交） |
| **核心结论** | AutoSave ON 时每个 `AddPolicy` 立即提交，无法实现跨上下文的原子回滚 |

---

##### 测试 10: `TestAutoSaveOff_MultiContext_BatchedCommit`

| 属性 | 内容 |
|------|------|
| **测试目标** | 验证 AutoSave OFF 的成功路径：批量提交 |
| **测试类型** | 正向测试 |
| **测试步骤** | 1. 创建共享连接的3个客户端<br>2. 创建 Enforcer，`EnableAutoSave(false)`<br>3. 添加2条 p + 2条 g + 2条 g2 策略<br>4. 验证数据库全为0<br>5. `SavePolicyAsync()`<br>6. 验证每个 Schema 各有2条策略 |
| **预期结果** | `policiesCount=2, groupingsCount=2, rolesCount=2` |
| **核心结论** | AutoSave OFF + 共享连接 = 跨上下文原子批量提交 |

---

## 5. 测试架构设计思想

### 5.1 三层验证模型

```
┌──────────────────────────────────────────────────────────────┐
│                     测试架构三层模型                          │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  第一层：事务原子性验证（TransactionIntegrityTests）         │
│  ├─ 正向：共享连接原子写入 ✅                                │
│  ├─ 正向：多次保存数据一致性 ✅                              │
│  ├─ 回滚：表删除后原子回滚 ✅                                │
│  ├─ 回滚：动态缺失原子回滚 ✅                                │
│  ├─ 负面：独立连接不原子 ✅                                  │
│  └─ 状态：内存反映真实DB状态 ✅                              │
│                                                              │
│  第二层：Schema 路由验证（SchemaDistributionTests）          │
│  ├─ 基准：独立连接下正确分布 ✅                              │
│  └─ 关键：共享连接下不破坏隔离 ✅                            │
│                                                              │
│  第三层：AutoSave 行为验证（AutoSaveTests）                  │
│  ├─ 单上下文：ON/OFF 的 p 和 g 策略行为                      │
│  ├─ 多上下文：ON 时独立提交、OFF 时批量提交                  │
│  └─ 多上下文：OFF 时故障原子回滚                             │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

### 5.2 核心设计模式

| 模式 | 实现 | 目的 |
|------|------|------|
| **共享 Fixture** | `ICollectionFixture<TransactionIntegrityTestFixture>` | 避免重复创建/销毁数据库资源 |
| **三路 Provider** | `ISqlSugarClientProvider` 的三种实现 | 模拟真实多租户/多 Schema 隔离场景 |
| **连接注入** | `client.Ado.Connection = sharedConnection` | SqlSugar 特有的外部连接注入方式 |
| **Schema 限定的表名** | `EntityService` 设置 `DbTableName = "schema.table"` | 实现跨 Schema 的表访问 |
| **AutoSave 开关** | `enforcer.EnableAutoSave(false)` | 控制策略提交时机，是实现原子回滚的前提 |

### 5.3 测试隔离策略

```
┌───────────────────────────────────────────────────────────────┐
│                    测试隔离策略                                │
├───────────────────────────────────────────────────────────────┤
│                                                               │
│  测试前 (InitializeAsync):                                    │
│    → TRUNCATE TABLE × 3 (彻底清空)                            │
│                                                               │
│  测试中:                                                      │
│    → 每个测试创建独立的 Enforcer/NpgsqlConnection             │
│    → 通过 try/finally 确保连接释放                            │
│                                                               │
│  测试后 (DisposeAsync):                                       │
│    → RunMigrationsAsync() (恢复被删除的表结构)                 │
│                                                               │
│  Fixture 全局:                                                │
│    → InitializeAsync() 创建 Schema + 表                       │
│    → DisposeAsync() 清理已注释（保留表用于检查）               │
│                                                               │
└───────────────────────────────────────────────────────────────┘
```

---

## 6. 测试覆盖率矩阵

### 6.1 按测试类

| 测试类 | 测试数 | 覆盖场景 | 状态 |
|--------|--------|----------|------|
| `TransactionIntegrityTests` | 7 | 共享连接原子性、回滚验证、连接共享验证、一致性、负面测试、状态验证 | ✅ |
| `SchemaDistributionTests` | 2 | 独立/共享连接下的 Schema 路由正确性 | ✅ |
| `AutoSaveTests` | 10 | AutoSave ON/OFF、p/g策略、单/多上下文、同步/异步、故障回滚 | ✅ |

### 6.2 按验证维度

| 维度 | 测试覆盖 |
|------|----------|
| **原子性（Atomicity）** | Test 1,3,4,5,6, AutoSave 8,9,10 |
| **一致性（Consistency）** | Test 5,7 |
| **隔离性（Isolation）** | SchemaDistribution 1,2 |
| **持久性（Durability）** | Test 7, AutoSave 1-7 |
| **共享连接正确性** | Test 1,2, SchemaDistribution 2 |
| **Schema 路由正确性** | SchemaDistribution 1,2 |
| **AutoSave 行为正确性** | AutoSave 1-10 |
| **同步/异步双模式** | AutoSave 1-7 均覆盖同步和异步 |
| **负面测试（Negative Tests）** | Test 6, AutoSave 9 |

### 6.3 按策略类型

| 策略类型 | 覆盖测试 |
|----------|----------|
| `p`（普通策略） | 全部测试 |
| `g`（分组策略） | Test 1-7, AutoSave 5-10, SchemaDistribution 1-2 |
| `g2`（命名分组） | Test 1,3,4,6, AutoSave 8-10, SchemaDistribution 1-2 |

---

## 7. 关键设计与实现细节

### 7.1 SqlSugar 特有的外部连接注入

不同于 Entity Framework Core 通过 `DbContextOptionsBuilder.UseNpgsql(connection)` 注入连接，SqlSugar 通过直接设置 `Ado.Connection` 属性实现：

```csharp
var client = new SqlSugarClient(config);
client.Ado.Connection = sharedConnection;  // 注入外部 Npgsql 连接
```

**注意事项:**
- `ConnectionConfig.ConnectionString` 仍需设置（用于 `CodeFirst.InitTables()` 等操作）
- `IsAutoCloseConnection` 必须设为 `false`（由外部管理生命周期）
- 每次创建客户端后都必须单独注入

### 7.2 Schema 限定的表名映射

SqlSugar 通过 `ConfigureExternalServices.EntityService` 实现动态表名/Schema 映射：

```csharp
ConfigureExternalServices = new ConfigureExternalServices
{
    EntityService = (c, p) =>
    {
        if (p.EntityName == nameof(CasbinRule))
        {
            p.DbTableName = $"{schemaName}.casbin_rule";
        }
    }
}
```

这与 EFCore 的 `modelBuilder.Entity<CasbinRule>().ToTable("casbin_rule", schema: "casbin_policies")` 等效，但通过回调而非 Fluent API 实现。

### 7.3 GetTableNameForPolicyType 方法（2024/12/21 新增）

这是 `ISqlSugarClientProvider` 接口中的关键新增方法，使 SqlSugarAdapter 在执行 `Insertable/Deleteable` 时能通过 `.AS(tableName)` 动态指定目标表：

```csharp
// 在 SqlSugarAdapter 内部
var tableName = provider.GetTableNameForPolicyType(policyType);
db.Insertable(rule).AS(tableName).ExecuteCommand();
```

这解决了早期集成测试中多 Schema 分布测试失败的问题。

### 7.4 EnableAutoSave 与事务原子性的关系

```
AutoSave ON (默认):
  AddPolicyAsync()  → 立即提交到数据库 ❌ 无法回滚
  SavePolicyAsync() → 仅回滚 DELETE 操作
  
AutoSave OFF:
  AddPolicyAsync()  → 仅在内存 ✅ 可回滚
  SavePolicyAsync() → 一次性提交所有操作 ✅ 原子性保证
```

**代码证据**: `TransactionIntegrityTests.cs` 第302行和第370行：

```csharp
enforcer.EnableAutoSave(false);
```

### 7.5 TRUNCATE vs DELETE 的测试隔离

Fixture 使用 `TRUNCATE TABLE ... RESTART IDENTITY CASCADE` 而非 `DELETE FROM`：

| 对比维度 | TRUNCATE | DELETE |
|----------|----------|--------|
| 速度 | 快（DDL，不记录每行） | 慢（DML，逐行记录WAL） |
| 自增序列 | 重置（RESTART IDENTITY） | 不重置 |
| 表级锁 | 获取 | 行级锁 |
| 依赖处理 | CASCADE 处理外键 | 需手动处理 |

---

## 8. 运行方式与故障排除

### 8.1 先决条件

1. **PostgreSQL 安装并运行**在 localhost:5432
2. **创建数据库**: `psql -U postgres -c "CREATE DATABASE casbin_integration_sqlsugar;"`
3. **默认凭据**: Username=`postgres`, Password=`postgres4all!`（或在 Fixture 中修改）

### 8.2 运行命令

```bash
# 运行所有集成测试
dotnet test --filter "Category=Integration"

# 运行特定测试类
dotnet test --filter "FullyQualifiedName~TransactionIntegrityTests"

# 运行特定测试方法
dotnet test --filter "FullyQualifiedName~SavePolicy_WhenTableDroppedInOneContext_ShouldRollbackAllContexts"

# 指定目标框架运行
dotnet test --filter "Category=Integration" -f net10.0

# 详细输出
dotnet test --filter "Category=Integration" --verbosity normal
```

### 8.3 常见错误与解决

| 错误信息 | 原因 | 解决方案 |
|----------|------|----------|
| `could not connect to server` | PostgreSQL 未运行 | 启动 PostgreSQL 服务 |
| `database "casbin_integration_sqlsugar" does not exist` | 数据库未创建 | `CREATE DATABASE casbin_integration_sqlsugar;` |
| `password authentication failed` | 凭据不匹配 | 更新 Fixture 中的 `ConnectionString` |
| `relation "casbin_rule" does not exist` | 表未创建 | 确保用户有 CREATE 权限或手动创建 Schema |

---

## 9. CI/CD 排除原因

所有集成测试标记为 `[Trait("Category", "Integration")]`，被**排除在 CI/CD 之外**：

| 原因 | 说明 |
|------|------|
| **流水线所有权** | CI/CD 流水线不由项目维护者管理 |
| **外部依赖** | 需要配置良好的 PostgreSQL 实例 |
| **定位** | 仅用于**本地验证**，证明文档所述的事务保证工作正常 |

> 运行方式：`dotnet test --filter "Category=Integration"`

---

## 10. 优化建议

| 优先级 | 方向 | 具体问题 | 建议 |
|--------|------|----------|------|
| 🔴 高 | 可配置性 | 数据库连接字符串硬编码在 Fixture 中 | 支持环境变量或配置文件覆盖（如 `CASBIN_TEST_CONNECTION_STRING`） |
| 🟡 中 | 可移植性 | 仅支持 PostgreSQL | 提供 SQL Server/MySQL 的备选 Fixture，扩大测试覆盖 |
| 🟡 中 | Provider 一致性 | `ThreeWayPolicyTypeProvider` 在两个测试类中各定义一次 | 提取为共享类，减少代码重复 |
| 🟡 中 | 文档内联 | AutoSaveTests 中大量注释描述版本依赖（2.19.1+） | 集中到 README 中管理，避免散落 |
| 🟢 低 | 测试输出 | `_output.WriteLine` 仅用于调试 | 考虑使用 xUnit `ITestOutputHelper` 的结构化日志 |
| 🟢 低 | 清理策略 | `DisposeAsync` 的 Schema 清理被注释 | 添加配置文件选项控制清理行为 |

---

## 📎 附录：文件统计

| 文件 | 行数 | 功能 |
|------|------|------|
| `TransactionIntegrityTestFixture.cs` | 246 | 测试夹具（Schema/表管理） |
| `TransactionIntegrityTests.cs` | 766 | 7个事务完整性测试 |
| `SchemaDistributionTests.cs` | 392 | 2个 Schema 分布测试 |
| `AutoSaveTests.cs` | 1219 | 10个 AutoSave 行为测试 |
| `IntegrationTestCollection.cs` | 21 | 测试集合定义 |
| `Casbin.Adapter.SqlSugar.IntegrationTest.csproj` | 61 | 项目配置 |
| `xunit.runner.json` | 5 | xUnit 运行器配置 |
| `multi_context_model.conf` | 15 | Casbin 模型定义 |
| **总计** | **2726** | **19个集成测试** |

---

> 📖 **相关文档导航:**
> - [00 — 总体分析报告](./00-总体分析报告.md)
> - [01 — 核心库分析：Casbin.Adapter.SqlSugar](./01-Casbin.Adapter.SqlSugar-核心库分析.md)
> - [02 — 单元测试分析：Casbin.Adapter.SqlSugar.UnitTest](./02-Casbin.Adapter.SqlSugar.UnitTest-单元测试分析.md)
> - [04 — 根目录配置文件分析](./04-根目录配置文件分析.md)
>
> *本文档由 Hermes Agent 基于源代码自动提取生成，分析覆盖了全部6个源文件、19个测试方法、3个内部Provider实现、所有配置项及辅助方法。*
