# SqlSugar Adapter

[![Build Status](https://github.com/SharpFort/sqlsugar-adapter/workflows/Build/badge.svg)](https://github.com/SharpFort/sqlsugar-adapter/actions)
[![Coverage Status](https://coveralls.io/repos/github/SharpFort/sqlsugar-adapter/badge.svg?branch=master)](https://coveralls.io/github/SharpFort/sqlsugar-adapter?branch=master)
[![Nuget](https://img.shields.io/nuget/v/Casbin.NET.Adapter.SqlSugar.svg)](https://www.nuget.org/packages/Casbin.NET.Adapter.SqlSugar/)
[![Release](https://img.shields.io/github/release/SharpFort/sqlsugar-adapter.svg)](https://github.com/SharpFort/sqlsugar-adapter/releases/latest)
[![Nuget](https://img.shields.io/nuget/dt/Casbin.NET.Adapter.SqlSugar.svg)](https://www.nuget.org/packages/Casbin.NET.Adapter.SqlSugar/)
[![Discord](https://img.shields.io/discord/1022748306096537660?logo=discord&label=discord&color=5865F2)](https://discord.gg/S5UjpzGZjN)

SqlSugar Adapter is the [SqlSugar](https://github.com/sunkaixuan/SqlSugar) adapter for [Casbin](https://github.com/casbin/casbin). With this library, Casbin can load policy from SqlSugar supported databases or save policy to them.

The current version supports all databases which SqlSugar supports, including:

- MySQL, MariaDB
- SQL Server
- PostgreSQL
- SQLite
- Oracle
- Db2
- And more...

## Installation

```bash
dotnet add package Casbin.NET.Adapter.SqlSugar
```

## Supported Frameworks

The adapter supports the following .NET target frameworks:
- .NET 10.0
- .NET 9.0
- .NET 8.0

## Simple Example

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
            // Initialize SqlSugarClient
            var connectionConfig = new ConnectionConfig()
            {
                ConnectionString = "Data Source=casbin_example.sqlite3",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute
            };
            var dbClient = new SqlSugarClient(connectionConfig);

            // Optional: Create database and tables automatically
            // dbClient.DbMaintenance.CreateDatabase(); 
            // The adapter can also handle table creation if autoCodeFirst is true (default).

            // Initialize a SqlSugar adapter and use it in a Casbin enforcer:
            var adapter = new SqlSugarAdapter(dbClient);
            var e = new Enforcer("examples/rbac_model.conf", adapter);

            // Load the policy from DB.
            e.LoadPolicy();

            // Check the permission.
            e.Enforce("alice", "data1", "read");
            
            // Modify the policy.
            // e.AddPolicy(...)
            // e.RemovePolicy(...)
	
            // Save the policy back to DB.
            e.SavePolicy();
        }
    }
}
```

## Using with Dependency Injection

When using the adapter with dependency injection (e.g., in ASP.NET Core), you can register `ISqlSugarClient` (or `ISqlSugarClientProvider` for advanced scenarios) and the adapter.

### Recommended Approach

```csharp
using Casbin.Adapter.SqlSugar;
using SqlSugar;
using NetCasbin;
using Microsoft.Extensions.DependencyInjection;

// Register SqlSugarClient (Scoped recommended for web apps)
services.AddScoped<ISqlSugarClient>(sp =>
{
    return new SqlSugarClient(new ConnectionConfig
    {
        ConnectionString = "YourConnectionString",
        DbType = DbType.SqlServer,
        IsAutoCloseConnection = true
    });
});

// Register the adapter
services.AddScoped<IAdapter, SqlSugarAdapter>();

// Register the Enforcer
services.AddScoped<IEnforcer>(sp =>
{
    var adapter = sp.GetRequiredService<IAdapter>();
    return new Enforcer("examples/rbac_model.conf", adapter);
});
```

## Multi-Context Support

The adapter supports storing different policy types in separate database contexts (schemas or tables), allowing you to:
- Store policies (p, p2, etc.) and groupings (g, g2, etc.) in different schemas and/or tables
- Separate data for multi-tenant or compliance scenarios

### Quick Example

To use multi-context support, implement `ISqlSugarClientProvider` or populate a `DefaultSqlSugarClientProvider`.

```csharp
// Example using a custom provider for multi-schema support
public class MyClientProvider : ISqlSugarClientProvider
{
    private readonly ISqlSugarClient _client; // Using shared connection
    
    public MyClientProvider(ISqlSugarClient client) { _client = client; }

    public ISqlSugarClient GetClientForPolicyType(string policyType) => _client;

    public IEnumerable<ISqlSugarClient> GetAllClients() => new[] { _client };
    
    public System.Data.Common.DbConnection? GetSharedConnection() => _client.Ado.Connection;
    
    public string? GetTableNameForPolicyType(string policyType)
    {
         // Route different policies to different tables/schemas
         return policyType.StartsWith("g") ? "groupings.casbin_rule" : "policies.casbin_rule";
    }
    
    public bool SharesConnection => true;
}

// Usage
var provider = new MyClientProvider(dbClient);
var adapter = new SqlSugarAdapter(provider);
```

> **⚠️ Transaction Integrity Requirements**
>
> For atomic multi-context operations:
> 1. **Share DbConnection:** All contexts must use the **same `DbConnection` object**.
> 2. **Disable AutoSave:** Use `enforcer.EnableAutoSave(false)` and call `SavePolicyAsync()` to batch commit.
>
> See detailed usage in the documentation.

### Documentation

- **[Multi-Context Usage Guide](MULTI_CONTEXT_USAGE_GUIDE.md)** - Complete step-by-step guide
- **[Multi-Context Design](MULTI_CONTEXT_DESIGN.md)** - Detailed design documentation
- **[Integration Tests Setup](Casbin.Adapter.SqlSugar.IntegrationTest/Integration/README.md)** - How to run transaction integrity tests locally

## Getting Help

- [Casbin.NET](https://github.com/casbin/Casbin.NET)
- [SqlSugar](https://github.com/sunkaixuan/SqlSugar)

## License

This project is under Apache 2.0 License. See the [LICENSE](LICENSE) file for the full license text.