using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Casbin.Model;
using Casbin.Persist;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Casbin.Adapter.SqlSugar.Entities;
using Casbin.Adapter.SqlSugar.Extensions;


namespace Casbin.Adapter.SqlSugar
{
    /// <summary>
    /// SqlSugar ORM 的 Casbin 适配器实现
    /// 支持单数据库和多租户/多数据库场景
    /// 【2024/12/21】添加 ISingleAdapter 接口以支持 AutoSave 功能（per-policy 持久化）
    /// </summary>
    public partial class SqlSugarAdapter : IAdapter, IBatchAdapter, IFilteredAdapter, ISingleAdapter
    {
        private readonly ISqlSugarClientProvider _clientProvider;
        private readonly bool _autoCodeFirst;
        protected ISqlSugarClient DbClient { get; }

        #region 构造函数

        /// <summary>
        /// 构造函数 - 使用单个 SqlSugar 客户端 (向后兼容)
        /// </summary>
        public SqlSugarAdapter(ISqlSugarClient db, bool autoCodeFirst = true)
            : this(new DefaultSqlSugarClientProvider(db), autoCodeFirst)
        {
            DbClient = db;
        }

        /// <summary>
        /// 构造函数 - 使用客户端提供程序 (支持多租户)
        /// </summary>
        public SqlSugarAdapter(ISqlSugarClientProvider clientProvider, bool autoCodeFirst = true)
        {
            _clientProvider = clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));
            _autoCodeFirst = autoCodeFirst;

            if (_autoCodeFirst)
            {
                foreach (var client in _clientProvider.GetAllClients().Distinct())
                {
                    client.CodeFirst.InitTables<CasbinRule>();
                    // EnsureUniqueIndex(client); // 新增：强制创建唯一索引（Strict）
                }
            }

            var clients = _clientProvider.GetAllClients().Distinct().ToList();
            DbClient = clients.Count == 1 ? clients[0] : null;
        }

        /// <summary>
        /// 构造函数 - 从 IServiceProvider 解析客户端 (用于依赖注入场景)
        /// </summary>
        public SqlSugarAdapter(IServiceProvider serviceProvider, bool autoCodeFirst = true)
        {
            if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));

            var client = serviceProvider.GetService<ISqlSugarClient>();
            if (client != null)
            {
                _clientProvider = new DefaultSqlSugarClientProvider(client);
                DbClient = client;
            }
            else
            {
                var provider = serviceProvider.GetService<ISqlSugarClientProvider>();
                if (provider == null)
                {
                    throw new InvalidOperationException(
                        "Unable to resolve ISqlSugarClient or ISqlSugarClientProvider from IServiceProvider.");
                }
                _clientProvider = provider;
            }

            _autoCodeFirst = autoCodeFirst;
            if (_autoCodeFirst)
            {
                foreach (var cli in _clientProvider.GetAllClients().Distinct())
                {
                    cli.CodeFirst.InitTables<CasbinRule>();
                    // EnsureUniqueIndex(client); // 新增：强制创建唯一索引（Strict）
                }
            }
        }

        #endregion

        #region IAdapter Load/Save

        public virtual void LoadPolicy(IPolicyStore store)
        {
            var allRules = new List<CasbinRule>();
            
            // 【集成测试多 Schema 支持】需要为每个策略类型确定正确的表名
            var policyTypes = new[] { "p", "p2", "p3", "g", "g2", "g3", "g4" };
            var queriedTables = new HashSet<string>();
            
            foreach (var policyType in policyTypes)
            {
                var client = _clientProvider.GetClientForPolicyType(policyType);
                var tableName = _clientProvider.GetTableNameForPolicyType(policyType);
                
                var tableKey = tableName ?? $"default_{client.GetHashCode()}";
                if (queriedTables.Contains(tableKey))
                    continue;
                queriedTables.Add(tableKey);
                
                try
                {
                    List<CasbinRule> rules;
                    if (!string.IsNullOrEmpty(tableName))
                    {
                        rules = client.Queryable<CasbinRule>().AS(tableName).ToList();
                    }
                    else
                    {
                        rules = client.Queryable<CasbinRule>().ToList();
                    }
                    allRules.AddRange(rules);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LoadPolicy] Error querying table {tableName ?? "default"}: {ex.Message}");
                }
            }

            var filteredRules = OnLoadPolicy(store, allRules);
            LoadPolicyData(store, CasbinRuleExtension.LoadPolicyLine, filteredRules.ToList());
            IsFiltered = false;
        }


        public virtual async Task LoadPolicyAsync(IPolicyStore store)
        {
            var allRules = new List<CasbinRule>();
            
            // 【集成测试多 Schema 支持】需要为每个策略类型确定正确的表名
            // 遍历所有策略类型并使用正确的表名进行查询
            var policyTypes = new[] { "p", "p2", "p3", "g", "g2", "g3", "g4" };
            var queriedTables = new HashSet<string>();
            
            foreach (var policyType in policyTypes)
            {
                var client = _clientProvider.GetClientForPolicyType(policyType);
                var tableName = _clientProvider.GetTableNameForPolicyType(policyType);
                
                // 避免重复查询同一个表
                var tableKey = tableName ?? $"default_{client.GetHashCode()}";
                if (queriedTables.Contains(tableKey))
                    continue;
                queriedTables.Add(tableKey);
                
                try
                {
                    List<CasbinRule> rules;
                    if (!string.IsNullOrEmpty(tableName))
                    {
                        // 使用 .AS() 指定多 Schema 场景下的表名
                        rules = await client.Queryable<CasbinRule>().AS(tableName).ToListAsync();
                    }
                    else
                    {
                        rules = await client.Queryable<CasbinRule>().ToListAsync();
                    }
                    allRules.AddRange(rules);
                }
                catch (Exception ex)
                {
                    // 表可能不存在（首次运行时），忽略错误继续
                    Console.WriteLine($"[LoadPolicyAsync] Error querying table {tableName ?? "default"}: {ex.Message}");
                }
            }

            var filteredRules = OnLoadPolicy(store, allRules);
            LoadPolicyData(store, CasbinRuleExtension.LoadPolicyLine, filteredRules.ToList());
            IsFiltered = false;
        }


        public virtual void SavePolicy(IPolicyStore store)
        {
            var allRules = GetAllRulesFromStore(store);
            if (allRules.Count == 0) return;

            var rulesByClient = allRules.GroupBy(r => _clientProvider.GetClientForPolicyType(r.PType)).ToList();

            if (_clientProvider.SharesConnection)
            {
                SavePolicyWithSharedTransaction(store, rulesByClient);
            }
            else
            {
                SavePolicyWithSeparateTransactions(store, rulesByClient);
            }
        }

        public virtual async Task SavePolicyAsync(IPolicyStore store)
        {
            var allRules = GetAllRulesFromStore(store);
            
            // 临时调试输出
            Console.WriteLine($"[DEBUG] SavePolicyAsync: GetAllRulesFromStore returned {allRules.Count} rules");
            foreach (var rule in allRules.Take(5))
            {
                Console.WriteLine($"[DEBUG]   Rule: PType={rule.PType}, V0={rule.V0}, V1={rule.V1}, V2={rule.V2}");
            }
            
            if (allRules.Count == 0) return;

            var rulesByClient = allRules.GroupBy(r => _clientProvider.GetClientForPolicyType(r.PType)).ToList();
            Console.WriteLine($"[DEBUG] SharesConnection={_clientProvider.SharesConnection}, Groups={rulesByClient.Count}");

            if (_clientProvider.SharesConnection)
            {
                await SavePolicyWithSharedTransactionAsync(store, rulesByClient);
            }
            else
            {
                await SavePolicyWithSeparateTransactionsAsync(store, rulesByClient);
            }
            Console.WriteLine("[DEBUG] SavePolicyAsync completed");
        }

        private List<CasbinRule> GetAllRulesFromStore(IPolicyStore store)
        {
            var allRules = new List<CasbinRule>();

            // 使用 Scan API 遍历所有策略类型
            var types = store.GetPolicyTypesAllSections();
            foreach (var section in types)
            {
                foreach (var type in section.Value)
                {
                    var scanner = store.Scan(section.Key, type);
                    while (scanner.GetNext(out var values))
                    {
                        allRules.Add(CasbinRuleExtension.ToCasbinRule(type, values));
                    }
                }
            }

            return allRules;
        }

        #endregion

        #region IAdapter Add/Remove

        public virtual void AddPolicy(string section, string policyType, IPolicyValues values)
        {
            if (values.Count is 0) return;

            var client = GetClientForPolicyType(policyType);
            // 【2024/12/21 多 Schema 支持】获取目标表名用于 PolicyExists 查询
            var tableName = _clientProvider.GetTableNameForPolicyType(policyType);

            if (PolicyExists(client, policyType, values, tableName))
                return;

            try
            {
                InternalAddPolicy(section, policyType, values);
            }
            catch (Exception ex) when (IsUniqueConstraintViolation(ex))
            {
                // 并发下另一方已插入：等价于已存在，保持幂等
                return;
            }
        }

        public virtual async Task AddPolicyAsync(string section, string policyType, IPolicyValues values)
        {
            Console.WriteLine($"[DEBUG AddPolicyAsync ENTRY] section={section}, policyType={policyType}, values.Count={values.Count}");
            if (values.Count is 0) return;

            var client = GetClientForPolicyType(policyType);
            // 【2024/12/21 多 Schema 支持】获取目标表名用于 PolicyExistsAsync 查询
            var tableName = _clientProvider.GetTableNameForPolicyType(policyType);
            Console.WriteLine($"[DEBUG AddPolicyAsync] tableName={tableName ?? "null"}");

            if (await PolicyExistsAsync(client, policyType, values, tableName).ConfigureAwait(false))
            {
                Console.WriteLine($"[DEBUG AddPolicyAsync] Policy already exists, skipping");
                return;
            }

            try
            {
                await InternalAddPolicyAsync(section, policyType, values).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsUniqueConstraintViolation(ex))
            {
                return;
            }
        }

        private static string NormalizeValue(string value)
            => string.IsNullOrWhiteSpace(value) ? null : value;

        /// <summary>
        /// 【2024/12/21 多 Schema 支持】
        /// 检查策略是否存在，支持通过 tableName 指定多 Schema 场景下的目标表
        /// </summary>
        private bool PolicyExists(ISqlSugarClient client, string policyType, IPolicyValues values, string? tableName)
        {
            // 严谨匹配：PType + V0..V5 完全一致
            // 归一化：空白/空串视为 null（与 LoadPolicyLine 的 IsNullOrEmpty 语义对齐）
            string v0 = values.Count > 0 ? NormalizeValue(values[0]) : null;
            string v1 = values.Count > 1 ? NormalizeValue(values[1]) : null;
            string v2 = values.Count > 2 ? NormalizeValue(values[2]) : null;
            string v3 = values.Count > 3 ? NormalizeValue(values[3]) : null;
            string v4 = values.Count > 4 ? NormalizeValue(values[4]) : null;
            string v5 = values.Count > 5 ? NormalizeValue(values[5]) : null;

            // 【多 Schema 支持】使用 .AS(tableName) 指定查询目标表
            var query = client.Queryable<CasbinRule>();
            if (!string.IsNullOrEmpty(tableName))
            {
                query = query.AS(tableName);
            }
            
            return query.Any(r =>
                r.PType == policyType &&
                r.V0 == v0 &&
                r.V1 == v1 &&
                r.V2 == v2 &&
                r.V3 == v3 &&
                r.V4 == v4 &&
                r.V5 == v5);
        }

        /// <summary>
        /// 【2024/12/21 多 Schema 支持】
        /// 异步检查策略是否存在，支持通过 tableName 指定多 Schema 场景下的目标表
        /// </summary>
        private Task<bool> PolicyExistsAsync(ISqlSugarClient client, string policyType, IPolicyValues values, string? tableName)
        {
            string v0 = values.Count > 0 ? NormalizeValue(values[0]) : null;
            string v1 = values.Count > 1 ? NormalizeValue(values[1]) : null;
            string v2 = values.Count > 2 ? NormalizeValue(values[2]) : null;
            string v3 = values.Count > 3 ? NormalizeValue(values[3]) : null;
            string v4 = values.Count > 4 ? NormalizeValue(values[4]) : null;
            string v5 = values.Count > 5 ? NormalizeValue(values[5]) : null;

            // 【多 Schema 支持】使用 .AS(tableName) 指定查询目标表
            var query = client.Queryable<CasbinRule>();
            if (!string.IsNullOrEmpty(tableName))
            {
                query = query.AS(tableName);
            }
            
            return query.AnyAsync(r =>
                r.PType == policyType &&
                r.V0 == v0 &&
                r.V1 == v1 &&
                r.V2 == v2 &&
                r.V3 == v3 &&
                r.V4 == v4 &&
                r.V5 == v5);
        }

        private static bool IsUniqueConstraintViolation(Exception ex)
        {
            // 保守判断：能确定是唯一冲突才吞，避免吞掉真实错误
            var msg = ex.Message ?? string.Empty;

            // SQLite: "UNIQUE constraint failed: casbin_rule.PType, casbin_rule.V0, ..."
            if (msg.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase))
                return true;

            // MySQL / MariaDB 常见："Duplicate entry"
            if (msg.Contains("Duplicate", StringComparison.OrdinalIgnoreCase) &&
                msg.Contains("entry", StringComparison.OrdinalIgnoreCase))
                return true;

            // PostgreSQL/SQL Server/其他：通用兜底关键词（仍较保守）
            if (msg.Contains("unique", StringComparison.OrdinalIgnoreCase) &&
                msg.Contains("constraint", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }


        public virtual void RemovePolicy(string section, string policyType, IPolicyValues values)
        {
            if (values.Count is 0) return;
            InternalRemovePolicy(section, policyType, values);
        }

        public virtual async Task RemovePolicyAsync(string section, string policyType, IPolicyValues values)
        {
            if (values.Count is 0) return;
            await InternalRemovePolicyAsync(section, policyType, values);
        }

        #endregion

        #region IBatchAdapter

        public virtual void AddPolicies(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            if (valuesList.Count is 0) return;
            InternalAddPolicies(section, policyType, valuesList);
        }

        public virtual async Task AddPoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            if (valuesList.Count is 0) return;
            await InternalAddPoliciesAsync(section, policyType, valuesList);
        }

        public virtual void RemovePolicies(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            if (valuesList.Count is 0) return;
            InternalRemovePolicies(section, policyType, valuesList);
        }

        public virtual async Task RemovePoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            if (valuesList.Count is 0) return;
            await InternalRemovePoliciesAsync(section, policyType, valuesList);
        }

        public virtual void RemoveFilteredPolicy(string section, string policyType, int fieldIndex, IPolicyValues fieldValues)
        {
            if (fieldValues.Count is 0) return;
            InternalRemoveFilteredPolicy(section, policyType, fieldIndex, fieldValues);
        }

        public virtual async Task RemoveFilteredPolicyAsync(string section, string policyType, int fieldIndex, IPolicyValues fieldValues)
        {
            if (fieldValues.Count is 0) return;
            await InternalRemoveFilteredPolicyAsync(section, policyType, fieldIndex, fieldValues);
        }

        public virtual void UpdatePolicy(string section, string policyType, IPolicyValues oldValues, IPolicyValues newValues)
        {
            if (newValues.Count is 0) return;
            InternalUpdatePolicy(section, policyType, oldValues, newValues);
        }

        public virtual async Task UpdatePolicyAsync(string section, string policyType, IPolicyValues oldValues, IPolicyValues newValues)
        {
            if (newValues.Count is 0) return;
            await InternalUpdatePolicyAsync(section, policyType, oldValues, newValues);
        }

        public virtual void UpdatePolicies(string section, string policyType, IReadOnlyList<IPolicyValues> oldValuesList, IReadOnlyList<IPolicyValues> newValuesList)
        {
            if (newValuesList.Count is 0) return;
            InternalUpdatePolicies(section, policyType, oldValuesList, newValuesList);
        }

        public virtual async Task UpdatePoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> oldValuesList, IReadOnlyList<IPolicyValues> newValuesList)
        {
            if (newValuesList.Count is 0) return;
            await InternalUpdatePoliciesAsync(section, policyType, oldValuesList, newValuesList);
        }

        #endregion

        #region IFilteredAdapter

        public bool IsFiltered { get; private set; }

        public virtual void LoadFilteredPolicy(IPolicyStore store, IPolicyFilter filter)
        {
            var allRules = new List<CasbinRule>();
            foreach (var client in _clientProvider.GetAllClients().Distinct())
            {
                allRules.AddRange(client.Queryable<CasbinRule>().ToList());
            }

            IEnumerable<CasbinRule> filteredRules = allRules;
            if (filter != null)
            {
                // 显式指定泛型参数，避免 CS0411
                filteredRules = filter.Apply<CasbinRule>(allRules.AsQueryable()).ToList();
            }

            var processedRules = OnLoadPolicy(store, filteredRules);
            LoadPolicyData(store, CasbinRuleExtension.LoadPolicyLine, processedRules.ToList());
            IsFiltered = filter != null;
        }

        public virtual async Task LoadFilteredPolicyAsync(IPolicyStore store, IPolicyFilter filter)
        {
            var allRules = new List<CasbinRule>();
            foreach (var client in _clientProvider.GetAllClients().Distinct())
            {
                allRules.AddRange(await client.Queryable<CasbinRule>().ToListAsync());
            }

            IEnumerable<CasbinRule> filteredRules = allRules;
            if (filter != null)
            {
                // 这里也是内存过滤，因此不用 ToListAsync，避免 CS1061
                filteredRules = filter.Apply<CasbinRule>(allRules.AsQueryable()).ToList();
            }

            var processedRules = OnLoadPolicy(store, filteredRules);
            LoadPolicyData(store, CasbinRuleExtension.LoadPolicyLine, processedRules.ToList());
            IsFiltered = filter != null;
        }


        #endregion

        #region 虚方法扩展点

        protected virtual IEnumerable<CasbinRule> OnLoadPolicy(IPolicyStore store, IEnumerable<CasbinRule> policies)
        {
            return policies;
        }

        protected virtual IEnumerable<CasbinRule> OnSavePolicy(IPolicyStore store, IEnumerable<CasbinRule> policies)
        {
            return policies;
        }

        #endregion

        #region 辅助方法

        protected ISqlSugarClient GetClientForPolicyType(string policyType)
        {
            return _clientProvider.GetClientForPolicyType(policyType);
        }

        private void LoadPolicyData(IPolicyStore model, CasbinRuleExtension.LoadPolicyLineHandler<CasbinRule, IPolicyStore> handler, List<CasbinRule> rules)
        {
            foreach (var rule in rules)
            {
                handler(rule, model);
            }
        }

        #endregion
    }
}
