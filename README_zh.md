# SqlSugar Adapter

[![Build Status](https://github.com/SharpFort/sqlsugar-adapter/workflows/Build/badge.svg)](https://github.com/SharpFort/sqlsugar-adapter/actions)
[![Release](https://img.shields.io/github/release/SharpFort/sqlsugar-adapter.svg)](https://github.com/SharpFort/sqlsugar-adapter/releases/latest)

SqlSugar Adapter 是 [Casbin](https://github.com/casbin/casbin) 的 [SqlSugar](https://github.com/DotNetNext/SqlSugar) ORM 适配器。使用此库，Casbin 可以从 SqlSugar 支持的数据库加载策略，或将策略保存到数据库中。

> **说明**: 此项目是基于 [casbin-net/efcore-adapter](https://github.com/casbin-net/efcore-adapter) 转换而来，将底层 ORM 从 Entity Framework Core 替换为 SqlSugar。

当前版本支持 SqlSugar 所支持的所有数据库，包括：

- SQL Server 2012 及以上版本
- SQLite 3.7 及以上版本
- PostgreSQL
- MySQL, MariaDB
- Oracle DB
- 达梦数据库
- 人大金仓数据库
- 更多...

您可以在 [SqlSugar 数据库支持](https://www.donet5.com/Home/Doc?typeId=1182) 查看完整列表。

## 安装

> **注意**: NuGet 包尚未发布。发布后，您可以使用以下命令安装：

```bash
dotnet add package SharpFort.Adapter.SqlSugar
```

## 支持的框架

此适配器支持以下 .NET 目标框架：
- .NET 10.0
- .NET 9.0
- .NET 8.0


## 简单示例

```csharp
using Casbin.Adapter.SqlSugar;
using SqlSugar;
using NetCasbin;

namespace ConsoleAppExample
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // 创建 SqlSugar 客户端配置
            var db = new SqlSugarClient(new ConnectionConfig()
            {
                DbType = DbType.Sqlite,
                ConnectionString = "Data Source=casbin_example.db",
                IsAutoCloseConnection = true
            });

            // 如果表不存在，可以使用 CodeFirst 自动创建
            db.CodeFirst.InitTables<CasbinRule>();

            // 初始化 SqlSugar 适配器并在 Casbin enforcer 中使用：
            var sqlSugarAdapter = new SqlSugarAdapter(db);
            var e = new Enforcer("examples/rbac_model.conf", sqlSugarAdapter);

            // 从数据库加载策略
            e.LoadPolicy();

            // 检查权限
            e.Enforce("alice", "data1", "read");
            
            // 修改策略
            // e.AddPolicy(...)
            // e.RemovePolicy(...)

            // 将策略保存回数据库
            e.SavePolicy();
        }
    }
}
```

## 使用依赖注入

当在依赖注入场景（如 ASP.NET Core）中使用适配器时，您应该使用 `IServiceProvider` 构造函数或扩展方法，以避免 ISqlSugarClient 实例被释放的问题。

### 推荐方式（使用扩展方法）

```csharp
using Casbin.Adapter.SqlSugar;
using Casbin.Adapter.SqlSugar.Extensions;
using SqlSugar;
using Microsoft.Extensions.DependencyInjection;

// 注册服务
services.AddSingleton<ISqlSugarClient>(sp =>
{
    return new SqlSugarClient(new ConnectionConfig()
    {
        DbType = DbType.SqlServer,
        ConnectionString = connectionString,
        IsAutoCloseConnection = true
    });
});

// 使用扩展方法注册适配器
services.AddSqlSugarAdapter();

// 适配器会在每次操作时从服务提供程序解析客户端，
// 防止在与长生命周期服务一起使用时出现上下文释放问题。
```

### 替代方式（使用 IServiceProvider 构造函数）

```csharp
// 在启动配置中
services.AddSingleton<ISqlSugarClient>(sp =>
{
    return new SqlSugarClient(new ConnectionConfig()
    {
        DbType = DbType.SqlServer,
        ConnectionString = connectionString,
        IsAutoCloseConnection = true
    });
});

services.AddCasbinAuthorization(options =>
{
    options.DefaultModelPath = "model.conf";
    
    // 使用 IServiceProvider 构造函数
    options.DefaultEnforcerFactory = (sp, model) =>
        new Enforcer(model, new SqlSugarAdapter(sp));
});
```

这种方式在每次数据库操作时从服务提供程序解析 ISqlSugarClient，确保：
- 适配器与 Scoped 的 ISqlSugarClient 实例正常工作
- 当适配器生命周期超出创建它的作用域时不会抛出 `ObjectDisposedException`
- 适配器可以在单例等长生命周期服务中使用

## 多客户端支持

适配器支持将不同的策略类型存储在不同的数据库客户端中，允许您：
- 将策略规则（p, p2 等）和分组规则（g, g2 等）存储在不同的 Schema 和/或表中
- 每个客户端可以独立控制 Schema 和表
- 为多租户或合规场景分离数据

### 快速示例

```csharp
// 创建共享连接配置
var sharedConfig = new ConnectionConfig()
{
    DbType = DbType.SqlServer,
    ConnectionString = connectionString,
    IsAutoCloseConnection = false  // 共享连接时不自动关闭
};

// 创建使用共享连接的客户端
var policyClient = new SqlSugarClient(sharedConfig);
var groupingClient = new SqlSugarClient(sharedConfig);

// 创建将策略类型路由到客户端的提供程序
var provider = new PolicyTypeClientProvider(policyClient, groupingClient);

// 使用提供程序创建适配器
var adapter = new SqlSugarAdapter(provider);
var enforcer = new Enforcer("rbac_model.conf", adapter);

// 所有操作透明地跨客户端工作
enforcer.AddPolicy("alice", "data1", "read");      // → policyClient
enforcer.AddGroupingPolicy("alice", "admin");      // → groupingClient
enforcer.SavePolicy();                              // 跨两者原子操作
```

> **⚠️ 事务完整性要求**
>
> 要实现多客户端原子操作：
> 1. **共享连接:** 所有客户端必须使用**相同的连接对象**（引用相等）
> 2. **禁用 AutoSave:** 使用 `enforcer.EnableAutoSave(false)` 并调用 `SavePolicyAsync()` 批量提交
> 3. **支持的数据库:** PostgreSQL、MySQL、SQL Server、SQLite（同一文件）
>
> **为什么要禁用 AutoSave？** 当 `EnableAutoSave(true)`（默认）时，每个策略操作立即独立提交。如果后续操作失败，之前的操作仍保持已提交状态。使用 `EnableAutoSave(false)` 时，所有更改保留在内存中，直到 `SavePolicyAsync()` 使用共享连接级事务原子地提交它们。
>
> - ✅ **原子性:** 相同连接对象 + `EnableAutoSave(false)` + `SavePolicyAsync()`
> - ❌ **非原子性:** AutoSave 开启、不同连接对象、不同数据库
>
> 详见 [EnableAutoSave 和事务原子性](MULTI_CONTEXT_USAGE_GUIDE_zh.md#enableautosave-和事务原子性)。

### 文档

- **[多客户端使用指南](MULTI_CONTEXT_USAGE_GUIDE_zh.md)** - 完整的分步指南和示例
- **[多客户端设计](MULTI_CONTEXT_DESIGN_zh.md)** - 详细的设计文档和限制说明
- **[集成测试设置](Casbin.Adapter.SqlSugar.IntegrationTest/Integration/README_zh.md)** - 如何在本地运行事务完整性测试

## 获取帮助

- [Casbin.NET](https://github.com/casbin/Casbin.NET)
- [SqlSugar ORM](https://github.com/DotNetNext/SqlSugar)
- [原 EFCore Adapter](https://github.com/casbin-net/efcore-adapter)

## 许可证

此项目采用 Apache 2.0 许可证。查看 [LICENSE](LICENSE) 文件获取完整许可证文本。
