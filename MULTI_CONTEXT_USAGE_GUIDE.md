# Multi-Context Support Usage Guide

## Overview

Multi-context support allows you to store different Casbin policy types in separate database locations while maintaining a unified authorization model.

**Use cases:**
- Store policy rules (p, p2) and role assignments (g, g2) in separate schemas
- Apply different retention policies per policy type
- Separate concerns in multi-tenant systems

**How it works:**
- Each `ISqlSugarClient` instance targets a different schema, table, or database
- A client provider routes policy types to the appropriate client
- The adapter automatically coordinates operations across all contexts
- Supports .NET 8.0, 9.0, and 10.0

## Quick Start

### Step 1: Create Database Contexts

Create separate `SqlSugarClient` instances that **share the same physical DbConnection object**.

**⚠️ CRITICAL - Shared Connection Requirement:**

For atomic transactions across contexts, you MUST assign the **same DbConnection object instance** to all clients.

**✅ CORRECT: Share physical DbConnection object**

```csharp
using SqlSugar;
using Npgsql; // or Microsoft.Data.SqlClient, etc.
using Casbin.Adapter.SqlSugar;

// 1. Create ONE shared connection object
string connectionString = "Host=localhost;Database=CasbinDB;Username=user;Password=pass";
var sharedConnection = new NpgsqlConnection(connectionString);
await sharedConnection.OpenAsync(); // Ensure connection is open

// 2. Create helper to configure clients (SqlSugar cannot directly take DbConnection in constructor config)
ConnectionConfig CreateConfig(string schema) => new ConnectionConfig 
{
    ConnectionString = connectionString,
    DbType = DbType.PostgreSQL,
    IsAutoCloseConnection = false, // Shared connection must NOT auto-close
    ConfigureExternalServices = new ConfigureExternalServices
    {
        EntityService = (c, p) => 
        {
            // Map to specific schema/table
            if (p.EntityName == nameof(CasbinRule)) 
                p.DbTableName = $"{schema}.casbin_rule"; 
        }
    }
};

// 3. Create clients and inject shared connection
var policyClient = new SqlSugarClient(CreateConfig("policies"));
policyClient.Ado.Connection = sharedConnection; // <--- Inject shared connection

var groupingClient = new SqlSugarClient(CreateConfig("groupings"));
groupingClient.Ado.Connection = sharedConnection; // <--- Inject same connection object

// 4. (Optional) Initialize tables if needed
// policyClient.CodeFirst.InitTables<CasbinRule>();
```

**❌ WRONG: This will NOT provide atomic transactions**

```csharp
// Creating separate clients without sharing Ado.Connection
var client1 = new SqlSugarClient(new ConnectionConfig { ConnectionString = connStr, ... });
var client2 = new SqlSugarClient(new ConnectionConfig { ConnectionString = connStr, ... });

// These clients have different connection objects (internal or external), 
// so they CANNOT share transactions automatically.
```

### Step 2: Implement Client Provider

Create a provider that routes policy types to clients by implementing `ISqlSugarClientProvider`:

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
        System.Data.Common.DbConnection sharedConnection)
    {
        _policyClient = policyClient;
        _groupingClient = groupingClient;
        _sharedConnection = sharedConnection;
    }

    public ISqlSugarClient GetClientForPolicyType(string policyType)
    {
        // Route: p/p2/p3 → policyClient, g/g2/g3 → groupingClient
        if (string.IsNullOrEmpty(policyType)) return _policyClient;
        return policyType.StartsWith("g", StringComparison.OrdinalIgnoreCase) 
            ? _groupingClient 
            : _policyClient;
    }

    public IEnumerable<ISqlSugarClient> GetAllClients() => new[] { _policyClient, _groupingClient };
    
    // Return shared connection to enable atomic transactions
    public System.Data.Common.DbConnection? GetSharedConnection() => _sharedConnection;
    
    public bool SharesConnection => true;
    
    // Optional: Dynamic table mapping if needed
    public string? GetTableNameForPolicyType(string policyType) => null; 
}
```

**Policy type routing:**

| Policy Type | Client | Description |
|-------------|---------|-------------|
| `p`, `p2`, `p3`, ... | policyClient | Permission rules |
| `g`, `g2`, `g3`, ... | groupingClient | Role/group assignments |

### Step 3-4: Create Adapter and Enforcer

```csharp
// Create provider
var provider = new PolicyTypeClientProvider(policyClient, groupingClient, sharedConnection);

// Create adapter using the provider
var adapter = new SqlSugarAdapter(provider);

// Create enforcer
var enforcer = new Enforcer("path/to/model.conf", adapter);
await enforcer.LoadPolicyAsync();
```

### Step 5: Use Normally

```csharp
// Add policies (automatically routed to correct clients)
await enforcer.AddPolicyAsync("alice", "data1", "read");        // → policyClient
await enforcer.AddGroupingPolicyAsync("alice", "admin");        // → groupingClient

// Save (coordinated across both clients atomically)
// Note: Disable AutoSave first for atomicity
enforcer.EnableAutoSave(false); 
await enforcer.SavePolicyAsync();

// Check permissions
bool allowed = await enforcer.EnforceAsync("alice", "data1", "read");
```

## Configuration Reference

### Async Operations

All operations have async variants:

```csharp
await enforcer.AddPolicyAsync("alice", "data1", "read");
await enforcer.AddGroupingPolicyAsync("alice", "admin");
await enforcer.SavePolicyAsync();
await enforcer.LoadPolicyAsync();
```

### Dependency Injection

For ASP.NET Core applications:

```csharp
// 1. Register shared connection
services.AddScoped<System.Data.Common.DbConnection>(sp =>
{
    var conn = new NpgsqlConnection(Configuration.GetConnectionString("Casbin"));
    conn.Open();
    return conn;
});

// 2. Register Client Provider
services.AddScoped<ISqlSugarClientProvider>(sp =>
{
    var sharedConn = sp.GetRequiredService<System.Data.Common.DbConnection>();
    
    // Helpers to create clients attached to sharedConn...
    var clientP = new SqlSugarClient(ConfigFor("policies"));
    clientP.Ado.Connection = sharedConn;
    
    var clientG = new SqlSugarClient(ConfigFor("groupings"));
    clientG.Ado.Connection = sharedConn;
    
    return new PolicyTypeClientProvider(clientP, clientG, sharedConn);
});

// 3. Register Adapter
services.AddScoped<IAdapter, SqlSugarAdapter>();

// 4. Register Enforcer
services.AddScoped<IEnforcer>(sp =>
{
    var adapter = sp.GetRequiredService<IAdapter>();
    return new Enforcer("model.conf", adapter);
});
```

### Connection Lifetime Management

**Important:** When using shared connections, **you** (or your DI container) are responsible for connection lifetime (disposing the connection). `SqlSugarClient` will not close an injected external connection if `IsAutoCloseConnection` is set to false (which is recommended for shared scenarios).

## Transaction Behavior

### Shared Connection Requirements

**For atomic transactions, all clients MUST share the same DbConnection object.**

**How atomic transactions work:**
1. You create ONE DbConnection object.
2. You pass it to all SqlSugarClient instances via `client.Ado.Connection`.
3. Provider returns `true` for `SharesConnection`.
4. Adapter starts a transaction on the shared connection.
5. Adapter assigns the transaction to `client.Ado.Transaction` for all clients.
6. Database ensures atomic commit/rollback.

### EnableAutoSave and Transaction Atomicity

The `EnableAutoSave` setting affects atomicity.

**AutoSave ON (Default)**
- Commits immediately per operation.
- **No atomicity across multiple operations.**

**AutoSave OFF (Recommended for Multi-Context)**
- Operations batch in memory.
- `SavePolicyAsync()` commits all changes in **one atomic transaction**.

**Best Practice:**
1. `enforcer.EnableAutoSave(false);`
2. Perform all additions/removals.
3. `await enforcer.SavePolicyAsync();`

## Troubleshooting

### "No such table" errors

Ensure tables are initialized. SqlSugar CodeFirst is powerful but explicit initialization might be needed for specific schemas:

```csharp
client.CodeFirst.InitTables<CasbinRule>();
```

### Transaction Warnings

If the adapter detects separate connections (Provider returns `SharesConnection = false` or `GetSharedConnection() = null`), it will fall back to individual transactions. This means if one context fails, others might already be committed.

## See Also

- [MULTI_CONTEXT_DESIGN.md](MULTI_CONTEXT_DESIGN.md) - Technical architecture
- [Integration Tests](Casbin.Adapter.SqlSugar.IntegrationTest/Integration/README.md) - Running transaction tests
