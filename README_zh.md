# 🛡️ Casbin.NET SqlSugar 适配器

[![NuGet](https://img.shields.io/nuget/v/Casbin.NET.Adapter.SqlSugar)](https://www.nuget.org/packages/Casbin.NET.Adapter.SqlSugar)
[![License](https://img.shields.io/github/license/SharpFort/sqlsugar-adapter)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4)](https://dotnet.microsoft.com/)

[Casbin.NET](https://github.com/casbin/Casbin.NET) 的 [SqlSugar](https://github.com/sunkaixuan/SqlSugar) 适配器。
支持从任何 SqlSugar 支持的数据库中高效加载和保存策略。

## 📚 文档中心

| 文档 | 描述 |
|------|------|
| [**使用指南**](MULTI_CONTEXT_USAGE_GUIDE_zh.md) | 🚀 **从这里开始！** 基础配置与多上下文实战指南。 |
| [**设计文档**](MULTI_CONTEXT_DESIGN_zh.md) | 🧠 多上下文支持的技术架构与设计细节。 |
| [**集成测试**](Casbin.Adapter.SqlSugar.IntegrationTest/Integration/README_zh.md) | 🧪 如何运行事务一致性集成测试。 |
| [**Multi-Tenant Guide**](MULTI_TENANT_GUIDE.md) | 🏢 (英文) 多租户应用策略方案。 |
| [**English Docs**](README.md) | 🇺🇸 切换至英文文档。 |

## ✨ 功能特性

- 🔌 **通用支持**：完美支持 MySQL, SQL Server, PostgreSQL, SQLite, Oracle 等所有 SqlSugar 支持的数据库。
- ⚛️ **原子事务**：完全支持多上下文（Multi-Context）操作的事务一致性。
- 🚀 **高性能**：针对高吞吐量策略评估进行了优化。
- 🎯 **运行环境**：原生支持 .NET 8.0, 9.0, 10.0。

## 📦 安装

```xml
<PackageReference Include="Casbin.NET.Adapter.SqlSugar" Version="x.x.x" />
```

或者使用 CLI：

```bash
dotnet add package Casbin.NET.Adapter.SqlSugar
```

## 🚀 快速开始

### 1. 基础用法

```csharp
using Casbin.Adapter.SqlSugar;
using SqlSugar;
using Casbin.NET;

// 1. 配置 SqlSugar
var sqlSugar = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = "...",
    DbType = DbType.MySql,
    IsAutoCloseConnection = true,
    InitKeyType = InitKeyType.Attribute
});

// 2. 创建适配器
var adapter = new SqlSugarAdapter(sqlSugar);

// 3. 初始化 Enforcer
var enforcer = new Enforcer("path/to/model.conf", adapter);

// 4. 加载并检查权限
await enforcer.LoadPolicyAsync();
if (await enforcer.EnforceAsync("alice", "data1", "read")) 
{
    // 允许访问
}
```

### 2. 依赖注入 (ASP.NET Core)

```csharp
// 在 Program.cs 中配置
services.AddScoped<ISqlSugarClient>(sp => ...); // 注册您的 SqlSugar client
services.AddScoped<IAdapter, SqlSugarAdapter>();
services.AddScoped<IEnforcer>(sp => 
{
    var adapter = sp.GetRequiredService<IAdapter>();
    return new Enforcer("model.conf", adapter);
});
```
