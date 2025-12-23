using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Casbin.Model;
using Casbin.Adapter.SqlSugar.Entities;
using Casbin.Adapter.SqlSugar.UnitTest.Extensions;
using Casbin.Adapter.SqlSugar.UnitTest.Fixtures;
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
        }

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

            // Act
            await enforcer.SavePolicyAsync();

            // Assert
            Assert.Equal(1, await policyClient.Queryable<CasbinRule>().CountAsync());
            Assert.Equal(1, await groupingClient.Queryable<CasbinRule>().CountAsync());
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
    }
}
