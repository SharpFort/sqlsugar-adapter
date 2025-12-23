# SqlSugar Casbin 适配器 - 多租户使用指南

## 功能特性

- ✅ 单数据库场景 - 简单直接的配置
- ✅ 多租户/多数据库 - 将策略和分组存储在不同数据库
- ✅ 自定义策略类型路由 - 完全控制策略存储位置
- ✅ 共享连接事务 - 保证跨数据库原子性（适用于同一服务器）
- ✅ 独立连接事务 - 支持完全独立的数据库服务器

---

## 场景 1: 单数据库场景（默认）

```csharp
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Casbin.Adapter.SqlSugar.Extensions;

// 方式 1: 使用扩展方法自动配置
services.AddSqlSugarCasbinAdapter(config =>
{
    config.ConnectionString = "Server=localhost;Database=casbin;User Id=sa;Password=***;";
    config.DbType = DbType.SqlServer;
    config.IsAutoCloseConnection = true;
});

// 方式 2: 使用已注册的 ISqlSugarClient
services.AddScoped<ISqlSugarClient>(sp =>
{
    var config = new ConnectionConfig
    {
        ConnectionString = "...",
        DbType = DbType.PostgreSQL,
        InitKeyType = InitKeyType.Attribute
    };
    return new SqlSugarClient(config);
});

services.AddSqlSugarCasbinAdapter(); // 使用已注册的客户端
```

---

## 场景 2: 策略与分组分离（多租户）

### 使用 PolicyTypeClientProvider

```csharp
using SqlSugarAdapter;
using Casbin.Adapter.SqlSugar.Extensions;

// 步骤 1: 创建两个独立的客户端
services.AddScoped<ISqlSugarClient>(sp =>
{
    // 策略客户端 - 存储 p, p2, p3...
    var config = new ConnectionConfig
    {
        ConnectionString = "Server=localhost;Database=policies_db;...",
        DbType = DbType.SqlServer,
        InitKeyType = InitKeyType.Attribute
    };
    return new SqlSugarClient(config);
});

services.AddScoped<ISqlSugarClient>(sp =>
{
    // 分组客户端 - 存储 g, g2, g3...
    var config = new ConnectionConfig
    {
        ConnectionString = "Server=localhost;Database=groupings_db;...",
        DbType = DbType.SqlServer,
        InitKeyType = InitKeyType.Attribute
    };
    return new SqlSugarClient(config);
});

// 步骤 2: 注册 Provider 和 Adapter
services.AddSqlSugarCasbinAdapterWithProvider(sp =>
{
    var policyClient = sp.GetRequiredService<ISqlSugarClient>(); // 第一个
    var groupingClient = /* 获取第二个客户端 */;
    
    // 如果两个数据库在同一服务器，可以共享连接
    return new PolicyTypeClientProvider(
        policyClient, 
        groupingClient, 
        sharesConnection: true  // 启用共享事务
    );
});
```

### 改进版：使用命名客户端

```csharp
// 注册多个命名客户端
services.AddScoped<PolicySqlSugarClient>(sp => 
    new SqlSugarClient(/* policy config */));

services.AddScoped<GroupingSqlSugarClient>(sp => 
    new SqlSugarClient(/* grouping config */));

// 使用 Provider
services.AddSqlSugarCasbinAdapterWithProvider(sp =>
{
    var policyClient = sp.GetRequiredService<PolicySqlSugarClient>();
    var groupingClient = sp.GetRequiredService<GroupingSqlSugarClient>();
    
    return new PolicyTypeClientProvider(policyClient, groupingClient, sharesConnection: false);
});
```

---

## 场景 3: 自定义策略类型映射

### 复杂多租户场景

```csharp
services.AddSqlSugarCasbinAdapterWithProvider(sp =>
{
    var tenantAClient = sp.GetRequiredService<TenantASqlSugarClient>();
    var tenantBClient = sp.GetRequiredService<TenantBSqlSugarClient>();
    var centralClient = sp.GetRequiredService<CentralSqlSugarClient>();
    
    // 自定义映射
    var mappings = new Dictionary<string, ISqlSugarClient>
    {
        { "p", tenantAClient },      // 租户 A 的策略
        { "p2", tenantBClient },     // 租户 B 的策略
        { "g", centralClient },      // 中央分组管理
        { "g2", tenantAClient }      // 租户 A 的角色继承
    };
    
    return new CustomMappingClientProvider(
        mappings, 
        defaultClient: centralClient,
        sharesConnection: false  // 不同租户，独立事务
    );
});
```

---

## 场景 4: 使用 SqlSugar Scope (推荐多租户方案)

```csharp
using SqlSugar;

// 注册 SqlSugarScope
services.AddScoped<ISqlSugarClient>(sp =>
{
    var configs = new List<ConnectionConfig>
    {
        new ConnectionConfig
        {
            ConfigId = "policy_db",
            ConnectionString = "...",
            DbType = DbType.SqlServer,
            InitKeyType = InitKeyType.Attribute
        },
        new ConnectionConfig
        {
            ConfigId = "grouping_db",
            ConnectionString = "...",
            DbType = DbType.PostgreSQL,
            InitKeyType = InitKeyType.Attribute
        }
    };
    
    return new SqlSugarScope(configs);
});

// 创建基于 ConfigId 的 Provider
services.AddSqlSugarCasbinAdapterWithProvider(sp =>
{
    var scope = sp.GetRequiredService<ISqlSugarClient>() as SqlSugarScope;
    
    var policyClient = scope.GetConnectionScope("policy_db");
    var groupingClient = scope.GetConnectionScope("grouping_db");
    
    return new PolicyTypeClientProvider(
        policyClient, 
        groupingClient, 
        sharesConnection: true  // SqlSugarScope 可以共享事务上下文
    );
});
```

---

## API 使用示例

```csharp
public class RbacService
{
    private readonly IAdapter _adapter;
    
    public RbacService(IAdapter adapter)
    {
        _adapter = adapter;
    }
    
    public async Task SetupPoliciesAsync()
    {
        var enforcer = new Enforcer("rbac_model.conf", _adapter);
        
        // 添加策略 - 自动路由到正确的数据库
        await enforcer.AddPolicyAsync("alice", "data1", "read");  // → policy_db
        await enforcer.AddPolicyAsync("bob", "data2", "write");   // → policy_db
        
        // 添加分组 - 自动路由到分组数据库
        await enforcer.AddGroupingPolicyAsync("alice", "admin");  // → grouping_db
        
        // 保存 - 自动协调多数据库事务
        await enforcer.SavePolicyAsync();
    }
}
```

---

## SharesConnection 参数说明

### 什么时候使用 `sharesConnection: true`?

- ✅ 使用 `SqlSugarScope` 管理多连接
- ✅ 多个数据库在同一服务器上
- ✅ 使用同一个 SqlServer/PostgreSQL 实例的不同数据库/Schema
- ✅ 需要跨数据库事务的原子性保证

### 什么时候使用 `sharesConnection: false`?

- ✅ 完全独立的数据库服务器
- ✅ 不同类型的数据库（如 MySQL + PostgreSQL）
- ✅ 分布式场景，无法保证跨数据库事务
- ✅ 不同云服务商的数据库

---

## 事务行为

### 共享连接 (`SharesConnection = true`)

```csharp
// 单一事务，原子性保证
SavePolicy()
{
    BeginTransaction();  // 单一事务
    try
    {
        policyClient.TruncateTable<CasbinRule>();
        policyClient.Insert(policies);
        
        groupingClient.TruncateTable<CasbinRule>();
        groupingClient.Insert(groupings);
        
        CommitTransaction();  // 全部成功或全部回滚
    }
    catch
    {
        RollbackTransaction();
    }
}
```

### 独立连接 (`SharesConnection = false`)

```csharp
// 每个客户端独立事务
SavePolicy()
{
    // 策略数据库事务
    policyClient.BeginTransaction();
    policyClient.TruncateAndInsert(policies);
    policyClient.CommitTransaction();
    
    // 分组数据库事务（独立）
    groupingClient.BeginTransaction();
    groupingClient.TruncateAndInsert(groupings);
    groupingClient.CommitTransaction();
}
```

---

## 性能优化建议

1. **使用 SqlSugarScope**：比多个独立 SqlSugarClient 性能更好
2. **合理设置 `IsAutoCloseConnection`**：多租户场景建议 `true`
3. **索引优化**：`CasbinRule` 实体已配置索引，确保数据库创建成功
4. **批量操作**：使用 `IBatchAdapter` 接口的批量方法

---

## 故障排查

### 问题 1: 跨数据库事务失败

**症状**: `SavePolicy()` 抛出事务异常

**解决**: 设置 `sharesConnection: false` 使用独立事务

### 问题 2: 策略未正确路由

**症状**: 策略存储在错误的数据库

**检查**: 
- `PolicyTypeClientProvider` 的策略类型判断逻辑
- `CustomMappingClientProvider` 的映射配置

### 问题 3: 表未自动创建

**解决**: 
```csharp
var adapter = new SqlSugarAdapter(provider, autoCodeFirst: true);
```

---

## 向后兼容性

所有新功能完全向后兼容：

```csharp
// 旧代码仍然正常工作
var adapter = new SqlSugarAdapter(sqlSugarClient);

// 新代码增加了多租户能力
var adapter = new SqlSugarAdapter(clientProvider);
```
