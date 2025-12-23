using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System;
using SqlSugar;
using Casbin.Adapter.SqlSugar.Extensions;

namespace Casbin.Adapter.SqlSugar.UnitTest.Fixtures
{
    /// <summary>
    /// 测试主机 Fixture，用于依赖注入测试
    /// </summary>
    public class TestHostFixture
    {
        public TestHostFixture()
        {
            // 使用唯一的数据库名称以允许并行测试执行
            var uniqueDbName = $"CasbinHostTest_{Guid.NewGuid():N}.db";

            Services = new ServiceCollection()
                .AddSqlSugarCasbinAdapter(config =>
                {
                    config.ConnectionString = $"Data Source={uniqueDbName}";
                    config.DbType = DbType.Sqlite;
                    config.IsAutoCloseConnection = true;
                })
                .BuildServiceProvider();
                
            Server = new TestServer(Services);
        }

        public TestServer Server { get; set; }

        public IServiceProvider Services { get; set; }
    }
}