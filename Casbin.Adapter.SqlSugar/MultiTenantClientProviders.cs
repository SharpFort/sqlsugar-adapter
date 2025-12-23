using System;
using System.Collections.Generic;
using System.Linq;
using SqlSugar;

#nullable enable

namespace Casbin.Adapter.SqlSugar
{
    /// <summary>
    /// 多策略类型客户端提供程序，支持将不同策略类型路由到不同的 ISqlSugarClient 实例。
    /// 适用于需要将策略和分组存储在不同数据库/Schema 的场景。
    /// </summary>
    /// <example>
    /// <code>
    /// // 创建不同的客户端用于策略和分组
    /// var policyClient = new SqlSugarClient(policyConfig);
    /// var groupingClient = new SqlSugarClient(groupingConfig);
    /// 
    /// // 创建提供程序
    /// var provider = new PolicyTypeClientProvider(policyClient, groupingClient);
    /// 
    /// // 使用提供程序创建适配器
    /// var adapter = new SqlSugarAdapter(provider);
    /// 
    /// // 策略操作 (p, p2, p3...) 将路由到 policyClient
    /// // 分组操作 (g, g2, g3...) 将路由到 groupingClient
    /// </code>
    /// </example>
    public class PolicyTypeClientProvider : ISqlSugarClientProvider
    {
        private readonly ISqlSugarClient _policyClient;
        private readonly ISqlSugarClient _groupingClient;
        private readonly bool _sharesConnection;

        /// <summary>
        /// 构造函数 - 使用单独的客户端处理策略和分组
        /// </summary>
        /// <param name="policyClient">处理策略规则 (p, p2, p3...) 的客户端</param>
        /// <param name="groupingClient">处理分组规则 (g, g2, g3...) 的客户端</param>
        /// <param name="sharesConnection">是否共享连接 (影响事务行为)</param>
        public PolicyTypeClientProvider(
            ISqlSugarClient policyClient, 
            ISqlSugarClient groupingClient,
            bool sharesConnection = false)
        {
            _policyClient = policyClient ?? throw new ArgumentNullException(nameof(policyClient));
            _groupingClient = groupingClient ?? throw new ArgumentNullException(nameof(groupingClient));
            _sharesConnection = sharesConnection;
        }

        /// <inheritdoc/>
        public ISqlSugarClient GetClientForPolicyType(string policyType)
        {
            // g, g2, g3... 使用分组客户端
            if (policyType.StartsWith("g", StringComparison.OrdinalIgnoreCase))
            {
                return _groupingClient;
            }
            // p, p2, p3... 使用策略客户端
            return _policyClient;
        }

        /// <inheritdoc/>
        public IEnumerable<ISqlSugarClient> GetAllClients()
        {
            // 如果两个客户端是同一个实例，只返回一个
            if (ReferenceEquals(_policyClient, _groupingClient))
            {
                return new[] { _policyClient };
            }
            return new[] { _policyClient, _groupingClient };
        }

        /// <inheritdoc/>
        public bool SharesConnection => _sharesConnection;
    }

    /// <summary>
    /// 自定义映射客户端提供程序，支持将任意策略类型映射到自定义客户端。
    /// 适用于复杂的多租户或多数据库场景。
    /// </summary>
    /// <example>
    /// <code>
    /// var mappings = new Dictionary&lt;string, ISqlSugarClient&gt;
    /// {
    ///     { "p", tenantAClient },
    ///     { "p2", tenantBClient },
    ///     { "g", centralClient },
    ///     { "g2", tenantAClient }
    /// };
    /// 
    /// var provider = new CustomMappingClientProvider(mappings, defaultClient);
    /// </code>
    /// </example>
    public class CustomMappingClientProvider : ISqlSugarClientProvider
    {
        private readonly Dictionary<string, ISqlSugarClient> _mappings;
        private readonly ISqlSugarClient _defaultClient;
        private readonly bool _sharesConnection;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="mappings">策略类型到客户端的映射</param>
        /// <param name="defaultClient">未定义映射时使用的默认客户端</param>
        /// <param name="sharesConnection">是否共享连接</param>
        public CustomMappingClientProvider(
            Dictionary<string, ISqlSugarClient> mappings,
            ISqlSugarClient defaultClient,
            bool sharesConnection = false)
        {
            _mappings = mappings ?? throw new ArgumentNullException(nameof(mappings));
            _defaultClient = defaultClient ?? throw new ArgumentNullException(nameof(defaultClient));
            _sharesConnection = sharesConnection;
        }

        /// <inheritdoc/>
        public ISqlSugarClient GetClientForPolicyType(string policyType)
        {
            return _mappings.TryGetValue(policyType, out var client) ? client : _defaultClient;
        }

        /// <inheritdoc/>
        public IEnumerable<ISqlSugarClient> GetAllClients()
        {
            var allClients = _mappings.Values.ToHashSet();
            allClients.Add(_defaultClient);
            return allClients;
        }

        /// <inheritdoc/>
        public bool SharesConnection => _sharesConnection;
    }
}
