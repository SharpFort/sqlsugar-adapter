using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Casbin;
using Casbin.Model;
using Casbin.Adapter.SqlSugar.Entities;
using SqlSugar;
using Xunit;

namespace Casbin.Adapter.SqlSugar.UnitTest
{
    /// <summary>
    /// SQLite 多 Client 场景测试
    /// 验证 SQLite 下使用多个 SqlSugarClient 实例时的行为
    /// </summary>
    public class SqliteMultiClientTest
    {
        private const string ModelText = @"
[request_definition]
r = sub, obj, act

[policy_definition]
p = sub, obj, act

[role_definition]
g = _, _

[policy_effect]
e = some(where (p.eft == allow))

[matchers]
m = g(r.sub, p.sub) && r.obj == p.obj && r.act == p.act
";
        
        /// <summary>
        /// 创建一个用于多 Schema 场景的 Provider，模拟多个 Client 实例
        /// </summary>
        private class MultiClientProvider : ISqlSugarClientProvider
        {
            private readonly ISqlSugarClient _client1;
            private readonly ISqlSugarClient _client2;

            public MultiClientProvider(ISqlSugarClient client1, ISqlSugarClient client2)
            {
                _client1 = client1;
                _client2 = client2;
            }

            public bool SharesConnection => false;

            public ISqlSugarClient GetClientForPolicyType(string policyType)
            {
                // 将 p 策略路由到 client1，g 策略路由到 client2
                return policyType.StartsWith("g") ? _client2 : _client1;
            }

            public IEnumerable<ISqlSugarClient> GetAllClients()
            {
                return new[] { _client1, _client2 };
            }

            public System.Data.Common.DbConnection? GetSharedConnection() => null;

            public string? GetTableNameForPolicyType(string policyType) => null;
        }

        /// <summary>
        /// 创建一个单 Client Provider
        /// </summary>
        private class SingleClientProvider : ISqlSugarClientProvider
        {
            private readonly ISqlSugarClient _client;

            public SingleClientProvider(ISqlSugarClient client)
            {
                _client = client;
            }

            public bool SharesConnection => true;

            public ISqlSugarClient GetClientForPolicyType(string policyType) => _client;

            public IEnumerable<ISqlSugarClient> GetAllClients() => new[] { _client };

            public System.Data.Common.DbConnection? GetSharedConnection() => null;

            public string? GetTableNameForPolicyType(string policyType) => null;
        }

        /// <summary>
        /// 辅助方法：安全删除测试数据库文件
        /// </summary>
        private static void SafeDeleteFile(string path)
        {
            try
            {
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
            catch
            {
                // 忽略删除失败（例如文件仍被占用），测试目录会定期清理
            }
        }

        [Fact]
        public void SavePolicy_WithMultipleClients_ShouldThrowForSqlite()
        {
            // Arrange
            var dbPath1 = $"MultiClientTest1_{Guid.NewGuid()}.db";
            var dbPath2 = $"MultiClientTest2_{Guid.NewGuid()}.db";
            SqlSugarClient? client1 = null;
            SqlSugarClient? client2 = null;
            
            try
            {
                // 创建两个 SQLite Client（指向不同的数据库，模拟多 Client 场景）
                client1 = new SqlSugarClient(new ConnectionConfig
                {
                    ConnectionString = $"DataSource={dbPath1}",
                    DbType = DbType.Sqlite,
                    IsAutoCloseConnection = false,
                    InitKeyType = InitKeyType.Attribute
                });

                client2 = new SqlSugarClient(new ConnectionConfig
                {
                    ConnectionString = $"DataSource={dbPath2}",
                    DbType = DbType.Sqlite,
                    IsAutoCloseConnection = false,
                    InitKeyType = InitKeyType.Attribute
                });

                // 初始化表结构
                client1.CodeFirst.InitTables<CasbinRule>();
                client2.CodeFirst.InitTables<CasbinRule>();

                // 创建使用多 Client 的 Provider
                var provider = new MultiClientProvider(client1, client2);
                var adapter = new SqlSugarAdapter(provider);

                var model = DefaultModel.CreateFromText(ModelText);
                var enforcer = new Enforcer(model, adapter);

                // 添加不同类型的策略（会路由到不同的 Client）
                enforcer.AddPolicy("alice", "data1", "read");
                enforcer.AddGroupingPolicy("alice", "admin");

                // Act & Assert
                // 在 SQLite 下，使用多个 Client 实例应该抛出 InvalidOperationException
                var exception = Assert.Throws<InvalidOperationException>(() => enforcer.SavePolicy());
                
                // 验证错误信息
                Assert.Contains("SQLite does not support SavePolicy across multiple SqlSugarClient instances", exception.Message);
            }
            finally
            {
                // 先释放客户端连接，再删除文件
                client1?.Dispose();
                client2?.Dispose();
                SafeDeleteFile(dbPath1);
                SafeDeleteFile(dbPath2);
            }
        }

        [Fact]
        public async Task SavePolicyAsync_WithMultipleClients_ShouldThrowForSqlite()
        {
            // Arrange
            var dbPath1 = $"MultiClientTestAsync1_{Guid.NewGuid()}.db";
            var dbPath2 = $"MultiClientTestAsync2_{Guid.NewGuid()}.db";
            SqlSugarClient? client1 = null;
            SqlSugarClient? client2 = null;
            
            try
            {
                client1 = new SqlSugarClient(new ConnectionConfig
                {
                    ConnectionString = $"DataSource={dbPath1}",
                    DbType = DbType.Sqlite,
                    IsAutoCloseConnection = false,
                    InitKeyType = InitKeyType.Attribute
                });

                client2 = new SqlSugarClient(new ConnectionConfig
                {
                    ConnectionString = $"DataSource={dbPath2}",
                    DbType = DbType.Sqlite,
                    IsAutoCloseConnection = false,
                    InitKeyType = InitKeyType.Attribute
                });

                client1.CodeFirst.InitTables<CasbinRule>();
                client2.CodeFirst.InitTables<CasbinRule>();

                var provider = new MultiClientProvider(client1, client2);
                var adapter = new SqlSugarAdapter(provider);

                var model = DefaultModel.CreateFromText(ModelText);
                var enforcer = new Enforcer(model, adapter);

                await enforcer.AddPolicyAsync("alice", "data1", "read");
                await enforcer.AddGroupingPolicyAsync("alice", "admin");

                // Act & Assert
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => enforcer.SavePolicyAsync());
                
                Assert.Contains("SQLite does not support SavePolicy across multiple SqlSugarClient instances", exception.Message);
            }
            finally
            {
                client1?.Dispose();
                client2?.Dispose();
                SafeDeleteFile(dbPath1);
                SafeDeleteFile(dbPath2);
            }
        }

        [Fact]
        public void SavePolicy_WithSingleClient_ShouldSucceedForSqlite()
        {
            // Arrange
            var dbPath = $"SingleClientTest_{Guid.NewGuid()}.db";
            SqlSugarClient? client = null;
            
            try
            {
                client = new SqlSugarClient(new ConnectionConfig
                {
                    ConnectionString = $"DataSource={dbPath}",
                    DbType = DbType.Sqlite,
                    IsAutoCloseConnection = false,
                    InitKeyType = InitKeyType.Attribute
                });

                client.CodeFirst.InitTables<CasbinRule>();

                var provider = new SingleClientProvider(client);
                var adapter = new SqlSugarAdapter(provider);

                var model = DefaultModel.CreateFromText(ModelText);
                var enforcer = new Enforcer(model, adapter);

                // 添加策略
                enforcer.AddPolicy("alice", "data1", "read");
                enforcer.AddGroupingPolicy("alice", "admin");

                // Act - 单 Client 场景应该成功
                enforcer.SavePolicy();

                // Assert - 验证策略已保存
                var policiesCount = client.Queryable<CasbinRule>().Where(r => r.PType == "p").Count();
                var groupingsCount = client.Queryable<CasbinRule>().Where(r => r.PType == "g").Count();

                Assert.Equal(1, policiesCount);
                Assert.Equal(1, groupingsCount);
            }
            finally
            {
                client?.Dispose();
                SafeDeleteFile(dbPath);
            }
        }

        [Fact]
        public async Task SavePolicyAsync_WithSingleClient_ShouldSucceedForSqlite()
        {
            // Arrange
            var dbPath = $"SingleClientTestAsync_{Guid.NewGuid()}.db";
            SqlSugarClient? client = null;
            
            try
            {
                client = new SqlSugarClient(new ConnectionConfig
                {
                    ConnectionString = $"DataSource={dbPath}",
                    DbType = DbType.Sqlite,
                    IsAutoCloseConnection = false,
                    InitKeyType = InitKeyType.Attribute
                });

                client.CodeFirst.InitTables<CasbinRule>();

                var provider = new SingleClientProvider(client);
                var adapter = new SqlSugarAdapter(provider);

                var model = DefaultModel.CreateFromText(ModelText);
                var enforcer = new Enforcer(model, adapter);

                await enforcer.AddPolicyAsync("bob", "data2", "write");
                await enforcer.AddGroupingPolicyAsync("bob", "user");

                // Act
                await enforcer.SavePolicyAsync();

                // Assert
                var policiesCount = client.Queryable<CasbinRule>().Where(r => r.PType == "p").Count();
                var groupingsCount = client.Queryable<CasbinRule>().Where(r => r.PType == "g").Count();

                Assert.Equal(1, policiesCount);
                Assert.Equal(1, groupingsCount);
            }
            finally
            {
                client?.Dispose();
                SafeDeleteFile(dbPath);
            }
        }

        [Fact]
        public void IsAnyTran_ShouldDetectExternalTransaction()
        {
            // Arrange
            var dbPath = $"IsAnyTranTest_{Guid.NewGuid()}.db";
            SqlSugarClient? client = null;
            
            try
            {
                client = new SqlSugarClient(new ConnectionConfig
                {
                    ConnectionString = $"DataSource={dbPath}",
                    DbType = DbType.Sqlite,
                    IsAutoCloseConnection = false,
                    InitKeyType = InitKeyType.Attribute
                });

                client.CodeFirst.InitTables<CasbinRule>();

                // Act & Assert - 无事务时
                Assert.False(client.Ado.IsAnyTran());

                // 开启事务
                client.Ado.BeginTran();
                
                // Act & Assert - 有事务时
                Assert.True(client.Ado.IsAnyTran());

                client.Ado.RollbackTran();
                
                // Act & Assert - 回滚后
                Assert.False(client.Ado.IsAnyTran());
            }
            finally
            {
                client?.Dispose();
                SafeDeleteFile(dbPath);
            }
        }
    }
}
