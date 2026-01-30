using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Casbin.Model;
using Casbin.Adapter.SqlSugar.Entities;
using Casbin.Adapter.SqlSugar.UnitTest.Extensions;
using Casbin.Adapter.SqlSugar.UnitTest.Fixtures;
using Casbin.Adapter.SqlSugar.UnitTest.TestAdapters;
using SqlSugar;
using Xunit;
using Casbin.Adapter.SqlSugar;

namespace Casbin.Adapter.SqlSugar.UnitTest
{
    /// <summary>
    /// 测试多客户端功能，不同的策略类型可以存储在独立的数据库客户端/表/Schema 中。
    /// </summary>
    public class MultiClientTest : TestUtil,
        IClassFixture<ModelProvideFixture>,
        IClassFixture<MultiContextProviderFixture>
    {
        private readonly ModelProvideFixture _modelProvideFixture;
        private readonly MultiContextProviderFixture _multiContextProviderFixture;

        public MultiClientTest(
            ModelProvideFixture modelProvideFixture,
            MultiContextProviderFixture multiContextProviderFixture)
        {
            _modelProvideFixture = modelProvideFixture;
            _multiContextProviderFixture = multiContextProviderFixture;
        }

        [Fact]
        public void TestMultiClientAddPolicy()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("AddPolicy");
            var (policyClient, groupingClient) = _multiContextProviderFixture.GetSeparateClients("AddPolicy");

            policyClient.Clear();
            groupingClient.Clear();

            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(provider);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act - 添加策略规则（应该进入策略客户端）
            enforcer.AddPolicy("alice", "data1", "read");
            enforcer.AddPolicy("bob", "data2", "write");

            // 添加分组规则（应该进入分组客户端）
            enforcer.AddGroupingPolicy("alice", "admin");

            // Assert - 验证策略在正确的客户端中
            Assert.Equal(2, policyClient.Queryable<CasbinRule>().Count());
            Assert.Equal(1, groupingClient.Queryable<CasbinRule>().Count());

            // 验证策略数据
            var alicePolicy = policyClient.Queryable<CasbinRule>().First(p => p.V0 == "alice");
            Assert.NotNull(alicePolicy);
            Assert.Equal("p", alicePolicy.PType);
            Assert.Equal("data1", alicePolicy.V1);
            Assert.Equal("read", alicePolicy.V2);

            // 验证分组数据
            var aliceGrouping = groupingClient.Queryable<CasbinRule>().First(p => p.V0 == "alice");
            Assert.NotNull(aliceGrouping);
            Assert.Equal("g", aliceGrouping.PType);
            Assert.Equal("admin", aliceGrouping.V1);
        }

        [Fact]
        public async Task TestMultiClientAddPolicyAsync()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("AddPolicyAsync");
            var (policyClient, groupingClient) = _multiContextProviderFixture.GetSeparateClients("AddPolicyAsync");

            policyClient.Clear();
            groupingClient.Clear();

            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(provider);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act
            await enforcer.AddPolicyAsync("alice", "data1", "read");
            await enforcer.AddPolicyAsync("bob", "data2", "write");
            await enforcer.AddGroupingPolicyAsync("alice", "admin");

            // Assert
            Assert.Equal(2, await policyClient.Queryable<CasbinRule>().CountAsync());
            Assert.Equal(1, await groupingClient.Queryable<CasbinRule>().CountAsync());
        }

        [Fact]
        public void TestMultiClientRemovePolicy()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("RemovePolicy");
            var (policyClient, groupingClient) = _multiContextProviderFixture.GetSeparateClients("RemovePolicy");

            policyClient.Clear();
            groupingClient.Clear();

            // 预填充数据
            policyClient.Insertable(new CasbinRule
            {
                PType = "p",
                V0 = "alice",
                V1 = "data1",
                V2 = "read"
            }).ExecuteCommand();

            groupingClient.Insertable(new CasbinRule
            {
                PType = "g",
                V0 = "alice",
                V1 = "admin"
            }).ExecuteCommand();

            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(provider);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);
            enforcer.LoadPolicy();

            // Act
            enforcer.RemovePolicy("alice", "data1", "read");
            enforcer.RemoveGroupingPolicy("alice", "admin");

            // Assert
            Assert.Equal(0, policyClient.Queryable<CasbinRule>().Count());
            Assert.Equal(0, groupingClient.Queryable<CasbinRule>().Count());
        }

        [Fact]
        public void TestMultiClientLoadPolicy()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("LoadPolicy");
            var (policyClient, groupingClient) = _multiContextProviderFixture.GetSeparateClients("LoadPolicy");

            policyClient.Clear();
            groupingClient.Clear();

            // 添加测试数据到策略客户端
            var policyRules = new List<CasbinRule>
            {
                new CasbinRule { PType = "p", V0 = "alice", V1 = "data1", V2 = "read" },
                new CasbinRule { PType = "p", V0 = "bob", V1 = "data2", V2 = "write" }
            };
            policyClient.Insertable(policyRules).ExecuteCommand();

            // 添加测试数据到分组客户端
            var groupingRules = new List<CasbinRule>
            {
                new CasbinRule { PType = "g", V0 = "alice", V1 = "admin" },
                new CasbinRule { PType = "g", V0 = "bob", V1 = "user" }
            };
            groupingClient.Insertable(groupingRules).ExecuteCommand();

            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(provider);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act
            enforcer.LoadPolicy();

            // Assert - 验证所有策略从两个客户端加载
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write")
            ));

            TestGetGroupingPolicy(enforcer, AsList(
                AsList("alice", "admin"),
                AsList("bob", "user")
            ));
        }

        [Fact]
        public async Task TestMultiClientLoadPolicyAsync()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("LoadPolicyAsync");
            var (policyClient, groupingClient) = _multiContextProviderFixture.GetSeparateClients("LoadPolicyAsync");

            policyClient.Clear();
            groupingClient.Clear();

            policyClient.Insertable(new CasbinRule { PType = "p", V0 = "alice", V1 = "data1", V2 = "read" }).ExecuteCommand();
            groupingClient.Insertable(new CasbinRule { PType = "g", V0 = "alice", V1 = "admin" }).ExecuteCommand();

            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(provider);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act
            await enforcer.LoadPolicyAsync();

            // Assert
            Assert.Single(enforcer.GetPolicy());
            Assert.Single(enforcer.GetGroupingPolicy());
        }

        /// <summary>
        /// 【2026/01/30 注意】此测试原本用于验证多 Client SavePolicy 功能。
        /// 由于 SQLite 使用文件级锁，不支持多连接同时写入，现在改为验证 SQLite 正确拒绝此操作。
        /// 多 Client SavePolicy 功能在 PostgreSQL/MySQL 下仍然有效，由集成测试覆盖：
        /// - TransactionIntegrityTests.SavePolicy_WithSharedConnection_ShouldWriteToAllContextsAtomically
        /// - SchemaDistributionTests.SavePolicy_SeparateConnections_ShouldDistributeAcrossSchemas
        /// </summary>
        [Fact]
        public void TestMultiClientSavePolicy()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("SavePolicy");
            var (policyClient, groupingClient) = _multiContextProviderFixture.GetSeparateClients("SavePolicy");

            policyClient.Clear();
            groupingClient.Clear();

            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(provider);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // 通过 enforcer 添加策略
            enforcer.AddPolicy("alice", "data1", "read");
            enforcer.AddPolicy("bob", "data2", "write");
            enforcer.AddGroupingPolicy("alice", "admin");

            // 【2026/01/30 更新】SQLite 下多 Client SavePolicy 现在会正确抛出 InvalidOperationException
            // 这是预期行为，因为 SQLite 使用文件级锁，不支持多连接同时写入
            var exception = Assert.Throws<InvalidOperationException>(() => enforcer.SavePolicy());
            Assert.Contains("SQLite does not support SavePolicy across multiple SqlSugarClient instances", exception.Message);
            
            // ==================== 原始测试逻辑（适用于 PostgreSQL/MySQL/SQL Server）====================
            // 以下代码保留作为文档参考，展示多 Client SavePolicy 在支持多连接写入的数据库上的预期行为
            // 这些断言在集成测试中使用 PostgreSQL 进行验证
            /*
            // Act - 保存应该将策略分发到正确的客户端
            enforcer.SavePolicy();

            // Assert - 验证数据在正确的客户端中
            Assert.Equal(2, policyClient.Queryable<CasbinRule>().Count());
            Assert.Equal(1, groupingClient.Queryable<CasbinRule>().Count());

            // 验证我们可以从两个客户端重新加载
            var newEnforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);
            newEnforcer.LoadPolicy();

            TestGetPolicy(newEnforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write")
            ));

            TestGetGroupingPolicy(newEnforcer, AsList(
                AsList("alice", "admin")
            ));
            */
        }

        /// <summary>
        /// 【2026/01/30 注意】此测试原本用于验证多 Client SavePolicyAsync 功能。
        /// 由于 SQLite 使用文件级锁，不支持多连接同时写入，现在改为验证 SQLite 正确拒绝此操作。
        /// 多 Client SavePolicyAsync 功能在 PostgreSQL/MySQL 下仍然有效，由集成测试覆盖。
        /// </summary>
        [Fact]
        public async Task TestMultiClientSavePolicyAsync()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("SavePolicyAsync");
            var (policyClient, groupingClient) = _multiContextProviderFixture.GetSeparateClients("SavePolicyAsync");

            policyClient.Clear();
            groupingClient.Clear();

            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(provider);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            enforcer.AddPolicy("alice", "data1", "read");
            enforcer.AddGroupingPolicy("alice", "admin");

            // 【2026/01/30 更新】SQLite 下多 Client SavePolicyAsync 现在会正确抛出 InvalidOperationException
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => enforcer.SavePolicyAsync());
            Assert.Contains("SQLite does not support SavePolicy across multiple SqlSugarClient instances", exception.Message);
            
            // ==================== 原始测试逻辑（适用于 PostgreSQL/MySQL/SQL Server）====================
            // 以下代码保留作为文档参考，展示多 Client SavePolicyAsync 在支持多连接写入的数据库上的预期行为
            /*
            // Act
            await enforcer.SavePolicyAsync();

            // Assert
            Assert.Equal(1, await policyClient.Queryable<CasbinRule>().CountAsync());
            Assert.Equal(1, await groupingClient.Queryable<CasbinRule>().CountAsync());
            */
        }

        [Fact]
        public void TestMultiClientBatchOperations()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("BatchOperations");
            var (policyClient, groupingClient) = _multiContextProviderFixture.GetSeparateClients("BatchOperations");

            policyClient.Clear();
            groupingClient.Clear();

            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(provider);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act - 一次添加多个策略
            enforcer.AddPolicies(new[]
            {
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("charlie", "data3", "read")
            });

            // Assert
            Assert.Equal(3, policyClient.Queryable<CasbinRule>().Count());

            // Act - 删除多个策略
            enforcer.RemovePolicies(new[]
            {
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write")
            });

            // Assert
            Assert.Equal(1, policyClient.Queryable<CasbinRule>().Count());
            Assert.Equal("charlie", policyClient.Queryable<CasbinRule>().First().V0);
        }

        [Fact]
        public void TestMultiClientLoadFilteredPolicy()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("LoadFilteredPolicy");
            var (policyClient, groupingClient) = _multiContextProviderFixture.GetSeparateClients("LoadFilteredPolicy");

            policyClient.Clear();
            groupingClient.Clear();

            // 添加多个策略
            var policies = new List<CasbinRule>
            {
                new CasbinRule { PType = "p", V0 = "alice", V1 = "data1", V2 = "read" },
                new CasbinRule { PType = "p", V0 = "bob", V1 = "data2", V2 = "write" }
            };
            policyClient.Insertable(policies).ExecuteCommand();

            groupingClient.Insertable(new CasbinRule { PType = "g", V0 = "alice", V1 = "admin" }).ExecuteCommand();

            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(provider);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act - 仅加载 alice 的策略
            enforcer.LoadFilteredPolicy(new SimpleFieldFilter("p", 0, Policy.ValuesFrom(AsList("alice", "", ""))));

            // Assert
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read")
            ));

            // Bob 的策略不应该被加载
            Assert.DoesNotContain(enforcer.GetPolicy(), p => p.Contains("bob"));
        }

        [Fact]
        public void TestMultiClientUpdatePolicyNoException()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("UpdatePolicyNoException");
            var (policyClient, groupingClient) = _multiContextProviderFixture.GetSeparateClients("UpdatePolicyNoException");

            policyClient.Clear();
            groupingClient.Clear();

            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(provider);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // 添加初始数据
            enforcer.AddPolicy("alice", "data1", "read");
            enforcer.AddGroupingPolicy("alice", "admin");

            // Act & Assert - UpdatePolicy 应该完成而不抛出异常
            enforcer.UpdatePolicy(
                AsList("alice", "data1", "read"),
                AsList("alice", "data1", "write")
            );

            // 验证更新成功应用
            Assert.True(enforcer.HasPolicy("alice", "data1", "write"));
            Assert.False(enforcer.HasPolicy("alice", "data1", "read"));
        }

        [Fact]
        public void TestMultiClientProviderGetAllClients()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("GetAllClients");

            // Act
            var clients = provider.GetAllClients().ToList();

            // Assert
            Assert.Equal(2, clients.Count);
            Assert.All(clients, client => Assert.NotNull(client));
        }

        [Fact]
        public void TestMultiClientProviderGetClientForPolicyType()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("GetClientForType");

            // Act & Assert
            var pClient = provider.GetClientForPolicyType("p");
            var p2Client = provider.GetClientForPolicyType("p2");
            var gClient = provider.GetClientForPolicyType("g");
            var g2Client = provider.GetClientForPolicyType("g2");

            // 所有 'p' 类型应该路由到同一个客户端
            Assert.Same(pClient, p2Client);

            // 所有 'g' 类型应该路由到同一个客户端
            Assert.Same(gClient, g2Client);

            // 'p' 和 'g' 类型应该路由到不同的客户端
            Assert.NotSame(pClient, gClient);
        }

        [Fact]
        public void TestCorrectClientAndTableRouting()
        {
            // 此测试验证 SqlSugar 适配器正确地将不同的策略类型路由到各自的客户端和表。
            // 这是 EFCore 适配器中 DbSet 缓存测试的 SqlSugar 等效测试。
            //
            // 关键验证点：
            // 1. 每个策略类型（'p' 和 'g'）应该路由到正确的客户端（仅在第一次访问时）
            // 2. 数据应该被写入到正确的客户端/数据库
            // 3. 策略类型应该被正确保留
            // 4. 后续对同一策略类型的操作应该使用相同的客户端引用

            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("ClientRouting");
            var (policyClient, groupingClient) = _multiContextProviderFixture.GetSeparateClients("ClientRouting");

            policyClient.Clear();
            groupingClient.Clear();

            // 创建路由跟踪器
            var routingTracker = new Dictionary<string, ClientRoutingInfo>();
            var adapter = new ClientRoutingTestAdapter(provider, routingTracker);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act - 添加不同类型的策略
            enforcer.AddPolicy("alice", "data1", "read");      // Type 'p' - 第一次调用应该路由到策略客户端
            enforcer.AddPolicy("bob", "data2", "write");       // Type 'p' - 应该使用相同的客户端引用
            enforcer.AddGroupingPolicy("alice", "admin");      // Type 'g' - 不同类型，应该路由到分组客户端
            enforcer.AddGroupingPolicy("bob", "user");         // Type 'g' - 应该使用相同的客户端引用

            // Assert 1 - 验证路由跟踪器记录了两种策略类型
            Assert.Equal(2, routingTracker.Count);
            Assert.True(routingTracker.ContainsKey("p"));
            Assert.True(routingTracker.ContainsKey("g"));

            // Assert 2 - 验证每种类型都被调用了正确的次数
            Assert.Equal(2, routingTracker["p"].CallCount);  // alice 和 bob 的策略
            Assert.Equal(2, routingTracker["g"].CallCount);  // alice 和 bob 的分组

            // Assert 3 - 验证 'p' 和 'g' 路由到不同的客户端实例
            var pRoutedClient = routingTracker["p"].Client;
            var gRoutedClient = routingTracker["g"].Client;
            Assert.NotSame(pRoutedClient, gRoutedClient);

            // Assert 4 - 验证数据被写入到正确的客户端
            Assert.Equal(2, policyClient.Queryable<CasbinRule>().Count());
            Assert.Equal(2, groupingClient.Queryable<CasbinRule>().Count());

            // Assert 5 - 验证策略类型被正确保留
            var policyRules = policyClient.Queryable<CasbinRule>().ToList();
            Assert.All(policyRules, p => Assert.Equal("p", p.PType));

            var groupingRules = groupingClient.Queryable<CasbinRule>().ToList();
            Assert.All(groupingRules, g => Assert.Equal("g", g.PType));

            // Assert 6 - 验证数据内容正确
            Assert.Contains(policyRules, p => p.V0 == "alice" && p.V1 == "data1" && p.V2 == "read");
            Assert.Contains(policyRules, p => p.V0 == "bob" && p.V1 == "data2" && p.V2 == "write");
            Assert.Contains(groupingRules, g => g.V0 == "alice" && g.V1 == "admin");
            Assert.Contains(groupingRules, g => g.V0 == "bob" && g.V1 == "user");

            // Assert 7 - 验证路由到的客户端与提供器返回的客户端一致
            var providerPolicyClient = provider.GetClientForPolicyType("p");
            var providerGroupingClient = provider.GetClientForPolicyType("g");
            Assert.Same(pRoutedClient, providerPolicyClient);
            Assert.Same(gRoutedClient, providerGroupingClient);
        }
    }
}
