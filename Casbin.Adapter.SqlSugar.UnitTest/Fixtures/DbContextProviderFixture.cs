using SqlSugar;

namespace Casbin.Adapter.SqlSugar.UnitTest.Fixtures
{
    /// <summary>
    /// SqlSugar 客户端提供器 Fixture，用于单元测试
    /// </summary>
    public class SqlSugarClientProviderFixture
    {
        /// <summary>
        /// 获取一个 SQLite 测试数据库的 SqlSugar 客户端
        /// </summary>
        /// <param name="name">数据库文件名称</param>
        /// <returns>配置好的 ISqlSugarClient 实例</returns>
        public ISqlSugarClient GetClient(string name)
        {
            var config = new ConnectionConfig
            {
                ConnectionString = $"Data Source={name}.db",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute
            };

            var client = new SqlSugarClient(config);
            
            // 自动创建表
            client.CodeFirst.InitTables<Casbin.Adapter.SqlSugar.Entities.CasbinRule>();
            
            return client;
        }
    }
}