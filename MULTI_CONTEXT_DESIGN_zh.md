# 多上下文支持设计文档

## 概述

本文档提供了 SqlSugar 适配器中多上下文支持的技术架构和实现细节。有关面向用户的设置说明，请参阅 [MULTI_CONTEXT_USAGE_GUIDE_zh.md](MULTI_CONTEXT_USAGE_GUIDE_zh.md)。

**目的：** 启用多个 `SqlSugarClient` 实例将不同的策略类型存储在分开的数据库位置（模式、表或数据库）中，同时尽可能保持事务一致性。

**支持版本：** 明确支持 .NET 8, .NET 9, 和 .NET 10 版本。

## 背景

### 架构演进
- **原 EFCore 架构**：每个适配器实例一个 `DbContext`，所有策略类型通常存储在同一个表中。
- **现 SqlSugar 架构**：引入 `ISqlSugarClientProvider`，支持将不同策略类型路由到不同的 `SqlSugarClient` 实例。

### 动机
- 将不同的策略类型存储在分开的数据库模式（Schema）或表中。
- 启用具有独立数据库连接的多租户场景。
- 满足组织对数据隔离的合规性要求。

### 需求

**功能性：**
1. 将策略类型路由到不同的 `ISqlSugarClient` 实例。
2. 当上下文共享连接时，维持 ACID 保证。
3. 保持向后兼容性。

**技术性：**
1. 使用 SqlSugar 的事务机制处理共享事务。
2. 运行时检测连接兼容性。
3. 当无法共享连接时，优雅降级为独立事务。

**非需求：**
1. 跨不同数据库/服务器的分布式事务。
2. 自动连接字符串管理。
3. 模式迁移协调。

## 架构

### 上下文提供者模式 (Context Provider Pattern)

#### ISqlSugarClientProvider 接口

```csharp
public interface ISqlSugarClientProvider
{
    /// <summary>
    /// 获取特定策略类型（例如 "p", "p2", "g", "g2"）的 SqlSugarClient
    /// </summary>
    ISqlSugarClient GetClientForPolicyType(string policyType);

    /// <summary>
    /// 获取此提供者使用的所有唯一 SqlSugarClient 实例。
    /// 用于跨所有上下文协调的操作（SavePolicy, LoadPolicy）
    /// </summary>
    IEnumerable<ISqlSugarClient> GetAllClients();

    /// <summary>
    /// 如果所有上下文使用相同的物理连接，则返回共享的 DbConnection。
    /// 如果上下文使用单独的连接，则返回 null。
    /// </summary>
    /// <remarks>
    /// 当非空时，适配器在连接级别启动事务，
    /// 而不是上下文级别，这对于 PostgreSQL 等数据库中的正确保存点处理是必需的。
    /// </remarks>
    System.Data.Common.DbConnection? GetSharedConnection();

    /// <summary>
    /// 获取特定策略类型对应的表名（支持多 Schema 场景）
    /// </summary>
    string? GetTableNameForPolicyType(string policyType);
    
    /// <summary>
    /// 标识是否使用共享连接
    /// </summary>
    bool SharesConnection { get; }
}
```

**契约：**
- `GetClientForPolicyType()` 必须为任何策略类型返回有效的 `ISqlSugarClient`。
- `GetAllClients()` 必须返回所有不同的客户端实例（用于 SavePolicy, LoadPolicy）。
- 相同的策略类型应始终路由到相同的客户端实例。
- 当所有上下文使用相同的物理连接时，`GetSharedConnection()` 必须返回共享的 `DbConnection`；当使用独立连接时返回 null。

#### 默认实现

```csharp
/// <summary>
/// 使用单个客户端处理所有策略类型的默认提供者（向后兼容）
/// </summary>
public class DefaultSqlSugarClientProvider : ISqlSugarClientProvider
{
    private readonly ISqlSugarClient _client;

    public DefaultSqlSugarClientProvider(ISqlSugarClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public ISqlSugarClient GetClientForPolicyType(string policyType) => _client;
    
    public IEnumerable<ISqlSugarClient> GetAllClients() => new[] { _client };

    // 单客户端场景下，视作共享连接（自身共享）或根据具体实现返回
    public System.Data.Common.DbConnection? GetSharedConnection() => _client.Ado.Connection;
    
    public bool SharesConnection => true;
    
    public string? GetTableNameForPolicyType(string policyType) => null; // 使用默认表名
}
```

### 构造函数设计

```csharp
public partial class SqlSugarAdapter : IAdapter
{
    private readonly ISqlSugarClientProvider _clientProvider;
    protected ISqlSugarClient DbClient { get; } // 保持向后兼容

    /// <summary>
    /// 新增：带有自定义提供者的多上下文构造函数
    /// </summary>
    public SqlSugarAdapter(ISqlSugarClientProvider clientProvider, bool autoCodeFirst = true)
    {
        _clientProvider = clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));
        // ... 初始化逻辑
    }

    /// <summary>
    /// 现有：单上下文构造函数（保持行为不变）
    /// </summary>
    public SqlSugarAdapter(ISqlSugarClient client, bool autoCodeFirst = true)
        : this(new DefaultSqlSugarClientProvider(client), autoCodeFirst)
    {
        DbClient = client;
    }
}
```

**向后兼容性：**
- 现有的单上下文构造函数将客户端包装在 `DefaultSqlSugarClientProvider` 中。
- 所有现有代码继续工作不变。

### 事务协调

#### 基于提供者的连接策略

适配器使用提供者的 `SharesConnection` 属性和 `GetSharedConnection()` 方法来确定事务策略：

```csharp
if (_clientProvider.SharesConnection)
{
    // 使用共享事务（原子性）
    await SavePolicyWithSharedTransactionAsync(store, rulesByClient);
}
else
{
    // 使用独立事务（跨上下文非原子性）
    await SavePolicyWithSeparateTransactionsAsync(store, rulesByClient);
}
```

**策略：**
- 提供者明确声明连接拓扑。
- 如果提供者指示共享连接 → 所有上下文共享该连接 → 使用连接级事务。
- 如果提供者指示不共享 → 上下文使用独立连接 → 使用独立事务。

#### 连接级事务模式 (SqlSugar 实现)

当提供者使用共享连接时：

```csharp
// 实际实现逻辑简化
var sharedConnection = _clientProvider.GetSharedConnection();

// 确保连接已打开
if (sharedConnection.State != ConnectionState.Open)
    sharedConnection.Open();

// 开启事务
using var transaction = sharedConnection.BeginTransaction();

try
{
    foreach (var group in rulesByClient)
    {
        var client = group.Key;
        // 关键：确保客户端使用该共享事务
        client.Ado.Transaction = transaction;
        
        // 执行删除和插入操作
        // ...
    }
    
    transaction.Commit(); // 跨所有上下文原子提交
}
catch
{
    transaction.Rollback();
    throw;
}
```

**关键点：**
- 事务在连接级别启动。
- 对于支持通过 `Ado.Transaction` 属性共享事务的 SqlSugar 客户端至关重要。

#### 独立事务模式 (降级)

当提供者使用独立连接时：

```csharp
// 伪代码 - 警告：跨上下文不具备原子性
foreach (var group in rulesByClient)
{
    var client = group.Key;
    try 
    {
        client.Ado.BeginTran();
        // 执行操作
        client.Ado.CommitTran(); // 仅提交此上下文
    }
    catch
    {
        client.Ado.RollbackTran();
        throw; // 一个上下文失败不会回滚其他上下文
    }
}
```

### 数据库支持

| 数据库 | 同一 Schema | 不同 Schema | 不同表 | 独立文件 | 原子性事务 |
|----------|-------------|-------------------|------------------|----------------|-----------|
| **SQL Server** | ✅ | ✅ | ✅ | ✅ (同一服务器) | ✅ |
| **PostgreSQL** | ✅ | ✅ | ✅ | ❌ | ✅ (同一 DB) |
| **MySQL** | ✅ | ✅ | ✅ | ❌ | ✅ (同一 DB) |
| **SQLite** | ✅ | N/A | ✅ (同一文件) | ⚠️ (无原子性) | ✅ (仅同一文件) |

**关键约束：**
- 所有上下文必须使用**同一个 DbConnection 对象实例**才能进行共享事务。
- 用户必须显式创建并将共享连接对象传递给所有客户端（如使用 SqlSugar 的 `Ado.Connection` 赋值）。
- 不支持跨数据库（分布式）事务。

## 实现细节

### 操作处理

#### SavePolicy (多上下文自适应事务)

最复杂的操作 - 协调所有上下文：

```csharp
public virtual async Task SavePolicyAsync(IPolicyStore store)
{
    var allRules = GetAllRulesFromStore(store);
    var rulesByClient = allRules.GroupBy(r => _clientProvider.GetClientForPolicyType(r.PType));

    if (_clientProvider.SharesConnection)
    {
        // 原子操作
        await SavePolicyWithSharedTransactionAsync(store, rulesByClient);
    }
    else
    {
        // 非原子操作
        await SavePolicyWithSeparateTransactionsAsync(store, rulesByClient);
    }
}
```

#### LoadPolicy (多上下文，只读)

不需要事务，但在多 Schema 场景下需要正确处理表名映射：

```csharp
public virtual async Task LoadPolicyAsync(IPolicyStore store)
{
    var allRules = new List<CasbinRule>();
    var policyTypes = new[] { "p", "p2", "p3", "g", "g2", "g3", "g4" };
    
    foreach (var policyType in policyTypes)
    {
        var client = _clientProvider.GetClientForPolicyType(policyType);
        var tableName = _clientProvider.GetTableNameForPolicyType(policyType);
        
        // 使用 .AS(tableName) 处理多 Schema 表名
        var rules = await client.Queryable<CasbinRule>().AS(tableName).ToListAsync();
        allRules.AddRange(rules);
    }
    
    // ... 加载到存储
}
```

### AutoSave 模式和事务原子性

Casbin Enforcer 的 `EnableAutoSave` 设置由根本上影响多上下文场景中的事务原子性。

**AutoSave ON (默认行为):**

当启用 AutoSave 时，Casbin Enforcer 为每个操作立即调用适配器的 Add/Remove/Update 方法。适配器随后调用数据库操作，这会为该单个操作创建一个隐式事务。

**影响：**
- 每个操作在隔离中是原子的。
- **跨多个操作没有事务协调**。
- 适配器的 `SavePolicyAsync()` 事务协调被完全绕过。

**AutoSave OFF (批处理模式):**

当禁用 AutoSave 时，操作停留在 Enforcer 的内存策略存储中。只有当调用 `SavePolicyAsync()` 时，适配器才会一次性接收所有策略，从而启用原子事务协调。

**推荐：**

对于需要原子性的多上下文场景：
1. 使用 `enforcer.EnableAutoSave(false)`。
2. 确保所有上下文共享同一个 `DbConnection` 对象。
3. 调用 `SavePolicyAsync()` 以原子方式批量提交。

## 验证

### 集成测试

事务完整性保证已通过全面的集成测试进行验证：
- `Casbin.Adapter.SqlSugar.IntegrationTest` 项目证明了跨多个上下文的原子提交/回滚。

**测试覆盖率包括：**
- 共享连接下的原子写入。
- 只有部分上下文失败时的完全回滚。
- .NET 8, .NET 9, .NET 10 环境下的兼容性测试。

## 状态

**实现：** ✅ 完成
**测试：** ✅ 单元测试和集成测试全部通过，支持 .NET 8/9/10
**文档：** ✅ 完成 (中文版已更新)

## 另请参阅

- [MULTI_CONTEXT_USAGE_GUIDE_zh.md](MULTI_CONTEXT_USAGE_GUIDE_zh.md) - 分步使用指南
- `ISqlSugarClientProvider` 接口源码
- `SqlSugarAdapter` 实现源码
