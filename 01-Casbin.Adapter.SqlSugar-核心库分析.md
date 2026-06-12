# 01 — Casbin.Adapter.SqlSugar 核心库分析

> **项目名称:** Casbin.NET.Adapter.SqlSugar  
> **分析目标:** `Casbin.Adapter.SqlSugar/` 核心适配器库  
> **分析日期:** 2026-06-12  
> **代码行数:** 约 2,073 行（覆盖 9 个源文件）  
> **分析方法:** 基于源代码的完整自动化提取（类结构、方法签名、配置项全覆盖）

---

## 📑 目录

1. [项目概述](#1-项目概述)
2. [文件详解](#2-文件详解)
   - [2.1 SqlSugarAdapter.cs — 主适配器公共接口](#21-sqlsugaradaptercs--主适配器公共接口)
   - [2.2 SqlSugarAdapter.Internal.cs — 内部 CRUD 实现](#22-sqlsugaradapterinternalcs--内部-crud-实现)
   - [2.3 ISqlSugarClientProvider.cs — 多租户抽象接口](#23-isqlsugarclientprovidercs--多租户抽象接口)
   - [2.4 DefaultSqlSugarClientProvider.cs — 单客户端默认实现](#24-defaultsqlsugarclientprovidercs--单客户端默认实现)
   - [2.5 MultiTenantClientProviders.cs — 多租户路由实现](#25-multitenantclientproviderscs--多租户路由实现)
   - [2.6 Entities/CasbinRule.cs — 策略实体映射](#26-entitiescasbinrulecs--策略实体映射)
   - [2.7 Extensions/ServiceCollectionExtensions.cs — DI 注册扩展](#27-extensionsservicecollectionextensionscs--di-注册扩展)
   - [2.8 Extensions/CasbinRuleExtension.cs — 数据转换工具](#28-extensionscasbinruleextensioncs--数据转换工具)
   - [2.9 Casbin.Adapter.SqlSugar.csproj — 项目配置](#29-casbinadaptersqlsugarcsproj--项目配置)
3. [配置项清单](#3-配置项清单)
4. [整体架构总结](#4-整体架构总结)
5. [导航](#5-导航)

---

## 1. 项目概述

`Casbin.Adapter.SqlSugar` 是整个解决方案的核心 NuGet 包，它实现了 Casbin.NET 的策略持久化接口（`IAdapter`、`IBatchAdapter`、`IFilteredAdapter`、`ISingleAdapter`），通过 SqlSugar ORM 将 Casbin 权限策略存储到任意关系型数据库中。

### 1.1 核心设计理念

- **面向接口编程**：通过 `ISqlSugarClientProvider` 抽象数据源，实现单数据库和多租户的统一处理
- **策略独立性**：P 类型策略（`p`/`p2`/`p3`）和 G 类型策略（`g`/`g2`/`g3`/`g4`）可以存储在不同的数据库/表中
- **事务原子性**：共享连接模式使用全局事务，独立连接模式各自提交，保障数据一致性
- **幂等设计**：写入操作先检查存在性，并通过唯一约束冲突捕获兜底

### 1.2 组件关系图

```
┌─────────────────────────────────────────────────────────────────────┐
│                          Casbin Enforcer                             │
└──────────────┬──────────────────────────────────────────────────────┘
               │  IAdapter / IBatchAdapter / IFilteredAdapter
               ▼
┌──────────────────────────────────────────────────────────────────────┐
│                       SqlSugarAdapter                                │
│  ┌────────────────────┐    ┌──────────────────────────────────────┐ │
│  │ SqlSugarAdapter.cs  │    │ SqlSugarAdapter.Internal.cs         │ │
│  │  • 公共 API 层      │    │  • 内部 CRUD 实现                   │ │
│  │  • 接口桥接         │◄───│  • 事务管理                          │ │
│  │  • 扩展点定义       │    │  • 数据库操作                        │ │
│  └────────┬───────────┘    └──────────────────────────────────────┘ │
│           │                                                          │
│           ▼                                                          │
│  ┌────────────────────┐                                              │
│  │ISqlSugarClientProvider│◄── 多租户路由抽象层                       │
│  │  • Default (单客户端) │                                           │
│  │  • PolicyType (P/G分离)│                                          │
│  │  • CustomMapping (自定义)│                                        │
│  └────────────────────┘                                              │
└──────────────────────────────────────────────────────────────────────┘
```

### 1.3 实现的接口

| 接口 | 来源 | 功能 |
|------|------|------|
| `IAdapter` | Casbin.NET Core | 基础策略加载/保存/增删 |
| `IBatchAdapter` | Casbin.NET Core | 批量增删改 + 过滤删除 |
| `IFilteredAdapter` | Casbin.NET Core | 按条件过滤加载策略 |
| `ISingleAdapter` | Casbin.NET | AutoSave 自动保存支持 |

---

## 2. 文件详解

### 2.1 SqlSugarAdapter.cs — 主适配器公共接口

**文件路径:** `Casbin.Adapter.SqlSugar/SqlSugarAdapter.cs`  
**代码行数:** 548 行  
**命名空间:** `Casbin.Adapter.SqlSugar`  
**类声明:** `public partial class SqlSugarAdapter : IAdapter, IBatchAdapter, IFilteredAdapter, ISingleAdapter`

#### 2.1.1 类结构概览

| 成员类别 | 数量 | 说明 |
|----------|------|------|
| 私有字段 | 2 | `_clientProvider`、`_autoCodeFirst` |
| 受保护属性 | 1 | `DbClient`（当前数据库客户端） |
| 公共属性 | 1 | `IsFiltered`（是否启用过滤） |
| 构造函数 | 3 | 覆盖单客户端、Provider、DI 三种场景 |
| 公共方法 | 18 | IAdapter/IBatchAdapter/IFilteredAdapter 接口实现 |
| 受保护虚方法 | 4 | 扩展点 + 数据访问辅助方法 |
| 私有方法 | 4 | 内部工具方法 |

#### 2.1.2 私有字段

```csharp
// 多租户客户端提供程序（核心抽象依赖）
private readonly ISqlSugarClientProvider _clientProvider;

// 是否自动执行 CodeFirst 建表
private readonly bool _autoCodeFirst;
```

#### 2.1.3 受保护属性

```csharp
// 获取当前操作的数据库客户端（从 _clientProvider 延迟获取）
protected ISqlSugarClient DbClient { get; }
```

#### 2.1.4 公共属性

```csharp
// 指示当前是否已设置策略过滤器
public bool IsFiltered { get; private set; }
```

#### 2.1.5 构造函数详解

| 构造函数 | 参数 | 适用场景 |
|----------|------|----------|
| `SqlSugarAdapter(ISqlSugarClient db, bool autoCodeFirst = true)` | 单一 SqlSugar 客户端 | **向后兼容**：最简单的初始化方式 |
| `SqlSugarAdapter(ISqlSugarClientProvider clientProvider, bool autoCodeFirst = true)` | 客户端提供程序 | **多租户场景**：支持 P/G 策略分离 |
| `SqlSugarAdapter(IServiceProvider serviceProvider, bool autoCodeFirst = true)` | DI 容器 | **依赖注入场景**：自动解析 `ISqlSugarClientProvider` 或回退到 `ISqlSugarClient` |

**构造函数逻辑：**

```
构造函数1 (ISqlSugarClient)
  └─► 内部包装为 DefaultSqlSugarClientProvider(db)

构造函数2 (ISqlSugarClientProvider)
  └─► 直接赋值 _clientProvider + _autoCodeFirst

构造函数3 (IServiceProvider)
  ├─► 尝试解析 ISqlSugarClientProvider → 成功则包装
  └─► 失败则解析 ISqlSugarClient → 包装为 DefaultSqlSugarClientProvider
```

#### 2.1.6 公共方法 — IAdapter 接口实现

##### LoadPolicy / LoadPolicyAsync

```csharp
// 从数据库加载所有策略到 IPolicyStore
public void LoadPolicy(IPolicyStore store)
public Task LoadPolicyAsync(IPolicyStore store)
```

**执行流程：**
1. 通过 `GetAllRulesFromStore()` 获取数据库中所有 `CasbinRule` 记录
2. 调用 `OnLoadPolicy()` 扩展点（虚方法，可被子类覆写）
3. 调用 `LoadPolicyData()` 将规则数据注入 `IPolicyStore`
4. 扫描遍历策略类型：`p`, `p2`, `p3`, `g`, `g2`, `g3`, `g4`
5. 支持多 Schema：通过 `GetClientForPolicyType()` 获取对应表的 `CasbinRule` 记录

##### SavePolicy / SavePolicyAsync

```csharp
// 将 IPolicyStore 中的策略持久化到数据库
public void SavePolicy(IPolicyStore store)
public Task SavePolicyAsync(IPolicyStore store)
```

**执行流程：**
1. 调用 `OnSavePolicy()` 扩展点
2. 从 `IPolicyStore` 提取所有策略转换为 `CasbinRule` 列表
3. 根据 `ISqlSugarClientProvider.SharesConnection` 选择事务策略：
   - **共享连接** → `SavePolicyWithSharedTransaction()`（全局事务）
   - **独立连接** → `SavePolicyWithSeparateTransactions()`（各自提交）

##### AddPolicy / AddPolicyAsync

```csharp
// 添加单条策略（幂等操作）
public void AddPolicy(string section, string policyType, IPolicyValues values)
public Task AddPolicyAsync(string section, string policyType, IPolicyValues values)
```

**幂等保证机制（双重保护）：**
1. 插入前调用 `PolicyExists()` 检查策略是否已存在 → 存在则跳过
2. 捕获唯一约束冲突异常（`IsUniqueConstraintViolation()`）→ 忽略冲突

**支持的数据库异常类型：**
- SQLite: `SqliteException`，检测 `"UNIQUE constraint failed"`
- MySQL: `MySqlConnector.MySqlException`，检测错误码 `1062`
- PostgreSQL: `PostgresException`，检测 SqlState `"23505"`

##### RemovePolicy / RemovePolicyAsync

```csharp
// 删除单条策略
public void RemovePolicy(string section, string policyType, IPolicyValues values)
public Task RemovePolicyAsync(string section, string policyType, IPolicyValues values)
```

**执行逻辑：**
- 委托到 `InternalRemovePolicy()`
- 通过 `PType` + `V0`~`V5` 字段精确匹配并删除

#### 2.1.7 公共方法 — IBatchAdapter 接口实现

| 方法 | 功能 | 事务支持 |
|------|------|----------|
| `AddPolicies(section, policyType, valuesList)` | 批量添加策略 | ✅ 事务回滚 |
| `AddPoliciesAsync(...)` | 批量添加策略（异步） | ✅ 事务回滚 |
| `RemovePolicies(section, policyType, valuesList)` | 批量删除策略 | ✅ 事务内逐条删除 |
| `RemovePoliciesAsync(...)` | 批量删除策略（异步） | ✅ 事务内逐条删除 |
| `RemoveFilteredPolicy(section, policyType, fieldIndex, fieldValues)` | 按字段过滤删除 | ✅ |
| `RemoveFilteredPolicyAsync(...)` | 按字段过滤删除（异步） | ✅ |
| `UpdatePolicy(section, policyType, oldValues, newValues)` | 更新单条策略（删旧+插新） | ✅ 事务 |
| `UpdatePolicyAsync(...)` | 更新单条策略（异步） | ✅ 事务 |
| `UpdatePolicies(section, policyType, oldValuesList, newValuesList)` | 批量更新策略 | ✅ 事务+数量校验 |
| `UpdatePoliciesAsync(...)` | 批量更新策略（异步） | ✅ 事务+数量校验 |

#### 2.1.8 公共方法 — IFilteredAdapter 接口实现

```csharp
// 按 IPolicyFilter 条件过滤加载策略
public void LoadFilteredPolicy(IPolicyStore store, IPolicyFilter filter)
public Task LoadFilteredPolicyAsync(IPolicyStore store, IPolicyFilter filter)
```

**执行流程：**
1. 根据过滤器构建 SQL 条件
2. 从数据库查询符合条件的 `CasbinRule` 记录
3. 将过滤后的结果加载到 `IPolicyStore`
4. 设置 `IsFiltered = true`

#### 2.1.9 受保护虚方法（扩展点）

| 方法 | 用途 | 覆写场景 |
|------|------|----------|
| `OnLoadPolicy(IPolicyStore store, IEnumerable<CasbinRule> policies)` | 加载策略前的处理钩子 | 日志记录、策略转换、审计 |
| `OnSavePolicy(IPolicyStore store, IEnumerable<CasbinRule> policies)` | 保存策略前的处理钩子 | 日志记录、策略验证、数据加密 |
| `GetClientForPolicyType(string policyType)` | 根据策略类型获取 SqlSugar 客户端 | 委派给 `_clientProvider` |

#### 2.1.10 私有方法详解

##### GetAllRulesFromStore

```csharp
// 遍历所有策略类型，从对应客户端获取所有 CasbinRule 记录
private List<CasbinRule> GetAllRulesFromStore(IPolicyStore store)
```

- 通过 Scan API 遍历 `p`, `p2`, `p3`, `g`, `g2`, `g3`, `g4` 策略类型
- 每种策略类型可能映射到不同的数据库表（多 Schema）
- 返回合并后的全部规则列表

##### NormalizeValue（静态）

```csharp
// 将空白字符串或空字符串归一化为 null
private static string NormalizeValue(string value)
```

- 用途：Casbin 策略中空白字段统一存储为数据库 `NULL`

##### PolicyExists / PolicyExistsAsync

```csharp
// 检查策略是否已存在（用于幂等插入）
private bool PolicyExists(ISqlSugarClient client, string policyType, IPolicyValues values, string? tableName)
private Task<bool> PolicyExistsAsync(...)
```

- 构建与策略值匹配的 WHERE 条件
- 支持自定义表名（多 Schema 场景）

##### IsUniqueConstraintViolation（静态）

```csharp
// 检测异常是否为唯一约束冲突
private static bool IsUniqueConstraintViolation(Exception ex)
```

**支持的数据库引擎：**

| 数据库 | 异常类型 | 检测方式 |
|--------|----------|----------|
| SQLite | `Microsoft.Data.Sqlite.SqliteException` | 消息包含 `"UNIQUE constraint failed"` |
| MySQL | `MySqlConnector.MySqlException` | 错误码 `1062`（Duplicate entry） |
| PostgreSQL | `Npgsql.PostgresException` | SqlState `"23505"`（unique_violation） |

##### LoadPolicyData

```csharp
// 将 CasbinRule 列表加载到 IPolicyStore
private void LoadPolicyData(IPolicyStore model, LoadPolicyLineHandler handler, List<CasbinRule> rules)
```

- 遍历规则列表
- 委托 `CasbinRuleExtension.LoadPolicyLine` 将每条规则解析注入策略存储

---

### 2.2 SqlSugarAdapter.Internal.cs — 内部 CRUD 实现

**文件路径:** `Casbin.Adapter.SqlSugar/SqlSugarAdapter.Internal.cs`  
**代码行数:** 926 行  
**命名空间:** `Casbin.Adapter.SqlSugar`  
**类声明:** `public partial class SqlSugarAdapter`（partial 扩展）

> 该文件通过 `partial class` 机制扩展 `SqlSugarAdapter`，将所有数据库 CRUD 操作集中管理，实现 **公共接口层** 与 **数据访问层** 的清晰分离。

#### 2.2.1 方法分类

| 类别 | 方法数 | 说明 |
|------|--------|------|
| InternalAdd* | 4 | 添加策略（单条/批量，同步/异步） |
| InternalRemove* | 6 | 删除策略（单条/过滤/批量，同步/异步） |
| InternalUpdate* | 4 | 更新策略（单条/批量，同步/异步） |
| SavePolicy* | 4 | Save 事务管理（共享/独立，同步/异步） |

#### 2.2.2 InternalAdd 系列

##### InternalAddPolicy

```csharp
// 单条策略添加
protected virtual void InternalAddPolicy(string section, string policyType, IPolicyValues values)
protected virtual Task InternalAddPolicyAsync(string section, string policyType, IPolicyValues values)
```

**操作步骤：**
1. 通过 `GetClientForPolicyType(policyType)` 获取对应 SqlSugar 客户端
2. 将 `IPolicyValues` 转换为 `CasbinRule` 实体
3. 利用 `.AS()` 方法指定可能不同的表名（多 Schema 支持）
4. 执行 `Insertable` 插入

##### InternalAddPolicies

```csharp
// 批量策略添加（事务包裹）
protected virtual void InternalAddPolicies(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
protected virtual Task InternalAddPoliciesAsync(...)
```

**事务保证：**
- 同一客户端的批量添加在单个事务内完成
- 任一插入失败则整体回滚
- 跨客户端时各自独立事务

#### 2.2.3 InternalRemove 系列

##### InternalRemovePolicy

```csharp
// 单条策略删除
protected virtual void InternalRemovePolicy(string section, string policyType, IPolicyValues values)
protected virtual Task InternalRemovePolicyAsync(...)
```

**WHERE 条件构建：**
- 固定匹配：`PType = policyType`
- 动态匹配：根据 `IPolicyValues` 的非空值添加 `V0`~`V5` 条件
- 多 Schema：通过 `.AS()` 指定目标表名

##### InternalRemoveFilteredPolicy

```csharp
// 按字段索引过滤删除
protected virtual void InternalRemoveFilteredPolicy(string section, string policyType, int fieldIndex, IPolicyValues fieldValues)
protected virtual Task InternalRemoveFilteredPolicyAsync(...)
```

**参数说明：**
- `fieldIndex`：从哪个字段开始匹配（0 = V0, 1 = V1, ...）
- `fieldValues`：要匹配的字段值列表

**使用场景示例：**
```
RemoveFilteredPolicy("p", "p", 1, ["data1"])
→ 删除所有 V1="data1" 的 p 类型策略
```

##### InternalRemovePolicies

```csharp
// 批量删除（事务内逐条调用 InternalRemovePolicy）
protected virtual void InternalRemovePolicies(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
protected virtual Task InternalRemovePoliciesAsync(...)
```

#### 2.2.4 InternalUpdate 系列

##### InternalUpdatePolicy

```csharp
// 单条策略更新 = 事务内：先删除旧值 + 再插入新值
protected virtual void InternalUpdatePolicy(string section, string policyType, IPolicyValues oldValues, IPolicyValues newValues)
protected virtual Task InternalUpdatePolicyAsync(...)
```

**原子性保证：** 删除和插入在同一个事务内完成。

##### InternalUpdatePolicies

```csharp
// 批量策略更新
protected virtual void InternalUpdatePolicies(
    string section, string policyType,
    IReadOnlyList<IPolicyValues> oldValuesList,
    IReadOnlyList<IPolicyValues> newValuesList)
protected virtual Task InternalUpdatePoliciesAsync(...)
```

**批量更新规则：**
1. **数量校验**：`oldValuesList.Count` 必须等于 `newValuesList.Count`，否则抛出异常
2. **逐对处理**：按索引依次"删旧→插新"
3. **事务保证**：所有操作在同一事务内

#### 2.2.5 SavePolicy 事务管理

##### SavePolicyWithSharedTransaction

```csharp
// 共享连接事务模式
private void SavePolicyWithSharedTransaction(
    IPolicyStore store,
    List<IGrouping<ISqlSugarClient, CasbinRule>> rulesByClient)
private Task SavePolicyWithSharedTransactionAsync(...)
```

**事务策略：**
1. 将策略规则按 `ISqlSugarClient` 分组
2. **SQLite 多 Client 防御**：检测 SQLite + 多个 Client 的场景，避免共享事务不兼容
3. 检测外部事务：如果已有 `IsAnyTran`，则复用外部事务
4. 否则创建新事务包裹所有客户端的写入操作
5. 统一提交或回滚

##### SavePolicyWithSeparateTransactions

```csharp
// 独立事务模式（2026/02/01 修复）
private void SavePolicyWithSeparateTransactions(...)
private Task SavePolicyWithSeparateTransactionsAsync(...)
```

**特点：**
- 每个 `ISqlSugarClient` 独立管理自己的事务
- 适用于跨数据库实例的多租户场景
- **2026/02/01 修复**：解决了之前的错误处理遗漏

---

### 2.3 ISqlSugarClientProvider.cs — 多租户抽象接口

**文件路径:** `Casbin.Adapter.SqlSugar/ISqlSugarClientProvider.cs`  
**代码行数:** 71 行  
**命名空间:** `Casbin.Adapter.SqlSugar`

```csharp
public interface ISqlSugarClientProvider
```

#### 2.3.1 接口成员

| 成员 | 类型 | 说明 |
|------|------|------|
| `GetClientForPolicyType(string policyType)` | 方法 → `ISqlSugarClient` | 根据策略类型名称（`p`/`g` 等）获取对应的数据库客户端 |
| `GetAllClients()` | 方法 → `IEnumerable<ISqlSugarClient>` | 获取所有唯一的数据库客户端（用于批量操作遍历） |
| `SharesConnection` | 属性 → `bool` | 所有客户端是否共享同一个物理数据库连接 |
| `GetTableNameForPolicyType(string policyType)` | 方法 → `string?` | 获取策略类型对应的表名（多 Schema），默认返回 `null` |

#### 2.3.2 设计意图

该接口是整个适配器架构的 **核心抽象层**，它实现了一个灵活的策略路由机制：

```
策略类型 "p"  ──► GetClientForPolicyType("p")  ──► ISqlSugarClient (指向 Policy 数据库)
策略类型 "g"  ──► GetClientForPolicyType("g")  ──► ISqlSugarClient (指向 Grouping 数据库)
策略类型 "p2" ──► GetClientForPolicyType("p2") ──► ISqlSugarClient (可能共享 Policy 连接)
```

**路由规则完全由实现类决定**，适配器本身不关心具体路由逻辑。

#### 2.3.3 SharesConnection 的含义

| 值 | 含义 | 事务行为 |
|----|------|----------|
| `true` | 所有 Client 共享同一物理连接 | 使用**全局事务**（`SavePolicyWithSharedTransaction`） |
| `false` | Client 连接不同数据库实例 | 使用**独立事务**（`SavePolicyWithSeparateTransactions`） |

---

### 2.4 DefaultSqlSugarClientProvider.cs — 单客户端默认实现

**文件路径:** `Casbin.Adapter.SqlSugar/DefaultSqlSugarClientProvider.cs`  
**代码行数:** 43 行  
**命名空间:** `Casbin.Adapter.SqlSugar`

```csharp
public class DefaultSqlSugarClientProvider : ISqlSugarClientProvider
```

#### 2.4.1 类结构

| 成员 | 详情 |
|------|------|
| 私有字段 | `_client: ISqlSugarClient`（只读，构造时注入） |
| 构造函数 | `DefaultSqlSugarClientProvider(ISqlSugarClient client)` — `null` 时抛出 `ArgumentNullException` |

#### 2.4.2 方法实现

| 方法 | 实现逻辑 |
|------|----------|
| `GetClientForPolicyType(string)` | **始终返回** `_client`（单客户端，不做路由） |
| `GetAllClients()` | 返回 `new[] { _client }`（单元素数组） |
| `SharesConnection` | 固定返回 `true`（单客户端天然共享连接） |

#### 2.4.3 使用场景

- **向后兼容**：`SqlSugarAdapter(ISqlSugarClient db)` 构造函数内部使用此实现
- **简单部署**：所有策略存储在同一数据库
- **DI 默认**：`SqlSugarAdapter(IServiceProvider)` 在无法解析 `ISqlSugarClientProvider` 时的回退方案

---

### 2.5 MultiTenantClientProviders.cs — 多租户路由实现

**文件路径:** `Casbin.Adapter.SqlSugar/MultiTenantClientProviders.cs`  
**代码行数:** 135 行  
**命名空间:** `Casbin.Adapter.SqlSugar`

#### 2.5.1 PolicyTypeClientProvider

```csharp
public class PolicyTypeClientProvider : ISqlSugarClientProvider
```

**设计目的：** 将 **P 类型**（`p`/`p2`/`p3`）和 **G 类型**（`g`/`g2`/`g3`/`g4`）策略分离到不同数据库。

| 成员 | 详情 |
|------|------|
| 字段 | `_policyClient`、`_groupingClient`、`_sharesConnection` |
| 构造函数 | `PolicyTypeClientProvider(ISqlSugarClient policyClient, ISqlSugarClient groupingClient, bool sharesConnection = false)` |

**路由规则：**

```
GetClientForPolicyType(policyType):
  ├─► policyType 以 "g" 开头 → 返回 _groupingClient
  └─► 否则                  → 返回 _policyClient
```

**GetAllClients 去重逻辑：**
- 使用 `ReferenceEquals()` 比较 `_policyClient` 和 `_groupingClient`
- 如果两者是同一实例，只返回一个（避免重复操作）

**典型场景：**
```
PolicyClient  ──► MySQL (policy 表)       ← 存储 p/p2/p3 策略
GroupingClient ──► PostgreSQL (grouping 表) ← 存储 g/g2/g3/g4 策略
```

#### 2.5.2 CustomMappingClientProvider

```csharp
public class CustomMappingClientProvider : ISqlSugarClientProvider
```

**设计目的：** 提供完全自定义的策略类型到数据库客户端的映射。

| 成员 | 详情 |
|------|------|
| 字段 | `_mappings: Dictionary<string, ISqlSugarClient>`、`_defaultClient`、`_sharesConnection` |
| 构造函数 | `CustomMappingClientProvider(Dictionary<string, ISqlSugarClient> mappings, ISqlSugarClient defaultClient, bool sharesConnection = false)` |

**路由规则：**

```
GetClientForPolicyType(policyType):
  ├─► _mappings.TryGetValue(policyType) → 找到 → 返回对应客户端
  └─► 未找到 → 返回 _defaultClient
```

**GetAllClients：**
- `_mappings.Values.ToHashSet()`（去重）
- 添加 `_defaultClient`

**典型场景：**
```csharp
var mappings = new Dictionary<string, ISqlSugarClient>
{
    ["p"] = mysqlClient,
    ["g"] = postgresqlClient,
    ["p2"] = sqlServerClient,  // 多级策略存 SQL Server
};
var provider = new CustomMappingClientProvider(mappings, defaultClient);
```

---

### 2.6 Entities/CasbinRule.cs — 策略实体映射

**文件路径:** `Casbin.Adapter.SqlSugar/Entities/CasbinRule.cs`  
**代码行数:** 132 行  
**命名空间:** `Casbin.Adapter.SqlSugar.Entities`

```csharp
[SugarTable("casbin_rule")]
public class CasbinRule : IPersistPolicy
```

#### 2.6.1 数据库表映射

| 属性 | C# 类型 | 数据库列配置 | 说明 |
|------|---------|-------------|------|
| `Id` | `int` | `[SugarColumn(IsPrimaryKey = true, IsIdentity = true)]` | 自增主键 |
| `PType` | `string` | 长度 254, `IndexGroup = "ux_casbin_rule"` | 策略类型（`p`/`g` 等） |
| `V0` | `string` | 长度 254, `IndexGroup = "index_v0"` + `ux_casbin_rule` | 策略字段 0（sub/role） |
| `V1` | `string` | 长度 254, `IndexGroup = "index_v1"` + `ux_casbin_rule` | 策略字段 1（obj/role2） |
| `V2` | `string` | 长度 254, `IndexGroup = "index_v2"` + `ux_casbin_rule` | 策略字段 2（act） |
| `V3` | `string` | 长度 254, `IndexGroup = "index_v3"` + `ux_casbin_rule` | 策略字段 3 |
| `V4` | `string` | 长度 254, `IndexGroup = "index_v4"` + `ux_casbin_rule` | 策略字段 4 |
| `V5` | `string` | 长度 254, `IndexGroup = "index_v5"` + `ux_casbin_rule` | 策略字段 5 |
| `V6`~`V14` | `string` | 长度 254, **无索引** | 扩展策略字段（仅存储） |

#### 2.6.2 索引策略

```
┌──────────────────────────────────────────────────────┐
│                 casbin_rule 表索引                     │
├──────────┬───────────────────────────────────────────┤
│ 单列索引 │ index_v0 (V0), index_v1 (V1), ... index_v5 │
├──────────┼───────────────────────────────────────────┤
│ 联合索引 │ ux_casbin_rule (PType + V0~V5)             │
│          │ → 用于幂等检查和去重                        │
│          │ → 用于 RemovePolicy 精确匹配                │
└──────────┴───────────────────────────────────────────┘
```

> **设计说明：** `V0`~`V5` 同时拥有单列索引和联合索引，单列索引用于过滤查询加速，联合索引用于幂等检查和精确匹配。`V6`~`V14` 为扩展字段，不建索引以节省存储空间。

#### 2.6.3 IPersistPolicy 接口属性

所有 `IPersistPolicy` 接口属性均标记 `[SugarColumn(IsIgnore = true)]`，不映射到数据库：

| 接口属性 | 映射到 |
|----------|--------|
| `Type` | → `PType` |
| `Section` | → `PType[0]`（取首字符） |
| `Value0`~`Value14` | → `V0`~`V14` |

#### 2.6.4 ToString 方法

```csharp
public override string ToString()
    => $"{PType}, {V0}, {V1}, {V2}, {V3}, {V4}, {V5}";
```

输出格式示例：`"p, alice, data1, read, , , "`

---

### 2.7 Extensions/ServiceCollectionExtensions.cs — DI 注册扩展

**文件路径:** `Casbin.Adapter.SqlSugar/Extensions/ServiceCollectionExtensions.cs`  
**代码行数:** 128 行  
**命名空间:** `Casbin.Adapter.SqlSugar.Extensions`

```csharp
public static class ServiceCollectionExtensions
```

#### 2.7.1 注册方法概览

| 方法 | 用途 |
|------|------|
| `AddSqlSugarCasbinAdapter(IServiceCollection, Action<ConnectionConfig>, ServiceLifetime)` | 配置连接 + 自动注册 |
| `AddSqlSugarCasbinAdapter(IServiceCollection, ServiceLifetime)` | 使用已注册的 `ISqlSugarClient` |
| `AddSqlSugarCasbinAdapterWithProvider(IServiceCollection, Func<IServiceProvider, ISqlSugarClientProvider>, ServiceLifetime)` | 多租户 Provider 工厂 |
| `AddSqlSugarCasbinAdapterWithProvider(IServiceCollection, ServiceLifetime)` | 使用已注册的 `ISqlSugarClientProvider` |

**所有方法返回 `IServiceCollection`**，支持链式调用。

#### 2.7.2 方法详解

##### 重载 1：配置连接字符串

```csharp
services.AddSqlSugarCasbinAdapter(config =>
{
    config.ConnectionString = "DataSource=:memory:";
    config.DbType = DbType.Sqlite;
});
```

**内部注册：**
1. `ISqlSugarClient` → `SqlSugarClient`（根据配置创建）
2. `IAdapter` → `SqlSugarAdapter`（自动包装）

##### 重载 2：使用已注册 Client

```csharp
services.AddSingleton<ISqlSugarClient>(sp => new SqlSugarClient(config));
services.AddSqlSugarCasbinAdapter(); // 自动发现已注册的 ISqlSugarClient
```

##### 重载 3 & 4：多租户 Provider

```csharp
// 方式 A：工厂函数
services.AddSqlSugarCasbinAdapterWithProvider(sp =>
{
    var policyClient = sp.GetRequiredService<ISqlSugarClient>();
    var groupingClient = new SqlSugarClient(groupingConfig);
    return new PolicyTypeClientProvider(policyClient, groupingClient);
});

// 方式 B：已注册 Provider
services.AddSingleton<ISqlSugarClientProvider>(provider);
services.AddSqlSugarCasbinAdapterWithProvider();
```

#### 2.7.3 TryAdd 防重复机制

所有注册均使用 `TryAdd` 语义，确保：
- 如果 `ISqlSugarClient` 已注册，不会覆盖
- 如果 `IAdapter` 已注册，不会覆盖
- 支持用户在调用前自行注册自定义实现

---

### 2.8 Extensions/CasbinRuleExtension.cs — 数据转换工具

**文件路径:** `Casbin.Adapter.SqlSugar/Extensions/CasbinRuleExtension.cs`  
**代码行数:** 89 行  
**命名空间:** `Casbin.Adapter.SqlSugar.Extensions`

```csharp
public static class CasbinRuleExtension
```

#### 2.8.1 委托定义

```csharp
// 策略行加载委托
public delegate void LoadPolicyLineHandler<TKey, TValue>(TKey key, TValue value);
```

#### 2.8.2 静态方法

##### LoadPolicyLine

```csharp
// 将一条 CasbinRule 记录加载到 IPolicyStore
public static void LoadPolicyLine(CasbinRule rule, IPolicyStore store)
```

**处理逻辑：**
1. 从 `V0`~`V5` 中提取非空值，组成规则值列表
2. 根据 `PType` 首字符判断 section：
   - 首字符 `'g'` → section = `"g"`
   - 其他 → section = `"p"`
3. 调用 `store.AddPolicy(section, rule.PType, values)`

**示例：**
```
规则: PType="g", V0="alice", V1="admin"
→ store.AddPolicy("g", "g", ["alice", "admin"])

规则: PType="p", V0="alice", V1="data1", V2="read"
→ store.AddPolicy("p", "p", ["alice", "data1", "read"])
```

##### ToCasbinRule (from IList\<string\>)

```csharp
// 从字符串列表创建 CasbinRule 实体
public static CasbinRule ToCasbinRule(string ptype, IList<string> rule)
```

- 遍历规则列表，将每个值赋给 `V0`~`Vn`
- 空白字符归一化为 `null`

##### ToCasbinRule (from IPolicyValues)

```csharp
// 从 IPolicyValues 创建 CasbinRule 实体
public static CasbinRule ToCasbinRule(string ptype, IPolicyValues values)
```

- **边界检查**：`values.Count` 超过 15 时抛出异常（因为只有 V0~V14）
- 逐一复制 `values[0]`→`V0`, `values[1]`→`V1`, ...

---

### 2.9 Casbin.Adapter.SqlSugar.csproj — 项目配置

**文件路径:** `Casbin.Adapter.SqlSugar/Casbin.Adapter.SqlSugar.csproj`

#### 2.9.1 构建配置

| 配置项 | 值 | 说明 |
|--------|-----|------|
| SDK | `Microsoft.NET.Sdk` | 标准 .NET SDK |
| `TargetFrameworks` | `net10.0;net9.0;net8.0` | 多目标框架 |
| `LangVersion` | `latest` | 使用最新 C# 语言特性 |
| `Nullable` | `annotations` | 启用可空引用类型注解 |
| `GenerateDocumentationFile` | `true` | 生成 XML 文档文件 |
| `NoWarn` | `CS1591` | 忽略缺少 XML 注释警告 |

#### 2.9.2 NuGet 包配置

| 配置项 | 值 |
|--------|-----|
| `PackageId` | `Casbin.NET.Adapter.SqlSugar` |
| `Version` | `1.0.3` |
| `Authors` | `SharpFort` |
| `License` | `Apache-2.0` |

#### 2.9.3 NuGet 依赖

| 包名 | 版本 | 用途 |
|------|------|------|
| `Casbin.NET` | `2.19.1` | Casbin 核心库（提供 IAdapter/IBatchAdapter/IFilteredAdapter 接口） |
| `SqlSugarCore` | `5.1.4.211` | SqlSugar ORM 核心（数据库访问） |
| `Npgsql` | `10.0.1` | PostgreSQL 驱动（用于 `IsUniqueConstraintViolation` 异常检测） |

#### 2.9.4 符号包

- 格式：`snupkg`
- 发布到 NuGet.org 的符号服务器，支持源码调试

---

## 3. 配置项清单

### 3.1 构造函数配置

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `autoCodeFirst` | `bool` | `true` | 是否在首次加载时自动执行 `InitTables<CasbinRule>()` 建表 |
| `db` (单客户端) | `ISqlSugarClient` | — | SqlSugar 数据库客户端实例 |
| `clientProvider` (多租户) | `ISqlSugarClientProvider` | — | 多租户客户端路由提供程序 |

### 3.2 ISqlSugarClientProvider 配置

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `SharesConnection` | `bool` | `true` (Default) / `false` (MultiTenant) | 是否共享同一物理连接，影响事务策略 |

### 3.3 DI 注册配置

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `ServiceLifetime` | `ServiceLifetime` | `Scoped` | `ISqlSugarClient` 和 `IAdapter` 的服务生命周期 |

### 3.4 运行时行为

| 行为 | 默认 | 说明 |
|------|------|------|
| 幂等添加 | ✅ 启用 | 通过 `PolicyExists` 检查 + 唯一约束冲突捕获 |
| CodeFirst 建表 | ✅ 启用 | `autoCodeFirst = true` 时自动建表 |
| 唯一约束冲突容忍 | ✅ 启用 | 检测 SQLite/MySQL/PG 三种异常类型 |

---

## 4. 整体架构总结

### 4.1 分层架构

```
┌───────────────────────────────────────────────────────┐
│                   公共接口层 (API)                       │
│  SqlSugarAdapter.cs                                    │
│  ├─ IAdapter, IBatchAdapter, IFilteredAdapter          │
│  ├─ 策略加载/保存/增删改查                              │
│  └─ 扩展点：OnLoadPolicy, OnSavePolicy                  │
├───────────────────────────────────────────────────────┤
│                   数据访问层 (DAL)                       │
│  SqlSugarAdapter.Internal.cs                           │
│  ├─ InternalAdd/Remove/Update 系列                      │
│  ├─ 事务管理（共享/独立）                                │
│  └─ 唯一约束冲突检测                                     │
├───────────────────────────────────────────────────────┤
│                   抽象路由层 (Abstraction)                │
│  ISqlSugarClientProvider                               │
│  ├─ DefaultSqlSugarClientProvider (单客户端)            │
│  ├─ PolicyTypeClientProvider (P/G 分离)                 │
│  └─ CustomMappingClientProvider (自定义映射)            │
├───────────────────────────────────────────────────────┤
│                   实体层 (Entity)                        │
│  CasbinRule → casbin_rule 表                            │
│  ├─ PType + V0~V14 字段                                 │
│  └─ 双索引策略（单列 + 联合唯一）                         │
├───────────────────────────────────────────────────────┤
│                   基础设施层 (Infrastructure)             │
│  ├─ ServiceCollectionExtensions (DI)                    │
│  ├─ CasbinRuleExtension (数据转换)                      │
│  └─ .csproj (构建/打包配置)                              │
└───────────────────────────────────────────────────────┘
```

### 4.2 设计模式总结

| 设计模式 | 应用位置 | 说明 |
|----------|----------|------|
| **策略模式 (Strategy)** | `ISqlSugarClientProvider` + 三种实现 | 运行时切换数据源路由策略 |
| **模板方法 (Template Method)** | `InternalAdd/Remove/Update` 虚方法 | 子类可覆写 CRUD 行为 |
| **工厂模式 (Factory)** | `ServiceCollectionExtensions` | DI 工厂创建 `SqlSugarAdapter` |
| **适配器模式 (Adapter)** | `SqlSugarAdapter` 本身 | 适配 Casbin ↔ SqlSugar |
| **外观模式 (Facade)** | `SqlSugarAdapter.cs` | 统一接口屏蔽 Internal 实现细节 |
| **分部类 (Partial Class)** | `SqlSugarAdapter.cs` + `.Internal.cs` | 物理分离 API 与实现 |

### 4.3 事务策略决策树

```
SavePolicy() 被调用
    │
    ├─► ISqlSugarClientProvider.SharesConnection == true?
    │       │
    │       ├─ YES ──► SavePolicyWithSharedTransaction()
    │       │            ├─ 已有外部事务? → 复用
    │       │            └─ 无外部事务   → 新建全局事务
    │       │
    │       └─ NO  ──► SavePolicyWithSeparateTransactions()
    │                    └─ 每个 Client 独立事务
```

### 4.4 幂等添加保障链

```
AddPolicy() 被调用
    │
    ├─► 1. PolicyExists() 检查
    │       ├─ 已存在 → 跳过 (return)
    │       └─ 不存在 → 继续
    │
    ├─► 2. 执行 INSERT
    │       ├─ 成功 → 完成
    │       └─ 异常 →
    │
    └─► 3. IsUniqueConstraintViolation() 判断
            ├─ SQLite: UNIQUE constraint failed
            ├─ MySQL: Error 1062
            ├─ PG: SqlState 23505
            ├─ 是 → 忽略 (幂等)
            └─ 否 → 重新抛出异常
```

### 4.5 多 Schema 支持

通过 `ISqlSugarClientProvider.GetTableNameForPolicyType()` + SqlSugar `.AS()` 方法实现：

```sql
-- 默认 Schema (public.casbin_rule)
SELECT * FROM casbin_rule WHERE PType = 'p';

-- 自定义 Schema (tenant_a.casbin_rule)
SELECT * FROM tenant_a.casbin_rule WHERE PType = 'p';
```

### 4.6 关键设计决策

| 决策 | 理由 |
|------|------|
| **partial class 分离 API 与实现** | 公共接口文件保持简洁，Internal 文件承载复杂 CRUD 逻辑 |
| **PType 路由而非 Section 路由** | 更细粒度，支持 `p2`/`p3`/`g2`/`g3`/`g4` 等多种策略子类型 |
| **V0~V5 双重索引** | 单列索引加速过滤查询，联合唯一索引保证幂等 |
| **PolicyExists + 异常捕获双重保障** | 杜绝竞态条件下的重复插入 |
| **null 参数检查在 DefaultSqlSugarClientProvider** | 在边界处抛出异常，快速失败 |

---

## 5. 导航

| 编号 | 文档 | 说明 |
|------|------|------|
| [00](./00-总体分析报告.md) | **总体分析报告** | 项目全景概述、架构总览、优化建议 |
| **01** | **核心库分析**（本文档） | `Casbin.Adapter.SqlSugar/` 主适配器库 |
| [02](./02-Casbin.Adapter.SqlSugar.UnitTest-单元测试分析.md) | **单元测试分析** | `Casbin.Adapter.SqlSugar.UnitTest/` 42 个单元测试 |
| [03](./03-Casbin.Adapter.SqlSugar.IntegrationTest-集成测试分析.md) | **集成测试分析** | `Casbin.Adapter.SqlSugar.IntegrationTest/` 19 个集成测试（PostgreSQL） |
| [04](./04-根目录配置文件分析.md) | **根目录/配置文件分析** | 解决方案、NuGet、CI/CD、文档等 |

---

> 📝 **文档生成信息**  
> - 生成时间: 2026-06-12  
> - 分析来源: 基于源代码的完整自动化提取  
> - 覆盖文件: 9 个（7 个 .cs 源码 + 1 个 .csproj 项目文件 + 1 个接口）  
> - 代码总行数: ~2,073 行
