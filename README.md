# 🛡️ Casbin.NET SqlSugar Adapter

[![NuGet](https://img.shields.io/nuget/v/Casbin.NET.Adapter.SqlSugar)](https://www.nuget.org/packages/Casbin.NET.Adapter.SqlSugar)
[![License](https://img.shields.io/github/license/SharpFort/sqlsugar-adapter)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4)](https://dotnet.microsoft.com/)

The [SqlSugar](https://github.com/sunkaixuan/SqlSugar) adapter for [Casbin.NET](https://github.com/casbin/Casbin.NET).
Efficiently load and save Casbin policies from any SqlSugar-supported database.

## 📚 Documentation

| Document | Description |
|----------|-------------|
| [**Usage Guide**](MULTI_CONTEXT_USAGE_GUIDE.md) | 🚀 **Start Here!** Step-by-step guide for basic and multi-context setup. |
| [**Design Document**](MULTI_CONTEXT_DESIGN.md) | 🧠 Technical architecture validation and deep dive. |
| [**Integration Tests**](Casbin.Adapter.SqlSugar.IntegrationTest/Integration/README.md) | 🧪 How to run transaction integrity tests. |
| [**Unit Tests**](Casbin.Adapter.SqlSugar.UnitTest/README.md) | 🧩 Fast validation for core logic. |
| [**Multi-Tenant Guide**](MULTI_TENANT_GUIDE.md) | 🏢 Strategies for multi-tenant applications. |
| [**中文文档**](README_zh.md) | 🇨🇳 Switch to Chinese Documentation. |

### 🧪 Test Strategy Documentation

This adapter includes comprehensive test coverage with detailed documentation explaining the testing approach:

- **[Client Routing Test](Casbin.Adapter.SqlSugar.UnitTest/TestAdapters/README_ClientRoutingTest.md)** - Validates correct client and table routing for different policy types in multi-context scenarios. This test ensures that the adapter correctly routes policies to their respective clients and tables, preventing data mixing bugs.

- **[Dependency Injection Strategy](Casbin.Adapter.SqlSugar.UnitTest/DependencyInjection_TestStrategy.md)** - Explains why SqlSugar's testing approach differs from EFCore. SqlSugar's `IsAutoCloseConnection` feature eliminates the need for complex `IServiceProvider` lifecycle management, resulting in simpler and more robust tests.


## ✨ Features

- 🔌 **Universal Support**: Works with MySQL, SQL Server, PostgreSQL, SQLite, Oracle, and more.
- ⚛️ **Atomic Transactions**: Full support for multi-context transactional integrity.
- 🚀 **Performance**: Optimized for high-throughput policy evaluation.
- 🎯 **Targets**: Native support for .NET 8.0, 9.0, and 10.0.

## 📦 Installation

```xml
<PackageReference Include="Casbin.NET.Adapter.SqlSugar" Version="x.x.x" />
```

Or via CLI:

```bash
dotnet add package Casbin.NET.Adapter.SqlSugar
```

## 🚀 Quick Start

### 1. Simple Usage

```csharp
using Casbin.Adapter.SqlSugar;
using SqlSugar;
using Casbin.NET;

// 1. Configure SqlSugar
var sqlSugar = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = "...",
    DbType = DbType.MySql,
    IsAutoCloseConnection = true,
    InitKeyType = InitKeyType.Attribute
});

// 2. Create Adapter
var adapter = new SqlSugarAdapter(sqlSugar);

// 3. Initialize Enforcer
var enforcer = new Enforcer("path/to/model.conf", adapter);

// 4. Load & Check
await enforcer.LoadPolicyAsync();
if (await enforcer.EnforceAsync("alice", "data1", "read")) 
{
    // Access granted
}
```

### 2. Dependency Injection (ASP.NET Core)

```csharp
// In Program.cs
services.AddScoped<ISqlSugarClient>(sp => ...); // Register your SqlSugar client
services.AddScoped<IAdapter, SqlSugarAdapter>();
services.AddScoped<IEnforcer>(sp => 
{
    var adapter = sp.GetRequiredService<IAdapter>();
    return new Enforcer("model.conf", adapter);
});
```