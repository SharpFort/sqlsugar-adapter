# Multi-Context Support Design Document

## Overview

This document provides technical architecture and implementation details for multi-context support in the SqlSugar adapter. For user-facing setup instructions, see [MULTI_CONTEXT_USAGE_GUIDE.md](MULTI_CONTEXT_USAGE_GUIDE.md).

**Purpose:** Enable different policy types to be stored in separate database locations (schemas, tables, or databases) while maintaining transactional integrity where possible.

## Background

### Motivation
- Store different policy types in separate schemas/tables
- Enable multi-tenant scenarios with separate contexts
- Separate concerns for organizational requirements

### Requirements

**Functional:**
1. Route policy types to different `ISqlSugarClient` instances
2. Maintain ACID guarantees when contexts share connections
3. Preserve backward compatibility

**Technical:**
1. Use SqlSugar's `Ado.Connection` for shared transactions
2. Detect connection compatibility at runtime
3. Gracefully degrade to individual transactions when sharing is not possible

## Architecture

### Context Provider Pattern

#### ISqlSugarClientProvider Interface

```csharp
public interface ISqlSugarClientProvider
{
    /// <summary>
    /// Gets the SqlSugarClient for a specific policy type (e.g., "p", "p2", "g", "g2")
    /// </summary>
    ISqlSugarClient GetClientForPolicyType(string policyType);

    /// <summary>
    /// Gets all unique SqlSugarClient instances used by this provider.
    /// Used for operations that coordinate across all contexts (SavePolicy, LoadPolicy)
    /// </summary>
    IEnumerable<ISqlSugarClient> GetAllClients();

    /// <summary>
    /// Gets the shared DbConnection if all contexts use the same physical connection.
    /// Returns null if contexts use separate connections.
    /// </summary>
    System.Data.Common.DbConnection? GetSharedConnection();

    /// <summary>
    /// Gets the table name for a specific policy type. 
    /// Useful for multi-schema routing (e.g., "schema.table").
    /// </summary>
    string? GetTableNameForPolicyType(string policyType);

    /// <summary>
    /// Indicates whether the provider handles connection sharing.
    /// </summary>
    bool SharesConnection { get; }
}
```

**Contract:**
- `GetClientForPolicyType()` must return a valid `ISqlSugarClient` for any policy type
- `GetAllClients()` must return all distinct clients
- `GetSharedConnection()` must return the shared DbConnection when all contexts use the same physical connection
- `GetTableNameForPolicyType()` allows dynamic table mapping

#### Default Implementation

```csharp
public class DefaultSqlSugarClientProvider : ISqlSugarClientProvider
{
    private readonly ISqlSugarClient _client;

    public DefaultSqlSugarClientProvider(ISqlSugarClient client)
    {
        _client = client;
    }

    public ISqlSugarClient GetClientForPolicyType(string policyType) => _client;
    public IEnumerable<ISqlSugarClient> GetAllClients() => new[] { _client };
    public System.Data.Common.DbConnection? GetSharedConnection() => _client.Ado.Connection;
    public string? GetTableNameForPolicyType(string policyType) => null; // Use default
    public bool SharesConnection => true;
}
```

### Transaction Coordination

#### Shared Connection Strategy

The adapter checks `_clientProvider.SharesConnection` to determine the transaction strategy.

```csharp
if (_clientProvider.SharesConnection)
{
    // Use shared transaction (atomic)
    await SavePolicyWithSharedTransactionAsync(store, rulesByClient);
}
else
{
    // Use individual transactions (not atomic across contexts)
    await SavePolicyWithSeparateTransactionsAsync(store, rulesByClient);
}
```

#### Shared Transaction Pattern

When `SharesConnection` is true:

```csharp
var sharedConnection = _clientProvider.GetSharedConnection();
if (sharedConnection.State != ConnectionState.Open) await sharedConnection.OpenAsync();

using var transaction = await sharedConnection.BeginTransactionAsync();

try
{
    foreach (var client in clients)
    {
        // Enlist client in shared transaction
        client.Ado.Transaction = transaction;
        
        // Perform operations...
    }
    
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

**Key Requirement:** All involved `ISqlSugarClient` instances must have their `Ado.Connection` property set to the exact same `DbConnection` object instance.

### Database Support
It is recommended not to use SQLite in project development, as the current configuration scheme may lead to transaction conflicts: SQLite Error 5: 'database is locked'. No solution has been found yet. Please directly use PostgreSQL

| Database | Same Schema | Different Schemas | Different Tables | Separate Files | Atomic Tx |
|----------|-------------|-------------------|------------------|----------------|-----------|
| **SQL Server** | ✅ | ✅ | ✅ | ✅ (same server) | ✅ |
| **PostgreSQL** | ✅ | ✅ | ✅ | ❌ | ✅ (same DB) |
| **MySQL** | ✅ | ✅ | ✅ | ❌ | ✅ (same DB) |
| **SQLite** | ✅ | N/A | ✅ (same file) | ⚠️ (no atomicity) | ✅ (same file only) |

## Implementation Details

### Table Name Dynamic Routing

Unlike EF Core which uses `DbSet<T>`, SqlSugar allows dynamic table mapping using `.AS("table_name")`.

```csharp
string? tableName = _clientProvider.GetTableNameForPolicyType(policyType);
// ...
await client.Insertable(rules).AS(tableName).ExecuteCommandAsync();
```

This feature is critical for multi-schema support (e.g., mapping policy "p" to "policies.casbin_rule").

### AutoSave Mode and Transaction Atomicity

The Casbin Enforcer's `EnableAutoSave` setting fundamentally affects transaction atomicity in multi-context scenarios.

**AutoSave ON (Default):**
- Each `AddPolicy` call commits immediately.
- **No transaction coordination across multiple operations.**
- Not atomic across multiple calls.

**AutoSave OFF (Batch Mode):**
- Operations stay in memory until `SavePolicyAsync()` is called.
- `SavePolicyAsync()` batches all changes and commits them in a single transaction (if sharing connection).
- **Atomic across all operations.**

**Recommendation:**
For multi-context scenarios requiring atomicity, always use `EnableAutoSave(false)` and call `SavePolicyAsync()`.

## Status

**Implementation:** ✅ Complete
**Testing:** ✅ Unit tests and Integration tests passing on .NET 8.0, 9.0, 10.0
**Documentation:** ✅ Complete

## See Also

- [MULTI_CONTEXT_USAGE_GUIDE.md](MULTI_CONTEXT_USAGE_GUIDE.md) - Step-by-step user guide
- [ISqlSugarClientProvider Interface](Casbin.Adapter.SqlSugar/ISqlSugarClientProvider.cs)
- [SqlSugarAdapter Implementation](Casbin.Adapter.SqlSugar/SqlSugarAdapter.cs)
