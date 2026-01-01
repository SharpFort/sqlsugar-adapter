using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Casbin.Model;
using Casbin.Persist;
using SqlSugar;
using Casbin.Adapter.SqlSugar.Entities;
using Casbin.Adapter.SqlSugar.Extensions;

namespace Casbin.Adapter.SqlSugar
{
    /// <summary>
    /// SqlSugarAdapter 内部实现方法
    /// </summary>
    public partial class SqlSugarAdapter
    {
        #region 内部 Add 方法

        /// <summary>
        /// 【2024/12/21 多 Schema 支持】
        /// 同步添加单条策略。使用 GetTableNameForPolicyType 获取目标表名，
        /// 通过 .AS() 方法在运行时指定表，确保多 Schema 场景下能正确写入。
        /// </summary>
        protected virtual void InternalAddPolicy(string section, string policyType, IPolicyValues values)
        {
            var client = GetClientForPolicyType(policyType);
            var rule = CasbinRuleExtension.ToCasbinRule(policyType, values);
            
            // 获取多 Schema 场景下的完全限定表名
            var tableName = _clientProvider.GetTableNameForPolicyType(policyType);
            if (!string.IsNullOrEmpty(tableName))
            {
                client.Insertable(rule).AS(tableName).ExecuteCommand();
            }
            else
            {
                client.Insertable(rule).ExecuteCommand();
            }
        }

        /// <summary>
        /// 【2024/12/21 多 Schema 支持】
        /// 异步添加单条策略。使用 GetTableNameForPolicyType 获取目标表名。
        /// </summary>
        protected virtual async Task InternalAddPolicyAsync(string section, string policyType, IPolicyValues values)
        {
            var client = GetClientForPolicyType(policyType);
            var rule = CasbinRuleExtension.ToCasbinRule(policyType, values);
            
            // 获取多 Schema 场景下的完全限定表名
            var tableName = _clientProvider.GetTableNameForPolicyType(policyType);
            
            // 单条插入通常是原子的，不需要显式事务，除非有特殊需求
            if (!string.IsNullOrEmpty(tableName))
            {
                await client.Insertable(rule).AS(tableName).ExecuteCommandAsync();
            }
            else
            {
                await client.Insertable(rule).ExecuteCommandAsync();
            }
        }

        /// <summary>
        /// 【2024/12/21 多 Schema 支持】
        /// 同步批量添加策略。使用 GetTableNameForPolicyType 获取目标表名。
        /// </summary>
        protected virtual void InternalAddPolicies(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            var client = GetClientForPolicyType(policyType);
            var rules = valuesList.Select(v => CasbinRuleExtension.ToCasbinRule(policyType, v)).ToList();
            
            // 获取多 Schema 场景下的完全限定表名
            var tableName = _clientProvider.GetTableNameForPolicyType(policyType);
            
            try
            {
                client.Ado.BeginTran();
                
                if (!string.IsNullOrEmpty(tableName))
                {
                    client.Insertable(rules).AS(tableName).ExecuteCommand();
                }
                else
                {
                    client.Insertable(rules).ExecuteCommand();
                }
                
                client.Ado.CommitTran();
            }
            catch
            {
                client.Ado.RollbackTran();
                throw;
            }
        }

        /// <summary>
        /// 【2024/12/21 多 Schema 支持】
        /// 异步批量添加策略。使用 GetTableNameForPolicyType 获取目标表名。
        /// </summary>
        protected virtual async Task InternalAddPoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            var client = GetClientForPolicyType(policyType);
            var rules = valuesList.Select(v => CasbinRuleExtension.ToCasbinRule(policyType, v)).ToList();
            
            // 获取多 Schema 场景下的完全限定表名
            var tableName = _clientProvider.GetTableNameForPolicyType(policyType);
            
            try
            {
                client.Ado.BeginTran();
                
                if (!string.IsNullOrEmpty(tableName))
                {
                    await client.Insertable(rules).AS(tableName).ExecuteCommandAsync();
                }
                else
                {
                    await client.Insertable(rules).ExecuteCommandAsync();
                }
                
                client.Ado.CommitTran();
            }
            catch
            {
                client.Ado.RollbackTran();
                throw;
            }
        }

        #endregion

        #region 内部 Remove 方法

        protected virtual void InternalRemovePolicy(string section, string policyType, IPolicyValues values)
        {
            var client = GetClientForPolicyType(policyType);
            var query = client.Deleteable<CasbinRule>().Where(r => r.PType == policyType);
            
            for (int i = 0; i < Math.Min(values.Count, 6); i++)
            {
                var value = values[i];
                if (!string.IsNullOrEmpty(value))
                {
                    switch (i)
                    {
                        case 0: query = query.Where(r => r.V0 == value); break;
                        case 1: query = query.Where(r => r.V1 == value); break;
                        case 2: query = query.Where(r => r.V2 == value); break;
                        case 3: query = query.Where(r => r.V3 == value); break;
                        case 4: query = query.Where(r => r.V4 == value); break;
                        case 5: query = query.Where(r => r.V5 == value); break;
                    }
                }
            }
            
            query.ExecuteCommand();
        }

        protected virtual async Task InternalRemovePolicyAsync(string section, string policyType, IPolicyValues values)
        {
            var client = GetClientForPolicyType(policyType);
            var query = client.Deleteable<CasbinRule>().Where(r => r.PType == policyType);
            
            for (int i = 0; i < Math.Min(values.Count, 6); i++)
            {
                var value = values[i];
                if (!string.IsNullOrEmpty(value))
                {
                    switch (i)
                    {
                        case 0: query = query.Where(r => r.V0 == value); break;
                        case 1: query = query.Where(r => r.V1 == value); break;
                        case 2: query = query.Where(r => r.V2 == value); break;
                        case 3: query = query.Where(r => r.V3 == value); break;
                        case 4: query = query.Where(r => r.V4 == value); break;
                        case 5: query = query.Where(r => r.V5 == value); break;
                    }
                }
            }
            
            await query.ExecuteCommandAsync();
        }

        protected virtual void InternalRemoveFilteredPolicy(string section, string policyType, int fieldIndex, IPolicyValues fieldValues)
        {
            var client = GetClientForPolicyType(policyType);
            var query = client.Deleteable<CasbinRule>().Where(r => r.PType == policyType);
            
            for (int i = 0; i < fieldValues.Count && (fieldIndex + i) < 6; i++)
            {
                var value = fieldValues[i];
                if (string.IsNullOrEmpty(value)) continue;
                
                switch (fieldIndex + i)
                {
                    case 0: query = query.Where(r => r.V0 == value); break;
                    case 1: query = query.Where(r => r.V1 == value); break;
                    case 2: query = query.Where(r => r.V2 == value); break;
                    case 3: query = query.Where(r => r.V3 == value); break;
                    case 4: query = query.Where(r => r.V4 == value); break;
                    case 5: query = query.Where(r => r.V5 == value); break;
                }
            }
            
            query.ExecuteCommand();
        }

        protected virtual async Task InternalRemoveFilteredPolicyAsync(string section, string policyType, int fieldIndex, IPolicyValues fieldValues)
        {
            var client = GetClientForPolicyType(policyType);
            var query = client.Deleteable<CasbinRule>().Where(r => r.PType == policyType);
            
            for (int i = 0; i < fieldValues.Count && (fieldIndex + i) < 6; i++)
            {
                var value = fieldValues[i];
                if (string.IsNullOrEmpty(value)) continue;
                
                switch (fieldIndex + i)
                {
                    case 0: query = query.Where(r => r.V0 == value); break;
                    case 1: query = query.Where(r => r.V1 == value); break;
                    case 2: query = query.Where(r => r.V2 == value); break;
                    case 3: query = query.Where(r => r.V3 == value); break;
                    case 4: query = query.Where(r => r.V4 == value); break;
                    case 5: query = query.Where(r => r.V5 == value); break;
                }
            }
            
            await query.ExecuteCommandAsync();
        }

        protected virtual void InternalRemovePolicies(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            var client = GetClientForPolicyType(policyType);
            try
            {
                client.Ado.BeginTran();
                
                foreach (var values in valuesList)
                {
                    InternalRemovePolicy(section, policyType, values);
                }
                
                client.Ado.CommitTran();
            }
            catch
            {
                client.Ado.RollbackTran();
                throw;
            }
        }

        protected virtual async Task InternalRemovePoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            var client = GetClientForPolicyType(policyType);
            try
            {
                client.Ado.BeginTran();
                
                foreach (var values in valuesList)
                {
                    await InternalRemovePolicyAsync(section, policyType, values);
                }
                
                client.Ado.CommitTran();
            }
            catch
            {
                client.Ado.RollbackTran();
                throw;
            }
        }

        #endregion

        #region 内部 Update 方法

        protected virtual void InternalUpdatePolicy(string section, string policyType, IPolicyValues oldValues, IPolicyValues newValues)
        {
            var client = GetClientForPolicyType(policyType);
            try
            {
                client.Ado.BeginTran();
                InternalRemovePolicy(section, policyType, oldValues);
                InternalAddPolicy(section, policyType, newValues);
                client.Ado.CommitTran();
            }
            catch
            {
                client.Ado.RollbackTran();
                throw;
            }
        }

        protected virtual async Task InternalUpdatePolicyAsync(string section, string policyType, IPolicyValues oldValues, IPolicyValues newValues)
        {
            var client = GetClientForPolicyType(policyType);
            try
            {
                client.Ado.BeginTran();
                await InternalRemovePolicyAsync(section, policyType, oldValues);
                await InternalAddPolicyAsync(section, policyType, newValues);
                client.Ado.CommitTran();
            }
            catch
            {
                client.Ado.RollbackTran();
                throw;
            }
        }

        protected virtual void InternalUpdatePolicies(string section, string policyType, IReadOnlyList<IPolicyValues> oldValuesList, IReadOnlyList<IPolicyValues> newValuesList)
        {
            if (oldValuesList.Count != newValuesList.Count)
            {
                throw new ArgumentException("Old and new values lists must have the same count");
            }
            
            var client = GetClientForPolicyType(policyType);
            try
            {
                client.Ado.BeginTran();
                
                for (int i = 0; i < oldValuesList.Count; i++)
                {
                    // Directly call atomic delete/insert to avoid nested transaction from InternalUpdatePolicy
                    InternalRemovePolicy(section, policyType, oldValuesList[i]);
                    InternalAddPolicy(section, policyType, newValuesList[i]);
                }
                
                client.Ado.CommitTran();
            }
            catch
            {
                client.Ado.RollbackTran();
                throw;
            }
        }

        protected virtual async Task InternalUpdatePoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> oldValuesList, IReadOnlyList<IPolicyValues> newValuesList)
        {
            if (oldValuesList.Count != newValuesList.Count)
            {
                throw new ArgumentException("Old and new values lists must have the same count");
            }
            
            var client = GetClientForPolicyType(policyType);
            try
            {
                client.Ado.BeginTran();
                
                for (int i = 0; i < oldValuesList.Count; i++)
                {
                    // Directly call atomic delete/insert to avoid nested transaction from InternalUpdatePolicy
                    await InternalRemovePolicyAsync(section, policyType, oldValuesList[i]);
                    await InternalAddPolicyAsync(section, policyType, newValuesList[i]);
                }
                
                client.Ado.CommitTran();
            }
            catch
            {
                client.Ado.RollbackTran();
                throw;
            }
        }

        #endregion

        #region 内部 Save 事务处理方法

        /// <summary>
        /// 【集成测试多 Schema 支持 - 2024/12/21 重构】
        /// 使用共享事务保存策略到多个 Schema/表（同步版本）。
        /// 
        /// 重构说明：同 SavePolicyWithSharedTransactionAsync，使用 .AS() 方法支持多 Schema。
        /// </summary>
        private void SavePolicyWithSharedTransaction(IPolicyStore store, List<IGrouping<ISqlSugarClient, CasbinRule>> rulesByClient)
        {
            var primaryClient = rulesByClient.First().Key;
            
            try
            {
                primaryClient.Ado.BeginTran();
                
                // 【多 Schema 支持】删除时需要根据策略类型确定目标表
                foreach (var group in rulesByClient)
                {
                    var client = group.Key;
                    var firstRule = group.FirstOrDefault();
                    if (firstRule == null) continue;
                    
                    var tableName = _clientProvider.GetTableNameForPolicyType(firstRule.PType);
                    
                    if (!string.IsNullOrEmpty(tableName))
                    {
                        client.Deleteable<CasbinRule>().AS(tableName).ExecuteCommand();
                    }
                    else
                    {
                        client.Deleteable<CasbinRule>().ExecuteCommand();
                    }
                }
                
                // 【多 Schema 支持】插入时使用正确的表名
                foreach (var group in rulesByClient)
                {
                    var client = group.Key;
                    var rules = OnSavePolicy(store, group).ToList();
                    if (rules.Any())
                    {
                        var policyType = rules.First().PType;
                        var tableName = _clientProvider.GetTableNameForPolicyType(policyType);
                        
                        if (!string.IsNullOrEmpty(tableName))
                        {
                            client.Insertable(rules).AS(tableName).ExecuteCommand();
                        }
                        else
                        {
                            client.Insertable(rules).ExecuteCommand();
                        }
                    }
                }
                
                primaryClient.Ado.CommitTran();
            }
            catch
            {
                primaryClient.Ado.RollbackTran();
                throw;
            }
        }

        /// <summary>
        /// 【集成测试多 Schema 支持 - 2024/12/21 重构】
        /// 使用共享事务保存策略到多个 Schema/表。
        /// 
        /// 重构说明：
        /// - 原始实现使用默认表名，无法支持 PostgreSQL 多 Schema 场景
        /// - 新实现通过 GetTableNameForPolicyType 获取每个策略类型对应的完全限定表名
        /// - 使用 SqlSugar 的 .AS(tableName) 方法在运行时指定目标表
        /// - 当 GetTableNameForPolicyType 返回 null 时，使用默认表名（保持向后兼容）
        /// </summary>
        private async Task SavePolicyWithSharedTransactionAsync(IPolicyStore store, List<IGrouping<ISqlSugarClient, CasbinRule>> rulesByClient)
        {
            var primaryClient = rulesByClient.First().Key;
            
            try
            {
                primaryClient.Ado.BeginTran();
                
                // 【多 Schema 支持】删除时需要根据策略类型确定目标表
                // 遍历所有规则组，为每个策略类型执行删除操作
                foreach (var group in rulesByClient)
                {
                    var client = group.Key;
                    // 获取该策略类型的第一个规则以确定 policyType
                    var firstRule = group.FirstOrDefault();
                    if (firstRule == null) continue;
                    
                    // 获取此策略类型对应的完全限定表名（如 "schema.table"）
                    var tableName = _clientProvider.GetTableNameForPolicyType(firstRule.PType);
                    
                    // 使用 .AS() 方法指定目标表，如果 tableName 为 null 则使用默认表名
                    if (!string.IsNullOrEmpty(tableName))
                    {
                        await client.Deleteable<CasbinRule>().AS(tableName).ExecuteCommandAsync();
                    }
                    else
                    {
                        // 回退到原始行为：使用默认表名
                        await client.Deleteable<CasbinRule>().ExecuteCommandAsync();
                    }
                }
                
                // 【多 Schema 支持】插入时同样需要使用正确的表名
                foreach (var group in rulesByClient)
                {
                    var client = group.Key;
                    var rules = OnSavePolicy(store, group).ToList();
                    if (rules.Any())
                    {
                        // 获取此策略类型对应的完全限定表名
                        var policyType = rules.First().PType;
                        var tableName = _clientProvider.GetTableNameForPolicyType(policyType);
                        
                        // 使用 .AS() 方法指定目标表
                        if (!string.IsNullOrEmpty(tableName))
                        {
                            await client.Insertable(rules).AS(tableName).ExecuteCommandAsync();
                        }
                        else
                        {
                            // 回退到原始行为：使用默认表名
                            await client.Insertable(rules).ExecuteCommandAsync();
                        }
                    }
                }
                
                primaryClient.Ado.CommitTran();
            }
            catch
            {
                primaryClient.Ado.RollbackTran();
                throw;
            }
        }

        /// <summary>
        /// 【集成测试多 Schema 支持 - 2024/12/21 重构】
        /// 使用独立事务保存策略到多个 Schema/表（同步版本）。
        /// 
        /// 【2024/12/22 修复非原子性行为】
        /// 当使用独立连接时，各组操作应该相互独立。
        /// </summary>
        private void SavePolicyWithSeparateTransactions(IPolicyStore store, List<IGrouping<ISqlSugarClient, CasbinRule>> rulesByClient)
        {
            var exceptions = new List<Exception>();
            
            foreach (var group in rulesByClient)
            {
                var client = group.Key;
                var firstRule = group.FirstOrDefault();
                if (firstRule == null) continue;
                
                var tableName = _clientProvider.GetTableNameForPolicyType(firstRule.PType);
                
                try
                {
                    client.Ado.BeginTran();
                    
                    if (!string.IsNullOrEmpty(tableName))
                    {
                        client.Deleteable<CasbinRule>().AS(tableName).ExecuteCommand();
                    }
                    else
                    {
                        client.Deleteable<CasbinRule>().ExecuteCommand();
                    }
                    
                    var rulesToSave = OnSavePolicy(store, group).ToList();
                    if (rulesToSave.Any())
                    {
                        if (!string.IsNullOrEmpty(tableName))
                        {
                            client.Insertable(rulesToSave).AS(tableName).ExecuteCommand();
                        }
                        else
                        {
                            client.Insertable(rulesToSave).ExecuteCommand();
                        }
                    }
                    
                    client.Ado.CommitTran();
                }
                catch (Exception ex)
                {
                    client.Ado.RollbackTran();
                    exceptions.Add(ex);
                }
            }
            
            if (exceptions.Count > 0)
            {
                throw new AggregateException("One or more save operations failed", exceptions);
            }
        }

        /// <summary>
        /// 【集成测试多 Schema 支持 - 2024/12/21 重构】
        /// 使用独立事务保存策略到多个 Schema/表（异步版本）。
        /// 
        /// 【2024/12/22 修复非原子性行为】
        /// 当使用独立连接时，各组操作应该相互独立。
        /// 即使某个组失败，其他组应该继续执行并提交。
        /// 所有异常都会被收集并在最后以 AggregateException 抛出。
        /// </summary>
        private async Task SavePolicyWithSeparateTransactionsAsync(IPolicyStore store, List<IGrouping<ISqlSugarClient, CasbinRule>> rulesByClient)
        {
            // 收集所有操作过程中的异常
            var exceptions = new List<Exception>();
            
            // 【多 Schema 支持】遍历每个策略组，为每个 client 执行独立事务
            foreach (var group in rulesByClient)
            {
                var client = group.Key;
                var firstRule = group.FirstOrDefault();
                if (firstRule == null) continue;
                
                // 获取此策略类型对应的完全限定表名
                var tableName = _clientProvider.GetTableNameForPolicyType(firstRule.PType);
                
                try
                {
                    client.Ado.BeginTran();
                    
                    // 使用 .AS() 方法指定目标表
                    if (!string.IsNullOrEmpty(tableName))
                    {
                        await client.Deleteable<CasbinRule>().AS(tableName).ExecuteCommandAsync();
                    }
                    else
                    {
                        await client.Deleteable<CasbinRule>().ExecuteCommandAsync();
                    }
                    
                    var rulesToSave = OnSavePolicy(store, group).ToList();
                    if (rulesToSave.Any())
                    {
                        if (!string.IsNullOrEmpty(tableName))
                        {
                            await client.Insertable(rulesToSave).AS(tableName).ExecuteCommandAsync();
                        }
                        else
                        {
                            await client.Insertable(rulesToSave).ExecuteCommandAsync();
                        }
                    }
                    
                    client.Ado.CommitTran();
                }
                catch (Exception ex)
                {
                    client.Ado.RollbackTran();
                    // 收集异常但继续处理其他组，实现非原子性行为
                    exceptions.Add(ex);
                }
            }
            
            // 如果有任何异常发生，抛出聚合异常
            if (exceptions.Count > 0)
            {
                throw new AggregateException("One or more save operations failed", exceptions);
            }
        }
        
        #endregion
    }
}
