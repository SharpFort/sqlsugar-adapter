# 多上下文事务一致性集成测试

该目录包含集成测试，用于验证 SqlSugar 适配器多上下文功能的事务一致性保证。

## 独立测试项目

这些集成测试位于一个**独立的测试项目** (`Casbin.Adapter.SqlSugar.IntegrationTest`) 中，以启用顺序框架执行。

**为什么要独立？**
- 集成测试**按顺序**（一次一个）运行框架，以避免 PostgreSQL 数据库冲突
- 单元测试继续**并行**运行框架以加快执行速度
- .NET 9+ 默认并行运行多目标测试 - 这种分离允许不同的配置

**项目设置：**
- `<TestTfmsInParallel>false</TestTfmsInParallel>` - 框架顺序执行
- 共享单个 PostgreSQL 数据库：`casbin_integration_sqlsugar`
- 在测试集合上使用 `DisableParallelization = true` 进行框架内的顺序执行

## 目的

这些通过证明当多个 `SqlSugarClient` 实例共享同一个 `DbConnection` 对象时，跨上下文的操作是**原子性**的 - 它们要么全部成功，要么全部一起失败。

## 先决条件

### 1. 安装 PostgreSQL

你需要在于本地开发机器上安装 PostgreSQL。

**安装 PostgreSQL：**
- **Windows**: 从 [postgresql.org](https://www.postgresql.org/download/windows/) 下载
- **macOS**: `brew install postgresql@17` (或使用 [Postgres.app](https://postgresapp.com/))
- **Linux**: `sudo apt-get install postgresql` (Debian/Ubuntu) 或同等方式

### 2. 数据库设置

创建测试数据库：

```bash
# 连接到 PostgreSQL (默认超级用户是 'postgres')
psql -U postgres

# 创建测试数据库
CREATE DATABASE casbin_integration_sqlsugar;

# 退出 psql
\q
```

或者使用一行命令：

```bash
psql -U postgres -c "CREATE DATABASE casbin_integration_sqlsugar;"
```

### 3. 连接凭据

测试使用这些默认凭据：
- **Host**: `localhost:5432`
- **Database**: `casbin_integration_sqlsugar`
- **Username**: `postgres`
- **Password**: `postgres4all!`

**如果你的 PostgreSQL 使用不同的凭据**，请更新 [TransactionIntegrityTestFixture.cs](TransactionIntegrityTestFixture.cs) 中的连接字符串：

```csharp
ConnectionString = "Host=localhost;Database=casbin_integration_sqlsugar;Username=YOUR_USER;Password=YOUR_PASSWORD";
```

## 运行测试

支持 .NET 8.0, .NET 9.0 和 .NET 10.0。

### 运行所有集成测试

```bash
dotnet test --filter "Category=Integration"
```

### 运行特定测试

```bash
dotnet test --filter "FullyQualifiedName~SavePolicy_WithSharedConnection_ShouldWriteToAllContextsAtomically"
```

### 运行特定框架 (支持 net8.0 / net9.0 / net10.0)

```bash
dotnet test --filter "Category=Integration" -f net10.0
```

## 测试架构

### 测试夹具 (Fixture)

[TransactionIntegrityTestFixture](TransactionIntegrityTestFixture.cs) 自动执行：
1. 创建 3 个 PostgreSQL 模式 (schema)：`casbin_policies`, `casbin_groupings`, `casbin_roles`
2. 在每个模式中创建表
3. 在每次测试前清除所有数据
4. 在所有测试完成后清理模式

### 测试组织

集成测试组织在 3 个测试类中：

| 测试类 | 测试数 | 目的 |
|------------|-------|---------|
| `TransactionIntegrityTests` | 7 | 多上下文事务原子性和回滚 |
| `AutoSaveTests` | 10 | Casbin.NET 自动保存 (AutoSave) 行为验证 |
| `SchemaDistributionTests` | 2 | shared connections (共享连接) 下的模式路由 |

**总计:** 19 个集成测试

测试使用一个三路上下文提供者 (three-way context provider) 进行路由：
- **p policies** → `casbin_policies` 模式
- **g groupings** → `casbin_groupings` 模式
- **g2 roles** → `casbin_roles` 模式

这模拟了真实世界的多上下文场景，其中不同的策略类型出于合规性、多租户或组织要求而分开存储。

## 测试覆盖率

| 测试 | 证明内容 |
|------|----------------|
| `SavePolicy_WithSharedConnection_ShouldWriteToAllContextsAtomically` | 在单个原子事务中将策略写入 3 个模式 |
| `MultiContextSetup_WithSharedConnection_ShouldShareSamePhysicalConnection` | 引用相等性确认 DbConnection 对象共享 |
| `SavePolicy_WhenTableMissingInOneContext_ShouldRollbackAllContexts` | 严重故障导致完全回滚 |
| `MultipleSaveOperations_WithSharedConnection_ShouldMaintainDataConsistency` | 多次操作随时间推移保持一致性 |
| `SavePolicy_WithSeparateConnections_ShouldNotBeAtomic` | **负面测试**: 证明单独的连接**不**具备原子性 |
| `SavePolicy_ShouldReflectDatabaseStateNotCasbinMemory` | 测试验证实际数据库状态，而不仅仅是 Casbin 内存 |

### SchemaDistributionTests

**文件:** [SchemaDistributionTests.cs](SchemaDistributionTests.cs)
**测试数:** 2
**状态:** ✅ 全部通过

**目的:**

这些测试验证当使用共享连接时，SqlSugar 适配器是否正确地将策略路由到它们指定的模式，确保维持模式隔离。

**测试覆盖率:**

| 测试 | 目的 | 状态 |
|------|---------|--------|
| `SavePolicy_SeparateConnections_ShouldDistributeAcrossSchemas` | 使用独立连接的基准行为 | ✅ 通过 |
| `SavePolicy_SharedConnection_ShouldDistributeAcrossSchemas` | 使用共享连接的模式路由 | ✅ 通过 |

**它们测试什么:**

1.  **模式路由 (Schema Routing):**
    *   `p` 策略 → `casbin_policies` 模式
    *   `g` 策略 → `casbin_groupings` 模式
    *   `g2` 策略 → `casbin_roles` 模式

2.  **共享连接影响:**
    *   验证适配器根据上下文/策略类型返回正确的模式名称
    *   确认共享连接不会破坏模式隔离
    *   验证多上下文路由正常工作

3.  **数据库验证:**
    *   直接对每个模式进行 SQL 查询
    *   统计每个模式中的策略类型
    *   断言正确的分布 (例如: `casbin_policies` 模式中只有 `p` 类型)

**为什么这很重要:**

当为了原子事务使用共享连接时，每个上下文仍必须路由到其正确的模式。这些测试证明共享连接对象不会意外合并上下文或路由到错误的模式。

**运行测试:**

```bash
# 运行两个 SchemaDistributionTests
dotnet test -f net10.0 --filter "FullyQualifiedName~SchemaDistributionTests" --verbosity normal

# 运行特定测试
dotnet test -f net10.0 --filter "FullyQualifiedName~SavePolicy_SharedConnection_ShouldDistributeAcrossSchemas" --verbosity normal
```

### AutoSaveTests

**文件:** [AutoSaveTests.cs](AutoSaveTests.cs)
**测试数:** 10
**状态:** ✅ 全部通过

**目的:**

这些测试验证 Casbin Enforcer 的 `EnableAutoSave` 行为在多上下文场景中的表现，并证明 `EnableAutoSave(false)` 是原子回滚测试所必需的。

**关键测试:**

| 测试 | 证明内容 | 状态 |
|------|----------------|--------|
| `TestPolicyAutoSaveOn` / `TestPolicyAutoSaveOnAsync` | AutoSave ON 立即提交 | ✅ 通过 |
| `TestPolicyAutoSaveOff` | AutoSave OFF 推迟直到 SavePolicy | ✅ 通过 |
| `TestGroupingPolicyAutoSaveOn` | Grouping 策略也立即提交 | ✅ 通过 |
| `TestGroupingPolicyAutoSaveOff` | AutoSave OFF 时 Grouping 策略推迟 | ✅ 通过 |
| `TestAutoSaveOn_MultiContext_IndividualCommits` | 多上下文: 操作独立提交 | ✅ 通过 |
| `TestAutoSaveOff_MultiContext_RollbackOnFailure` | 多上下文: AutoSave OFF 时原子回滚 | ✅ 通过 |

**为什么 AutoSave 测试很重要:**

`TransactionIntegrityTests` 中的回滚测试需要 `enforcer.EnableAutoSave(false)` (代码行 302, 370)，因为：
- AutoSave ON 时: 当调用 `AddPolicyAsync()` 时策略立即提交到数据库
- AutoSave OFF 时: 策略保留在内存中，直到调用 `SavePolicyAsync()`
- 原子回滚测试要求所有策略都是同一事务的一部分

**参见:** [MULTI_CONTEXT_USAGE_GUIDE_zh.md - EnableAutoSave 和事务原子性](../../MULTI_CONTEXT_USAGE_GUIDE_zh.md#enableautosave-和事务原子性) 了解详细解释。

## 为什么这些测试被排除在 CI/CD 之外

这些测试被标记为 `[Trait("Category", "Integration")]` 并被**排除在 CI/CD 之外**，因为：

1.  **流水线所有权**: CI/CD 流水线不由该项目的维护者拥有
2.  **外部依赖**: 需要具有特定配置的 PostgreSQL 实例
3.  **本地验证**: 这些测试**仅用于本地验证** - 它们证明文档所述的事务保证工作正常

## 故障排除

### 错误: "could not connect to server"

PostgreSQL 未运行。启动它：
- **Windows**: 打开服务 → 启动 "postgresql-x64-XX"
- **macOS (Homebrew)**: `brew services start postgresql@17`
- **Linux**: `sudo systemctl start postgresql`

### 错误: "database 'casbin_integration_sqlsugar' does not exist"

创建数据库：
```bash
psql -U postgres -c "CREATE DATABASE casbin_integration_sqlsugar;"
```

### 错误: "password authentication failed for user 'postgres'"

或者：
1. 更新你的 PostgreSQL 密码: `ALTER USER postgres PASSWORD 'postgres';`
2. 或者在 [TransactionIntegrityTestFixture.cs](TransactionIntegrityTestFixture.cs) 中更新连接字符串以匹配你的凭据

### 错误: "relation 'casbin_rule' does not exist"

测试夹具应该自动创建表。如果失败：
1. 确保数据库存在
2. 确保用户具有 CREATE 权限: `GRANT ALL PRIVILEGES ON DATABASE casbin_integration_sqlsugar TO postgres;`
3. 尝试手动创建模式: `CREATE SCHEMA casbin_policies;` 等。

## 事务保证验证

### 关键回滚测试

**最关键的测试**是回滚验证测试：
- `SavePolicy_WhenTableDroppedInOneContext_ShouldRollbackAllContexts`
- `SavePolicy_WhenTableMissingInOneContext_ShouldRollbackAllContexts`

**关键实现细节:**

这些测试在创建 enforcer 后立即调用 `enforcer.EnableAutoSave(false)` (在 `TransactionIntegrityTests.cs` 的 302, 370 行)。这是**至关重要的**，因为：

- **AutoSave ON (默认):** `AddPolicyAsync()` 立即提交到数据库。当稍后调用 `SavePolicyAsync()` 失败时，它只回滚 DELETE 操作，而不回滚之前已经提交的 INSERT 操作。

- **AutoSave OFF:** 策略保留在内存中，直到调用 `SavePolicyAsync()`。当事务失败时，所有操作（INSERT 和 DELETE）都会原子回滚。

**代码参考:** 参见 [TransactionIntegrityTests.cs](TransactionIntegrityTests.cs) 中的 302, 370 行

## 另请参阅

- [MULTI_CONTEXT_DESIGN_zh.md](../../MULTI_CONTEXT_DESIGN_zh.md) - 技术设计和架构
- [MULTI_CONTEXT_USAGE_GUIDE_zh.md](../../MULTI_CONTEXT_USAGE_GUIDE_zh.md) - 面向用户的使用指南
- [Main README](../../README.md) - 项目概览
