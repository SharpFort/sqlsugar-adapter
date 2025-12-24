# 多上下文支持使用指南

## 概述

多上下文支持允许您将不同的 Casbin 策略类型存储在分开的数据库位置中，同时保持统一的授权模型。

**应用场景：**
- 将策略规则 (p, p2) 和角色分配 (g, g2) 存储在不同的 Schema 中。
- 对不同的策略类型应用不同的数据保留策略。
- 在多租户系统中实现关注点分离。

**工作原理：**
- 每个 `ISqlSugarClient` 实例指向不同的 Schema、表或数据库。
- 上下文提供者 (`ISqlSugarClientProvider`) 将策略类型路由到合适的客户端。
- 适配器自动协调跨上下文的操作。
- 支持 .NET 8 / .NET 9 / .NET 10 版本。

## 快速开始

### 步骤 1: 创建共享连接

创建共享的物理数据库连接。这对 SqlSugar 事务原子性至关重要。

**⚠️ 关键 - 共享连接要求：**

为了跨上下文实现原子事务，您必须让所有 SqlSugar 客户端实例**引用同一个 DbConnection 对象**。

**✅ 正确：共享物理 DbConnection 对象**

```csharp
using SqlSugar;
using Npgsql; // 或 Microsoft.Data.SqlClient 等
using Casbin.Adapter.SqlSugar;

// 1. 创建单个共享连接对象
string connectionString = "Host=localhost;Database=CasbinDB;Username=user;Password=pass";
var sharedConnection = new NpgsqlConnection(connectionString);
await sharedConnection.OpenAsync(); // 确保连接已打开

// 2. 创建不同的配置，但不要在此处直接设置 Connection
// 注意：SqlSugar 的 ConnectionConfig 不直接支持设置 DbConnection 实例
// 我们需要通过 Ado.Connection 属性来注入共享连接

var policyConfig = new ConnectionConfig()
{
    ConnectionString = connectionString,
    DbType = DbType.PostgreSQL,
    IsAutoCloseConnection = false, // 关键：共享连接不能自动关闭
    ConfigureExternalServices = new ConfigureExternalServices
    {
        EntityService = (c, p) =>
        {
            // 为 "policies" schema 映射正确的表名
            if (p.EntityName == nameof(CasbinRule))
                p.DbTableName = "policies.casbin_rule"; 
        }
    }
};

var groupingConfig = new ConnectionConfig()
{
    ConnectionString = connectionString, 
    DbType = DbType.PostgreSQL,
    IsAutoCloseConnection = false, // 关键：共享连接不能自动关闭
    ConfigureExternalServices = new ConfigureExternalServices
    {
        EntityService = (c, p) =>
        {
            // 为 "groupings" schema 映射正确的表名
            if (p.EntityName == nameof(CasbinRule))
                p.DbTableName = "groupings.casbin_rule";
        }
    }
};

// 3. 创建客户端并注入共享连接
var policyClient = new SqlSugarClient(policyConfig);
policyClient.Ado.Connection = sharedConnection; // <--- 注入共享连接

var groupingClient = new SqlSugarClient(groupingConfig);
groupingClient.Ado.Connection = sharedConnection; // <--- 注入同一个连接对象

// 4. (可选) 如果表不存在，需先初始化
// 注意：CodeFirst 在多 Schema 场景下可能需要特殊处理，建议预先创建 Schema
policyClient.CodeFirst.InitTables<CasbinRule>();
groupingClient.CodeFirst.InitTables<CasbinRule>();
```

**其他配置选项：**

| 选项 | 用例 | 示例 |
|--------|----------|---------|
| **不同 Schemas** | PostgreSQL, SQL Server | `DbTableName = "policies.casbin_rule"` |
| **不同表** | 任何数据库 | `DbTableName = "casbin_policy"` |
| **不同数据库** | 仅测试 | 不同 DbConnection (⚠️ 不支持原子事务) |

### 步骤 2: 实现上下文提供者

创建一个实现 `ISqlSugarClientProvider` 接口的提供者，将策略类型路由到客户端：

```csharp
using System;
using System.Collections.Generic;
using Casbin.Adapter.SqlSugar;
using SqlSugar;

public class PolicyTypeClientProvider : ISqlSugarClientProvider
{
    private readonly ISqlSugarClient _policyClient;
    private readonly ISqlSugarClient _groupingClient;
    private readonly System.Data.Common.DbConnection _sharedConnection;

    public PolicyTypeClientProvider(
        ISqlSugarClient policyClient,
        ISqlSugarClient groupingClient,
        System.Data.Common.DbConnection sharedConnection) // 传入共享连接
    {
        _policyClient = policyClient;
        _groupingClient = groupingClient;
        _sharedConnection = sharedConnection;
    }
    
    // 路由逻辑
    public ISqlSugarClient GetClientForPolicyType(string policyType)
    {
        // 路由: p/p2/p3 → policyClient, g/g2/g3 → groupingClient
        return policyType.StartsWith("g", StringComparison.OrdinalIgnoreCase)
            ? _groupingClient
            : _policyClient;
    }

    public IEnumerable<ISqlSugarClient> GetAllClients()
    {
        return new[] { _policyClient, _groupingClient };
    }
    
    // 返回共享连接，告诉适配器可以使用原子事务
    public System.Data.Common.DbConnection? GetSharedConnection()
    {
        return _sharedConnection;
    }
    
    public bool SharesConnection => true;

    // (可选) 为多 Schema 场景返回显式表名
    public string? GetTableNameForPolicyType(string policyType)
    {
        return policyType.StartsWith("g", StringComparison.OrdinalIgnoreCase)
            ? "groupings.casbin_rule"
            : "policies.casbin_rule";
    }
}
```

**策略类型路由表:**

| 策略类型 | 上下文 | 描述 |
|-------------|---------|-------------|
| `p`, `p2`, `p3`, ... | policyClient | 权限规则 (Permission rules) |
| `g`, `g2`, `g3`, ... | groupingClient | 角色/组分配 (Role assignments) |

### 步骤 3-4: 创建适配器和 Enforcer

```csharp
// 创建提供者实例
var provider = new PolicyTypeClientProvider(policyClient, groupingClient, sharedConnection);

// 创建支持多上下文的适配器
// autoCodeFirst: false (建议手动管理 Schema)
var adapter = new SqlSugarAdapter(provider, autoCodeFirst: false);

// 创建 Enforcer (多上下文行为是透明的)
var enforcer = new Enforcer("path/to/model.conf", adapter);
await enforcer.LoadPolicyAsync();
```

### 步骤 5: 正常使用

```csharp
// 添加策略 (自动路由到正确的上下文)
await enforcer.AddPolicyAsync("alice", "data1", "read");        // → policyClient (policies schema)
await enforcer.AddGroupingPolicyAsync("alice", "admin");        // → groupingClient (groupings schema)

// 保存 (跨两个上下文协调原子事务)
// 注意：需禁用 AutoSave 才能利用原子事务，详见下文
await enforcer.SavePolicyAsync();

// 检查权限 (组合来自两个上下文的数据)
bool allowed = await enforcer.EnforceAsync("alice", "data1", "read");
```

## 配置参考

### 异步操作

所有操作都有异步变体，推荐在 .NET 8/9/10 中使用：

```csharp
await enforcer.AddPolicyAsync("alice", "data1", "read");
await enforcer.AddGroupingPolicyAsync("alice", "admin");
await enforcer.SavePolicyAsync();
await enforcer.LoadPolicyAsync();
```

### 过滤加载 (Filtered Loading)

通过实现 `IPolicyFilter` 接口跨所有上下文加载策略子集：

SqlSugar 适配器目前支持基于内存的过滤加载（即先加载所有数据到内存，再进行过滤），或者您可以扩展适配器实现基于 `Queryable` 的过滤。

```csharp
// 加载特定用户的策略
await enforcer.LoadFilteredPolicyAsync(new List<string> { "alice" }); 
// (前提是适配器实现了对应的 LoadFilteredPolicyAsync 重载)
```

或使用 Enforcer 自带的过滤 API。

### 依赖注入 (DI)

对于 ASP.NET Core 应用程序：

```csharp
// 1. 注册共享连接 (Scoped 或 Singleton，视连接生命周期而定，Web应用通常用Scoped)
services.AddScoped<System.Data.Common.DbConnection>(sp =>
{
    var connStr = Configuration.GetConnectionString("Casbin");
    var conn = new NpgsqlConnection(connStr);
    conn.Open(); // 确保打开
    return conn;
});

// 2. 注册客户端提供者
services.AddScoped<ISqlSugarClientProvider>(sp =>
{
    var sharedConn = sp.GetRequiredService<System.Data.Common.DbConnection>();
    
    // 创建 helper 方法来生成配置
    ConnectionConfig CreateConfig(string schema) => new ConnectionConfig {
        ConnectionString = sharedConn.ConnectionString,
        DbType = DbType.PostgreSQL,
        IsAutoCloseConnection = false, // 可以在请求结束时由 DI 容器释放 sharedConn
        ConfigureExternalServices = new ConfigureExternalServices {
             EntityService = (c, p) => { 
                 if(p.EntityName == nameof(CasbinRule)) p.DbTableName = $"{schema}.casbin_rule";
             }
        }
    };
    
    var clientP = new SqlSugarClient(CreateConfig("policies"));
    clientP.Ado.Connection = sharedConn;
    
    var clientG = new SqlSugarClient(CreateConfig("groupings"));
    clientG.Ado.Connection = sharedConn;
    
    return new PolicyTypeClientProvider(clientP, clientG, sharedConn);
});

// 3. 注册适配器
services.AddScoped<IAdapter, SqlSugarAdapter>();

// 4. 注册 Enforcer
services.AddScoped<IEnforcer>(sp =>
{
    var adapter = sp.GetRequiredService<IAdapter>();
    return new Enforcer("model.conf", adapter);
});
```

### 连接生命周期管理

**重要：** 当使用共享连接时，您（或 DI 容器）负责连接的生命周期。SqlSugarClient 不会自动关闭注入的外部连接（如果 `IsAutoCloseConnection = false`）。

**使用 `using` 语句：**
```csharp
using (var connection = new NpgsqlConnection(connStr))
{
    await connection.OpenAsync();
    
    // ... 创建 provider, adapter, enforcer
    // ... 使用 enforcer
    
} // 连接在此处自动 Dispose
```

## 事务行为

### 共享连接要求

**要在跨上下文实现原子事务，所有上下文必须共享同一个 DbConnection 对象实例。**

**原子事务工作原理：**
1. 您创建一个 DbConnection 对象并传递给所有客户端（通过 `Ado.Connection`）。
2. 提供者在 `SharesConnection` 属性中返回 `true`，并提供该连接实例。
3. 适配器在 `SavePolicyAsync` 中开启事务。
4. 适配器将所有客户端的 `Ado.Transaction` 设置为该事务。
5. 数据库确保跨两个 Schema 的提交/回滚是原子的。

### EnableAutoSave 和事务原子性

Casbin Enforcer 的 `EnableAutoSave` 设置从根本上影响多上下文场景中的事务原子性。

#### 理解 AutoSave 模式

**EnableAutoSave(true) - 立即提交 (默认)**

当 AutoSave 启用时（默认），每个 `AddPolicy` 等操作都会立即提交到数据库。

**行为：**
- 每个单独的操作都是原子的。
- 每个操作都有自己的隐式事务。
- **跨多个操作没有原子性：** 如果第 3 个操作失败，前 2 个操作仍然已提交，无法回滚。

**EnableAutoSave(false) - 批量原子提交**

当 AutoSave 禁用时，所有操作保留在内存中，直到调用 `enforcer.SavePolicyAsync()`。

**行为：**
- 操作存储在 Casbin 的内存策略存储中。
- 当调用 `SavePolicyAsync()` 且存在共享连接时：
  - 适配器开启单个连接级事务。
  - 所有操作原子提交（要么全成，要么全败）。
  - **跨所有操作具有完全的原子性**。

#### 关于多上下文原子性的建议

> **💡 最佳实践**
>
> 当使用多个上下文且需要所有策略更改一起成功或失败时：
>
> 1. **禁用 AutoSave:** `enforcer.EnableAutoSave(false)`
> 2. **使用共享连接:** 确保所有客户端共享同一个 `DbConnection` 对象。
> 3. **批量提交:** 调用 `await enforcer.SavePolicyAsync()` 进行原子提交。

#### 真实案例：授权设置

**无原子性 (AutoSave ON - 默认):**
```csharp
// AutoSave 默认为 ON
await enforcer.AddPolicyAsync("bob", "data1", "read");      // ✓ 已提交到 policies schema
await enforcer.AddPolicyAsync("bob", "data1", "write");     // ✓ 已提交到 policies schema
await enforcer.AddGroupingPolicyAsync("bob", "admin");      // ✗ 失败 - 网络错误

// 问题: Bob 拥有部分权限但不属于 admin 角色
// 结果: 不一致的授权状态
```

**有原子性 (AutoSave OFF):**
```csharp
enforcer.EnableAutoSave(false);  // 要求显式保存

await enforcer.AddPolicyAsync("bob", "data1", "read");      // 仅在内存中
await enforcer.AddPolicyAsync("bob", "data1", "write");     // 仅在内存中
await enforcer.AddGroupingPolicyAsync("bob", "admin");      // 仅在内存中

try
{
    await enforcer.SavePolicyAsync();  // 原子提交 - 全有或全无
    // ✓ 成功: 所有 3 条策略都已提交
}
catch (Exception ex)
{
    // ✓ 失败: 所有 3 条策略自动回滚
    // 结果: Bob 没有被赋予任何不完整的权限
    Console.WriteLine($"Setup failed: {ex.Message}");
}
```

### 数据库兼容性

| 数据库 | 原子事务 | 连接要求 | 备注 |
|----------|-------------------|----------------------|-------|
| **PostgreSQL** | ✅ 是 | 同一个 DbConnection 对象 | 完美支持多 Schema (search_path) |
| **SQL Server** | ✅ 是 | 同一个 DbConnection 对象 | 支持多 Schema |
| **MySQL** | ✅ 是 | 同一个 DbConnection 对象 | 支持多数据库 (如果用户有权限) |
| **SQLite** | ✅ 是 | 同一个 DbConnection 对象 | 仅支持同一文件内的不同表 |

### 职责矩阵

| 任务 | 您的职责 | 适配器职责 |
|------|-------------------|----------------------|
| 创建共享 DbConnection 对象 | ✅ 是 | ❌ 否 |
| 将同一连接传递给所有客户端 | ✅ 是 | ❌ 否 |
| 管理连接生命周期 (Dispose) | ✅ 是 | ❌ 否 |
| 实现 `ISqlSugarClientProvider` | ✅ 是 | ❌ 否 (除非用 Default) |
| 开启/提交/回滚事务 | ❌ 否 | ✅ 是 (在 SavePolicyAsync 中) |
| 协调多客户端事务 | ❌ 否 | ✅ 是 |

## 故障排除

### "No such table" 错误

**原因：** 数据库表未创建。

**解决方案：**
确保在应用启动时使用了 CodeFirst 初始化，或者手动运行了 SQL 脚本创建表。特别是在多 Schema 模式下，确保 Schema 本身（如 `CREATE SCHEMA policies;`）已经存在，SqlSugar CodeFirst 通常不会自动创建 Schema。

### 事务日志中的警告

**原因：** 适配器检测到非共享连接，降级为独立事务。

**解决方案：** 确保您的 Provider 的 `SharesConnection` 返回 `true`，且 `GetSharedConnection()` 返回了有效的、已打开的连接对象。

## 另请参阅

- [MULTI_CONTEXT_DESIGN_zh.md](MULTI_CONTEXT_DESIGN_zh.md) - 技术架构和实现细节
- [Adapter 源码](Casbin.Adapter.SqlSugar/SqlSugarAdapter.cs) - SqlSugarAdapter 实现
