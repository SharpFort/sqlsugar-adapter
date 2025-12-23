using System.Linq;
using System.Threading.Tasks;
using Casbin.Model;
using Casbin.Adapter.SqlSugar.Entities;
using Casbin.Adapter.SqlSugar.UnitTest.Extensions;
using Casbin.Adapter.SqlSugar.UnitTest.Fixtures;
using SqlSugar;
using Xunit;
using System.Collections.Generic;
using Casbin.Adapter.SqlSugar;
using System;

namespace Casbin.Adapter.SqlSugar.UnitTest
{
    /// <summary>
    /// 测试以确保与现有单客户端行为的向后兼容性。
    /// 这些测试验证多客户端更改不会破坏现有的使用模式。
    /// </summary>
    public class BackwardCompatibilityTest : TestUtil,
        IClassFixture<ModelProvideFixture>,
        IClassFixture<SqlSugarClientProviderFixture>
    {
        private readonly ModelProvideFixture _modelProvideFixture;
        private readonly SqlSugarClientProviderFixture _clientProviderFixture;

        public BackwardCompatibilityTest(
            ModelProvideFixture modelProvideFixture,
            SqlSugarClientProviderFixture clientProviderFixture)
        {
            _modelProvideFixture = modelProvideFixture;
            _clientProviderFixture = clientProviderFixture;
        }

        [Fact]
        public void TestSingleClientConstructorStillWorks()
        {
            // Arrange - 使用原始构造函数模式
            var client = _clientProviderFixture.GetClient("SingleClientConstructor");
            client.Clear();

            // Act - 使用单客户端构造函数创建适配器（原始 API）
            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(client);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // 添加策略
            enforcer.AddPolicy("alice", "data1", "read");
            enforcer.AddGroupingPolicy("alice", "admin");

            // Assert - 所有策略应该在单个客户端中
            Assert.Equal(2, client.Queryable<CasbinRule>().Count());

            var policies = client.Queryable<CasbinRule>().ToList();
            Assert.Contains(policies, p => p.PType == "p" && p.V0 == "alice");
            Assert.Contains(policies, p => p.PType == "g" && p.V0 == "alice");
        }

        [Fact]
        public async Task TestSingleClientAsyncOperationsStillWork()
        {
            // Arrange
            var client = _clientProviderFixture.GetClient("SingleClientAsync");
            client.Clear();

            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(client);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act
            await enforcer.AddPolicyAsync("alice", "data1", "read");
            await enforcer.AddGroupingPolicyAsync("alice", "admin");

            // Assert
            Assert.Equal(2, await client.Queryable<CasbinRule>().CountAsync());
        }

        [Fact]
        public void TestSingleClientLoadAndSave()
        {
            // Arrange
            var client = _clientProviderFixture.GetClient("SingleClientLoadSave");
            client.Clear();

            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(client);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act - 添加并保存
            enforcer.AddPolicy("alice", "data1", "read");
            enforcer.AddGroupingPolicy("alice", "admin");
            enforcer.SavePolicy();

            // 创建新的 enforcer 并加载
            var newEnforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);
            newEnforcer.LoadPolicy();

            // Assert
            TestGetPolicy(newEnforcer, AsList(
                AsList("alice", "data1", "read")
            ));

            TestGetGroupingPolicy(newEnforcer, AsList(
                AsList("alice", "admin")
            ));
        }

        [Fact]
        public void TestSingleClientWithExistingTests()
        {
            // 此测试模拟 EFCoreAdapterTest.cs 中的模式以确保兼容性
            var client = _clientProviderFixture.GetClient("ExistingPattern");
            client.Clear();

            // 使用数据初始化（类似 EFCoreAdapterTest.cs 中的 InitPolicy）
            var policies = new List<CasbinRule>
            {
                new CasbinRule { PType = "p", V0 = "alice", V1 = "data1", V2 = "read" },
                new CasbinRule { PType = "p", V0 = "bob", V1 = "data2", V2 = "write" },
                new CasbinRule { PType = "g", V0 = "alice", V1 = "data2_admin" }
            };
            client.Insertable(policies).ExecuteCommand();

            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(client);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act - 加载策略
            enforcer.LoadPolicy();

            // Assert - 应该匹配预期行为
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write")
            ));

            TestGetGroupingPolicy(enforcer, AsList(
                AsList("alice", "data2_admin")
            ));
        }

        [Fact]
        public void TestSingleClientRemoveOperations()
        {
            // Arrange
            var client = _clientProviderFixture.GetClient("SingleClientRemove");
            client.Clear();

            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(client);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            enforcer.AddPolicy("alice", "data1", "read");
            enforcer.AddPolicy("bob", "data2", "write");

            // Act
            enforcer.RemovePolicy("alice", "data1", "read");

            // Assert
            var count = client.Queryable<CasbinRule>().Count();
            Assert.Equal(1, count);
            var remaining = client.Queryable<CasbinRule>().First();
            Assert.Equal("bob", remaining.V0);
        }

        [Fact]
        public void TestSingleClientUpdateOperations()
        {
            // Arrange
            var client = _clientProviderFixture.GetClient("SingleClientUpdate");
            client.Clear();

            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(client);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            enforcer.AddPolicy("alice", "data1", "read");

            // Act
            enforcer.UpdatePolicy(
                AsList("alice", "data1", "read"),
                AsList("alice", "data1", "write")
            );

            // Assert
            var count = client.Queryable<CasbinRule>().Count();
            Assert.Equal(1, count);
            var policy = client.Queryable<CasbinRule>().First();
            Assert.Equal("write", policy.V2);
        }

        [Fact]
        public void TestSingleClientBatchOperations()
        {
            // Arrange
            var client = _clientProviderFixture.GetClient("SingleClientBatch");
            client.Clear();

            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(client);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act - 添加多个
            enforcer.AddPolicies(new[]
            {
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("charlie", "data3", "read")
            });

            // Assert
            Assert.Equal(3, client.Queryable<CasbinRule>().Count());

            // Act - 删除多个
            enforcer.RemovePolicies(new[]
            {
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write")
            });

            // Assert
            Assert.Equal(1, client.Queryable<CasbinRule>().Count());
        }

        [Fact]
        public void TestSingleClientFilteredLoading()
        {
            // Arrange
            var client = _clientProviderFixture.GetClient("SingleClientFiltered");
            client.Clear();

            var policies = new List<CasbinRule>
            {
                new CasbinRule { PType = "p", V0 = "alice", V1 = "data1", V2 = "read" },
                new CasbinRule { PType = "p", V0 = "bob", V1 = "data2", V2 = "write" },
                new CasbinRule { PType = "g", V0 = "alice", V1 = "admin" }
            };
            client.Insertable(policies).ExecuteCommand();

            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(client);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act - 仅加载 alice 的策略
            enforcer.LoadFilteredPolicy(new SimpleFieldFilter("p", 0, Policy.ValuesFrom(AsList("alice", "", ""))));

            // Assert
            Assert.True(adapter.IsFiltered);
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read")
            ));
        }

        [Fact]
        public void TestSingleClientProviderWrapping()
        {
            // Arrange - 使用显式 DefaultSqlSugarClientProvider 创建适配器
            var client = _clientProviderFixture.GetClient("ProviderWrapping");
            client.Clear();

            var provider = new DefaultSqlSugarClientProvider(client);
            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(provider);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act
            enforcer.AddPolicy("alice", "data1", "read");

            // Assert - 应该与直接客户端构造函数行为相同
            var count = client.Queryable<CasbinRule>().Count();
            Assert.Equal(1, count);
            Assert.Equal("alice", client.Queryable<CasbinRule>().First().V0);
        }

        [Fact]
        public void TestSingleClientProviderGetAllClients()
        {
            // Arrange
            var client = _clientProviderFixture.GetClient("ProviderGetAll");
            var provider = new DefaultSqlSugarClientProvider(client);

            // Act
            var clients = provider.GetAllClients().ToList();

            // Assert
            Assert.Single(clients);
            Assert.Same(client, clients[0]);
        }

        [Fact]
        public void TestSingleClientProviderGetClientForPolicyType()
        {
            // Arrange
            var client = _clientProviderFixture.GetClient("ProviderGetForType");
            var provider = new DefaultSqlSugarClientProvider(client);

            // Act & Assert - 所有策略类型应该返回相同的客户端
            Assert.Same(client, provider.GetClientForPolicyType("p"));
            Assert.Same(client, provider.GetClientForPolicyType("p2"));
            Assert.Same(client, provider.GetClientForPolicyType("g"));
            Assert.Same(client, provider.GetClientForPolicyType("g2"));
            Assert.Same(client, provider.GetClientForPolicyType(null));
            Assert.Same(client, provider.GetClientForPolicyType(""));
        }

        // 在测试项目中创建临时测试
        [Fact]
        public void TestPolicyFilterOnInMemoryData()
        {
            var rules = new List<CasbinRule>
            {
                new CasbinRule { PType = "p", V0 = "alice", V1 = "data1", V2 = "read" },
                new CasbinRule { PType = "p", V0 = "bob", V1 = "data2", V2 = "write" },
                new CasbinRule { PType = "g", V0 = "alice", V1 = "admin" }
            };
            var filter = new SimpleFieldFilter("p", 0, Policy.ValuesFrom(AsList("alice", "", "")));

            var filtered = filter.Apply(rules.AsQueryable()).ToList();
            Assert.Equal(1, filtered.Count);
            Assert.Equal("alice", filtered[0].V0);
        }

        // 单测代码（同步版，严格检查 DB 行数 + enforcer policy 数）
        [Fact]
        public void TestAddPolicyDoesNotInsertDuplicates()
        {
            // Arrange
            var client = _clientProviderFixture.GetClient("AddPolicyNoDuplicates");
            client.Clear();

            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(client);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act - add the same policy twice
            Assert.True(enforcer.AddPolicy("alice", "data1", "read"));
            Assert.False(enforcer.AddPolicy("alice", "data1", "read"));

            // Assert - in-memory model has only one
            Assert.Single(enforcer.GetPolicy());

            // Assert - database has only one row for this rule
            var rows = client.Queryable<Casbin.Adapter.SqlSugar.Entities.CasbinRule>()
                .Where(r => r.PType == "p" && r.V0 == "alice" && r.V1 == "data1" && r.V2 == "read")
                .ToList();

            Assert.Single(rows);
        }

        // 单测代码（异步版，严格检查 DB 行数 + enforcer policy 数）
        [Fact]
        public async Task TestAddPolicyAsyncDoesNotInsertDuplicates()
        {
            var client = _clientProviderFixture.GetClient("AddPolicyAsyncNoDuplicates");
            client.Clear();

            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(client);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            Assert.True(await enforcer.AddPolicyAsync("alice", "data1", "read"));
            Assert.False(await enforcer.AddPolicyAsync("alice", "data1", "read"));

            Assert.Single(enforcer.GetPolicy());

            var rows = await client.Queryable<Casbin.Adapter.SqlSugar.Entities.CasbinRule>()
                .Where(r => r.PType == "p" && r.V0 == "alice" && r.V1 == "data1" && r.V2 == "read")
                .ToListAsync();

            Assert.Single(rows);
        }

        // // 并发版测试（同步 AddPolicy，多任务并发）
        // [Fact]
        // public async Task TestAddPolicyConcurrentDoesNotInsertDuplicates()
        // {
        //     // Arrange
        //     var dbName = "AddPolicyConcurrentNoDuplicates";

        //     // 用一个 client 做清理与最终验证（不要在并发任务里共享它）
        //     var verifyClient = _clientProviderFixture.GetClient(dbName);
        //     verifyClient.Clear();

        //     var concurrency = Math.Min(Environment.ProcessorCount, 8);
        //     var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        //     // Act: each task uses its own client/adapter/enforcer instance
        //     var tasks = Enumerable.Range(0, concurrency)
        //         .Select(_ => Task.Run(async () =>
        //         {
        //             await start.Task.ConfigureAwait(false);

        //             var client = _clientProviderFixture.GetClient(dbName);
        //             var adapter = new SqlSugarAdapter.SqlSugarAdapter(client);
        //             var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

        //             // SQLite 并发写可能遇到 "database is locked"：少量重试降低 CI 偶发失败
        //             for (int attempt = 0; attempt < 8; attempt++)
        //             {
        //                 try
        //                 {
        //                     enforcer.AddPolicy("alice", "data1", "read");
        //                     return;
        //                 }
        //                 catch (Exception ex) when (
        //                     (ex.Message?.Contains("database is locked", StringComparison.OrdinalIgnoreCase) ?? false) ||
        //                     (ex.Message?.Contains("busy", StringComparison.OrdinalIgnoreCase) ?? false))
        //                 {
        //                     await Task.Delay(15 * (attempt + 1)).ConfigureAwait(false);
        //                 }
        //             }
        //         }))
        //         .ToArray();

        //     start.SetResult(true);
        //     await Task.WhenAll(tasks).ConfigureAwait(false);

        //     // Assert: DB has only one row for this rule
        //     var rows = verifyClient.Queryable<Casbin.Adapter.SqlSugar.Entities.CasbinRule>()
        //         .Where(r => r.PType == "p" && r.V0 == "alice" && r.V1 == "data1" && r.V2 == "read")
        //         .ToList();

        //     Assert.Single(rows);

        //     // Assert: reloading into a fresh enforcer yields only one policy
        //     var reloadAdapter = new SqlSugarAdapter.SqlSugarAdapter(_clientProviderFixture.GetClient(dbName));
        //     var reloadEnforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), reloadAdapter);
        //     reloadEnforcer.LoadPolicy();
        //     Assert.Single(reloadEnforcer.GetPolicy());
        // }

    }
}
