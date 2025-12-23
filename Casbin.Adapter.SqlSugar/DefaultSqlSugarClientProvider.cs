using System;
using System.Collections.Generic;
using SqlSugar;

#nullable enable

namespace Casbin.Adapter.SqlSugar
{
    /// <summary>
    /// 默认的单客户端提供程序，使用单个 ISqlSugarClient 处理所有策略类型。
    /// 适用于大多数单数据库场景，保持向后兼容性。
    /// </summary>
    public class DefaultSqlSugarClientProvider : ISqlSugarClientProvider
    {
        private readonly ISqlSugarClient _client;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="client">用于所有策略类型的 SqlSugar 客户端实例</param>
        /// <exception cref="ArgumentNullException">当 client 为 null 时抛出</exception>
        public DefaultSqlSugarClientProvider(ISqlSugarClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <inheritdoc/>
        public ISqlSugarClient GetClientForPolicyType(string policyType)
        {
            return _client;
        }

        /// <inheritdoc/>
        public IEnumerable<ISqlSugarClient> GetAllClients()
        {
            return new[] { _client };
        }

        /// <inheritdoc/>
        /// <remarks>单客户端场景，总是返回 true</remarks>
        public bool SharesConnection => true;
    }
}
