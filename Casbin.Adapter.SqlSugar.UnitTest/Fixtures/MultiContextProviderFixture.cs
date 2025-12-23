using System;
using SqlSugar;

namespace Casbin.Adapter.SqlSugar.UnitTest.Fixtures
{
    /// <summary>
    /// Fixture 用于创建多客户端测试场景，为策略和分组使用独立的客户端
    /// </summary>
    public class MultiContextProviderFixture : IDisposable
    {
        private bool _disposed;

        /// <summary>
        /// 创建一个多客户端提供器，为策略和分组规则使用独立的客户端。
        /// 使用独立的数据库文件以实现适当的隔离。
        /// 此方法避免了 SQLite 跨表事务的限制。
        /// </summary>
        /// <param name="testName">此测试的唯一名称，避免数据库冲突</param>
        /// <returns>配置用于测试的 TestPolicyTypeClientProvider</returns>
        public TestPolicyTypeClientProvider GetMultiContextProvider(string testName)
        {
            // 使用独立的数据库文件以实现适当的隔离
            var policyDbName = $"MultiContext_{testName}_policy.db";
            var groupingDbName = $"MultiContext_{testName}_grouping.db";

            // 为策略创建客户端（独立数据库）
            var policyConfig = new ConnectionConfig
            {
                ConnectionString = $"Data Source={policyDbName}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute
            };
            var policyClient = new SqlSugarClient(policyConfig);
            policyClient.CodeFirst.InitTables<Casbin.Adapter.SqlSugar.Entities.CasbinRule>();

            // 为分组创建客户端（独立数据库）
            var groupingConfig = new ConnectionConfig
            {
                ConnectionString = $"Data Source={groupingDbName}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute
            };
            var groupingClient = new SqlSugarClient(groupingConfig);
            groupingClient.CodeFirst.InitTables<Casbin.Adapter.SqlSugar.Entities.CasbinRule>();

            return new TestPolicyTypeClientProvider(policyClient, groupingClient);
        }

        /// <summary>
        /// 获取独立的客户端用于测试中的直接验证。
        /// 返回指向与 provider 相同数据库的新客户端实例。
        /// </summary>
        public (ISqlSugarClient policyClient, ISqlSugarClient groupingClient) GetSeparateClients(string testName)
        {
            // 使用与 GetMultiContextProvider 相同的数据库文件名
            var policyDbName = $"MultiContext_{testName}_policy.db";
            var groupingDbName = $"MultiContext_{testName}_grouping.db";

            // 创建指向相同数据库文件的新客户端实例
            var policyConfig = new ConnectionConfig
            {
                ConnectionString = $"Data Source={policyDbName}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute
            };
            var policyClient = new SqlSugarClient(policyConfig);
            policyClient.CodeFirst.InitTables<Casbin.Adapter.SqlSugar.Entities.CasbinRule>();

            var groupingConfig = new ConnectionConfig
            {
                ConnectionString = $"Data Source={groupingDbName}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute
            };
            var groupingClient = new SqlSugarClient(groupingConfig);
            groupingClient.CodeFirst.InitTables<Casbin.Adapter.SqlSugar.Entities.CasbinRule>();

            return (policyClient, groupingClient);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // 清理由测试框架处理
                _disposed = true;
            }
        }
    }
}
