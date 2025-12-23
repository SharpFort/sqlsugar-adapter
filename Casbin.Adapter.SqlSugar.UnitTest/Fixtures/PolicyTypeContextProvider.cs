using System;
using System.Collections.Generic;
using SqlSugar;
using Casbin.Adapter.SqlSugar;

#nullable enable

namespace Casbin.Adapter.SqlSugar.UnitTest.Fixtures
{
    /// <summary>
    /// 测试用的客户端提供器，将策略类型 (p, p2, 等) 路由到一个客户端，
    /// 将分组类型 (g, g2, 等) 路由到另一个客户端。
    /// </summary>
    public class TestPolicyTypeClientProvider : ISqlSugarClientProvider
    {
        private readonly ISqlSugarClient _policyClient;
        private readonly ISqlSugarClient _groupingClient;

        public TestPolicyTypeClientProvider(
            ISqlSugarClient policyClient,
            ISqlSugarClient groupingClient)
        {
            _policyClient = policyClient ?? throw new ArgumentNullException(nameof(policyClient));
            _groupingClient = groupingClient ?? throw new ArgumentNullException(nameof(groupingClient));
        }

        public ISqlSugarClient GetClientForPolicyType(string policyType)
        {
            if (string.IsNullOrEmpty(policyType))
            {
                return _policyClient;
            }

            // 将 'p' 类型 (p, p2, p3, 等) 路由到策略客户端
            // 将 'g' 类型 (g, g2, g3, 等) 路由到分组客户端
            return policyType.StartsWith("p", StringComparison.OrdinalIgnoreCase)
                ? _policyClient
                : _groupingClient;
        }

        public IEnumerable<ISqlSugarClient> GetAllClients()
        {
            return new ISqlSugarClient[] { _policyClient, _groupingClient };
        }

        public bool SharesConnection =>  false; // 测试中使用独立的 SQLite 数据库文件
    }
}
