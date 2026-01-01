# 事务一致性修复 (2026-01-01)

## 概述
本文档总结了 `Casbin.Adapter.SqlSugar` 的关键更新，旨在确保策略持久化操作期间的数据一致性和事务完整性。

**日期**: 2026-01-01
**组件**: `Casbin.Adapter.SqlSugar/SqlSugarAdapter.Internal.cs`
**重点**: 策略操作的 ACID 合规性

## 问题分析
先前的分析显示，适配器中的几个变更操作缺乏适当的事务边界：
1.  **原子性违反**：“删除旧值 + 插入新值” 操作（`UpdatePolicy`）被作为单独的命令执行。如果第二步失败，第一步仍然提交，导致数据丢失。
2.  **批量不一致**：批量操作（`AddPolicies`、`RemovePolicies`、`UpdatePolicies`）按顺序处理项目。中途失败会导致数据库处于部分更新的状态（孤儿记录）。

## 已应用的修复

### 1. 更新策略（单条）
- **方法**: `UpdatePolicy` / `UpdatePolicyAsync`
- **变更**: 将 “删除旧值” + “添加新值” 序列包裹在事务中 (`BeginTran` / `CommitTran`)。
- **收益**: 确保策略更新是一个原子操作。要么策略完全更新，要么保持不变。

### 2. 批量更新策略
- **方法**: `UpdatePolicies` / `UpdatePoliciesAsync`
- **变更**: 将整个迭代循环包含在单个事务中。
- **优化**: 在循环内部 **内联** 了添加/删除逻辑，而不是调用单条 `UpdatePolicy` 方法。这避免了“嵌套事务”开销，并确保整个批次被视为一个原子工作单元。

### 3. 批量删除策略
- **方法**: `RemovePolicies` / `RemovePoliciesAsync`
- **变更**: 在删除循环周围添加了事务包装器。
- **收益**: 防止部分删除。如果任何删除失败，整个集合将保留其原始状态。

### 4. 批量添加策略
- **方法**: `AddPolicies` / `AddPoliciesAsync`
- **变更**: 显式地将批量插入操作包裹在事务中。
- **收益**: 虽然 `Insertable(list)` 通常是原子的，但显式事务为所有数据库提供程序和复杂场景提供了保证。

## 技术实现细节

实现遵循以下使用 SqlSugar ADO 事务 API 的模式：

```csharp
var client = GetClientForPolicyType(policyType);
try
{
    client.Ado.BeginTran();
    
    // ... 关键操作 ...
    // ... 删除 / 插入 / 循环 ...
    
    client.Ado.CommitTran();
}
catch
{
    client.Ado.RollbackTran();
    throw;
}
```

## 事务状态总结

| 操作 | 内部方法 | 事务状态 | 备注 |
| :--- | :--- | :--- | :--- |
| **SavePolicy** | `SavePolicy` | ✅ **已存在** | 正确使用了 `BeginTran`。 |
| **AddPolicy** | `InternalAddPolicy` | ➖ **无** | 单条插入本身是原子的。 |
| **AddPolicies** | `InternalAddPolicies` | ✅ **已添加** | 批量插入已保护。 |
| **RemovePolicy** | `InternalRemovePolicy` | ➖ **无** | 单条删除本身是原子的。 |
| **RemovePolicies** | `InternalRemovePolicies` | ✅ **已添加** | 批量删除循环已保护。 |
| **UpdatePolicy** | `InternalUpdatePolicy` | ✅ **已添加** | “删除 + 插入” 序列已保护。 |
| **UpdatePolicies** | `InternalUpdatePolicies` | ✅ **已添加** | 批量更新循环已保护。 |

这些更改确保 `Casbin.Adapter.SqlSugar` 现在为所有标准 Casbin 持久化操作提供强大的数据一致性保证。
