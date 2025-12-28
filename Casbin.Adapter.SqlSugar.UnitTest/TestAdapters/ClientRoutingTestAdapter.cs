using System.Collections.Generic;
using Casbin.Model;
using Casbin.Persist;
using SqlSugar;

namespace Casbin.Adapter.SqlSugar.UnitTest.TestAdapters
{
    /// <summary>
    /// 测试适配器,用于跟踪客户端和表名路由调用。
    /// 此适配器验证 SqlSugarAdapter 是否为不同的策略类型正确路由到不同的客户端和表。
    /// </summary>
    public class ClientRoutingTestAdapter : SqlSugarAdapter
    {
        private readonly Dictionary<string, ClientRoutingInfo> _routingTracker;
        private readonly ISqlSugarClientProvider _provider;

        public ClientRoutingTestAdapter(
            ISqlSugarClientProvider clientProvider,
            Dictionary<string, ClientRoutingInfo> routingTracker)
            : base(clientProvider)
        {
            _routingTracker = routingTracker;
            _provider = clientProvider;
        }

        public override void AddPolicy(string section, string policyType, IPolicyValues values)
        {
            // 在调用基类方法之前跟踪路由信息
            TrackRouting(policyType);
            base.AddPolicy(section, policyType, values);
        }

        private void TrackRouting(string policyType)
        {
            if (!_routingTracker.ContainsKey(policyType))
            {
                // 获取此策略类型的客户端和表名
                var client = _provider.GetClientForPolicyType(policyType);
                var tableName = _provider.GetTableNameForPolicyType(policyType);

                _routingTracker[policyType] = new ClientRoutingInfo
                {
                    Client = client,
                    TableName = tableName,
                    CallCount = 1
                };
            }
            else
            {
                _routingTracker[policyType].CallCount++;
            }
        }
    }

    /// <summary>
    /// 记录客户端路由信息
    /// </summary>
    public class ClientRoutingInfo
    {
        /// <summary>
        /// 路由到的 SqlSugar 客户端实例
        /// </summary>
        public ISqlSugarClient Client { get; set; }

        /// <summary>
        /// 使用的表名（如果有）
        /// </summary>
        public string TableName { get; set; }

        /// <summary>
        /// 此策略类型的路由调用次数
        /// </summary>
        public int CallCount { get; set; }
    }
}
