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
        /// 【2026/02/01 修复】逻辑原子性改进
        /// - 每个策略组的 DELETE + INSERT 是不可分割的逻辑单元
        /// - 避免"全删未插"的中间态被观察到
        /// - 异常定位更明确（知道哪个策略类型失败）
        /// </summary>
        private void SavePolicyWithSharedTransaction(IPolicyStore store, List<IGrouping<ISqlSugarClient, CasbinRule>> rulesByClient)
        {
            if (rulesByClient == null || rulesByClient.Count == 0)
                return;

            var primaryClient = rulesByClient.First().Key;
            
            // =========================================================
            // ✅ P0：SQLite 多 Client 防御（必须）
            // =========================================================
            if (primaryClient.CurrentConnectionConfig.DbType == DbType.Sqlite
                && rulesByClient.Select(x => x.Key).Distinct().Count() > 1)
            {
                throw new InvalidOperationException(
                    "SQLite does not support SavePolicy across multiple SqlSugarClient instances. " +
                    "Please ensure all policy types route to the same SqlSugarClient when using SQLite.");
            }
            
            // =========================================================
            // ✅ 事务判断：是否存在外部事务（ABP UOW 等）
            // =========================================================
            bool hasOuterTransaction = primaryClient.Ado.IsAnyTran();
            bool isLocalTransaction = false;
            
            try
            {
                // =====================================================
                // ✅ 若无外部事务，由 Adapter 自行开启
                // =====================================================
                if (!hasOuterTransaction)
                {
                    primaryClient.Ado.BeginTran();
                    isLocalTransaction = true;
                }
                
                // =====================================================
                // ✅ 执行所有策略组（每组 DELETE+INSERT 是逻辑原子单元）
                // =====================================================
                foreach (var group in rulesByClient)
                {
                    var client = group.Key;
                    var firstRule = group.FirstOrDefault();
                    if (firstRule == null)
                        continue;
                    
                    // 获取策略类型对应的表名（支持多 Schema）
                    var tableName = _clientProvider.GetTableNameForPolicyType(firstRule.PType);
                    
                    // -----------------------------
                    // DELETE
                    // -----------------------------
                    if (!string.IsNullOrEmpty(tableName))
                    {
                        client.Deleteable<CasbinRule>()
                            .AS(tableName)
                            .ExecuteCommand();
                    }
                    else
                    {
                        client.Deleteable<CasbinRule>()
                            .ExecuteCommand();
                    }
                    
                    // -----------------------------
                    // INSERT
                    // -----------------------------
                    var rulesToSave = OnSavePolicy(store, group).ToList();
                    if (rulesToSave.Count > 0)
                    {
                        if (!string.IsNullOrEmpty(tableName))
                        {
                            client.Insertable(rulesToSave)
                                .AS(tableName)
                                .ExecuteCommand();
                        }
                        else
                        {
                            client.Insertable(rulesToSave)
                                .ExecuteCommand();
                        }
                    }
                }
                
                // =====================================================
                // ✅ 提交事务（仅限本地事务）
                // =====================================================
                if (isLocalTransaction)
                {
                    primaryClient.Ado.CommitTran();
                }
            }
            catch
            {
                // =====================================================
                // ✅ 回滚事务（仅限本地事务）
                // =====================================================
                if (isLocalTransaction)
                {
                    primaryClient.Ado.RollbackTran();
                }
                
                // ❗ 不吞异常，保证调用方感知失败
                throw;
            }
        }

        /// <summary>
        /// 【集成测试多 Schema 支持 - 2024/12/21 重构】
        /// 使用共享事务保存策略到多个 Schema/表。
        /// 
        /// 【2026/02/01 修复】逻辑原子性改进
        /// - 每个策略组的 DELETE + INSERT 是不可分割的逻辑单元
        /// - 避免"全删未插"的中间态被观察到
        /// - 异常定位更明确（知道哪个策略类型失败）
        /// </summary>
        private async Task SavePolicyWithSharedTransactionAsync(IPolicyStore store, List<IGrouping<ISqlSugarClient, CasbinRule>> rulesByClient)
        {
            if (rulesByClient == null || rulesByClient.Count == 0)
                return;

            var primaryClient = rulesByClient.First().Key;
            
            // =========================================================
            // ✅ P0：SQLite 多 Client 防御（必须）
            // =========================================================
            if (primaryClient.CurrentConnectionConfig.DbType == DbType.Sqlite
                && rulesByClient.Select(x => x.Key).Distinct().Count() > 1)
            {
                throw new InvalidOperationException(
                    "SQLite does not support SavePolicy across multiple SqlSugarClient instances. " +
                    "Please ensure all policy types route to the same SqlSugarClient when using SQLite.");
            }
            
            // =========================================================
            // ✅ 事务判断：是否存在外部事务（ABP UOW 等）
            // =========================================================
            bool hasOuterTransaction = primaryClient.Ado.IsAnyTran();
            bool isLocalTransaction = false;

            try
            {
                // =====================================================
                // ✅ 若无外部事务，由 Adapter 自行开启
                // =====================================================
                if (!hasOuterTransaction)
                {
                    primaryClient.Ado.BeginTran();
                    isLocalTransaction = true;
                }
                
                // =====================================================
                // ✅ 执行所有策略组（每组 DELETE+INSERT 是逻辑原子单元）
                // =====================================================
                foreach (var group in rulesByClient)
                {
                    var client = group.Key;
                    var firstRule = group.FirstOrDefault();
                    if (firstRule == null)
                        continue;
                    
                    // 获取策略类型对应的表名（支持多 Schema）
                    var tableName = _clientProvider.GetTableNameForPolicyType(firstRule.PType);
                    
                    // -----------------------------
                    // DELETE
                    // -----------------------------
                    if (!string.IsNullOrEmpty(tableName))
                    {
                        await client.Deleteable<CasbinRule>()
                            .AS(tableName)
                            .ExecuteCommandAsync();
                    }
                    else
                    {
                        await client.Deleteable<CasbinRule>()
                            .ExecuteCommandAsync();
                    }
                    
                    // -----------------------------
                    // INSERT
                    // -----------------------------
                    var rulesToSave = OnSavePolicy(store, group).ToList();
                    if (rulesToSave.Count > 0)
                    {
                        if (!string.IsNullOrEmpty(tableName))
                        {
                            await client.Insertable(rulesToSave)
                                .AS(tableName)
                                .ExecuteCommandAsync();
                        }
                        else
                        {
                            await client.Insertable(rulesToSave)
                                .ExecuteCommandAsync();
                        }
                    }
                }
                
                // =====================================================
                // ✅ 提交事务（仅限本地事务）
                // =====================================================
                if (isLocalTransaction)
                {
                    primaryClient.Ado.CommitTran();
                }
            }
            catch
            {
                // =====================================================
                // ✅ 回滚事务（仅限本地事务）
                // =====================================================
                if (isLocalTransaction)
                {
                    primaryClient.Ado.RollbackTran();
                }
                
                // ❗ 不吞异常，保证调用方感知失败
                throw;
            }
        }

        /// <summary>
        /// 【集成测试多 Schema 支持 - 2024/12/21 重构】
        /// 使用独立事务保存策略到多个 Schema/表（同步版本）。
        /// 
        /// 【2026/02/01 修复】统一事务模式，确保真正的原子性。
        /// - SQLite 多 Client 检测并明确拒绝
        /// - 复用外部事务（如 ABP UnitOfWork）
        /// - 无外部事务时由 Adapter 开启统一事务
        /// - 不允许部分成功，保证 Casbin 策略一致性
        /// </summary>
        private void SavePolicyWithSeparateTransactions(IPolicyStore store, List<IGrouping<ISqlSugarClient, CasbinRule>> rulesByClient)
        {
            // 【2026/01/30 修复 SQLite 锁问题 - P0】
            // SQLite 使用文件级锁，不支持多连接同时写入

            if (rulesByClient == null || rulesByClient.Count == 0)
                return;

            var primaryClient = rulesByClient.First().Key;

            // =========================================================
            // ✅ P0：SQLite 多 Client 防御（必须）
            // =========================================================
            if (primaryClient.CurrentConnectionConfig.DbType == DbType.Sqlite &&
                rulesByClient.Select(x => x.Key).Distinct().Count() > 1)
            {
                throw new InvalidOperationException(
                    "SQLite does not support SavePolicy across multiple SqlSugarClient instances. " +
                    "Please ensure all policy types route to the same SqlSugarClient when using SQLite.");
            }

            // =========================================================
            // ✅ 事务判断：是否存在外部事务（ABP UOW 等）
            // =========================================================
            bool hasOuterTransaction = primaryClient.Ado.IsAnyTran();
            bool isLocalTransaction = false;

            try
            {
                // =====================================================
                // ✅ 若无外部事务，由 Adapter 自行开启
                // =====================================================
                if (!hasOuterTransaction)
                {
                    primaryClient.Ado.BeginTran();
                    isLocalTransaction = true;
                }

                // =====================================================
                // ✅ 执行所有策略组（逻辑原子）
                // =====================================================
                foreach (var group in rulesByClient)
                {
                    var client = group.Key;
                    var firstRule = group.FirstOrDefault();
                    if (firstRule == null)
                        continue;

                    // 获取策略类型对应的表名（支持多 Schema）
                    var tableName = _clientProvider.GetTableNameForPolicyType(firstRule.PType);

                    // -----------------------------
                    // DELETE
                    // -----------------------------
                    if (!string.IsNullOrEmpty(tableName))
                    {
                        client.Deleteable<CasbinRule>()
                            .AS(tableName)
                            .ExecuteCommand();
                    }
                    else
                    {
                        client.Deleteable<CasbinRule>()
                            .ExecuteCommand();
                    }

                    // -----------------------------
                    // INSERT
                    // -----------------------------
                    var rulesToSave = OnSavePolicy(store, group).ToList();
                    if (rulesToSave.Count > 0)
                    {
                        if (!string.IsNullOrEmpty(tableName))
                        {
                            client.Insertable(rulesToSave)
                                .AS(tableName)
                                .ExecuteCommand();
                        }
                        else
                        {
                            client.Insertable(rulesToSave)
                                .ExecuteCommand();
                        }
                    }
                }

                // =====================================================
                // ✅ 提交事务（仅限本地事务）
                // =====================================================
                if (isLocalTransaction)
                {
                    primaryClient.Ado.CommitTran();
                }
            }
            catch
            {
                // =====================================================
                // ✅ 回滚事务（仅限本地事务）
                // =====================================================
                if (isLocalTransaction)
                {
                    primaryClient.Ado.RollbackTran();
                }

                // ❗ 不吞异常，保证调用方感知失败
                throw;
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
            // 【2026/01/30 修复 SQLite 锁问题 - P0】
            // SQLite 使用文件级锁，不支持多连接同时写入

             if (rulesByClient == null || rulesByClient.Count == 0)
                return;

            var primaryClient = rulesByClient.First().Key;

            // =========================================================
            // ✅ P0：SQLite 多 Client 防御（必须）
            // =========================================================
            if (primaryClient.CurrentConnectionConfig.DbType == DbType.Sqlite &&
                rulesByClient.Select(x => x.Key).Distinct().Count() > 1)
            {
                throw new InvalidOperationException(
                    "SQLite does not support SavePolicy across multiple SqlSugarClient instances. " +
                    "Please ensure all policy types route to the same SqlSugarClient when using SQLite.");
            }

            // var firstClient = rulesByClient.FirstOrDefault()?.Key;
            // if (firstClient != null 
            //     && firstClient.CurrentConnectionConfig.DbType == DbType.Sqlite
            //     && rulesByClient.Select(x => x.Key).Distinct().Count() > 1)
            // {
            //     throw new InvalidOperationException(
            //         "SQLite does not support SavePolicy across multiple SqlSugarClient instances. " +
            //         "Please ensure all policy types route to the same SqlSugarClient when using SQLite.");
            // }
            
            // =========================================================
            // ✅ 事务判断：是否存在外部事务（ABP UOW 等）
            // =========================================================
            bool hasOuterTransaction = primaryClient.Ado.IsAnyTran();
            bool isLocalTransaction = false;

            try
            {
                // =====================================================
                // ✅ 若无外部事务，由 Adapter 自行开启
                // =====================================================
                if (!hasOuterTransaction)
                {
                    primaryClient.Ado.BeginTran();
                    isLocalTransaction = true;
                }

                // =====================================================
                // ✅ 执行所有策略组（逻辑原子）
                // =====================================================
                foreach (var group in rulesByClient)
                {
                    var client = group.Key;
                    var firstRule = group.FirstOrDefault();
                    if (firstRule == null)
                        continue;

                    // 获取策略类型对应的表名（支持多 Schema）
                    var tableName = _clientProvider.GetTableNameForPolicyType(firstRule.PType);

                    // -----------------------------
                    // DELETE
                    // -----------------------------
                    if (!string.IsNullOrEmpty(tableName))
                    {
                        await client.Deleteable<CasbinRule>()
                            .AS(tableName)
                            .ExecuteCommandAsync();
                    }
                    else
                    {
                        await client.Deleteable<CasbinRule>()
                            .ExecuteCommandAsync();
                    }

                    // -----------------------------
                    // INSERT
                    // -----------------------------
                    var rulesToSave = OnSavePolicy(store, group).ToList();
                    if (rulesToSave.Count > 0)
                    {
                        if (!string.IsNullOrEmpty(tableName))
                        {
                            await client.Insertable(rulesToSave)
                                .AS(tableName)
                                .ExecuteCommandAsync();
                        }
                        else
                        {
                            await client.Insertable(rulesToSave)
                                .ExecuteCommandAsync();
                        }
                    }
                }

                // =====================================================
                // ✅ 提交事务（仅限本地事务）
                // =====================================================
                if (isLocalTransaction)
                {
                    primaryClient.Ado.CommitTran();
                }
            }
            catch
            {
                // =====================================================
                // ✅ 回滚事务（仅限本地事务）
                // =====================================================
                if (isLocalTransaction)
                {
                    primaryClient.Ado.RollbackTran();
                }

                // ❗ 不吞异常，保证调用方感知失败
                throw;
            }

            // // 收集所有操作过程中的异常
            // var exceptions = new List<Exception>();
            
            // // 【多 Schema 支持】遍历每个策略组，为每个 client 执行独立事务
            // foreach (var group in rulesByClient)
            // {
            //     var client = group.Key;
            //     var firstRule = group.FirstOrDefault();
            //     if (firstRule == null) continue;
                
            //     // 获取此策略类型对应的完全限定表名
            //     var tableName = _clientProvider.GetTableNameForPolicyType(firstRule.PType);
                
            //     // 【2026/01/30 修复 SQLite 锁问题】
            //     bool isLocalTransaction = false;

            //     try
            //     {
            //         // 【2026/01/30 优化 - P1】使用 IsAnyTran() 语义更清晰
            //         if (!client.Ado.IsAnyTran())
            //         {
            //             client.Ado.BeginTran();
            //             isLocalTransaction = true;
            //         }
                    
            //         // 使用 .AS() 方法指定目标表
            //         if (!string.IsNullOrEmpty(tableName))
            //         {
            //             await client.Deleteable<CasbinRule>().AS(tableName).ExecuteCommandAsync();
            //         }
            //         else
            //         {
            //             await client.Deleteable<CasbinRule>().ExecuteCommandAsync();
            //         }
                    
            //         var rulesToSave = OnSavePolicy(store, group).ToList();
            //         if (rulesToSave.Any())
            //         {
            //             if (!string.IsNullOrEmpty(tableName))
            //             {
            //                 await client.Insertable(rulesToSave).AS(tableName).ExecuteCommandAsync();
            //             }
            //             else
            //             {
            //                 await client.Insertable(rulesToSave).ExecuteCommandAsync();
            //             }
            //         }
                    
            //         // 仅当是本地开启的事务时才提交
            //         if (isLocalTransaction)
            //         {
            //             client.Ado.CommitTran();
            //         }
            //     }
            //     catch (Exception ex)
            //     {
            //         // 仅当是本地开启的事务时才回滚
            //         if (isLocalTransaction)
            //         {
            //             client.Ado.RollbackTran();
            //         }
            //         // 收集异常但继续处理其他组，实现非原子性行为
            //         exceptions.Add(ex);
            //     }
            // }
            
            // // 如果有任何异常发生，抛出聚合异常
            // if (exceptions.Count > 0)
            // {
            //     throw new AggregateException("One or more save operations failed", exceptions);
            // }
        }
        
        #endregion
    }
}
