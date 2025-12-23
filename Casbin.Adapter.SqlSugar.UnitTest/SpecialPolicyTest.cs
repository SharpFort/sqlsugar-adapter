using Casbin.Adapter.SqlSugar.UnitTest.Extensions;
using Casbin.Model;
using Casbin.Adapter.SqlSugar.Entities;
using Casbin.Adapter.SqlSugar.UnitTest.Fixtures;
using Xunit;
using SqlSugar;
using System.Collections.Generic;

namespace Casbin.Adapter.SqlSugar.UnitTest
{
    public class PolicyEdgeCasesTest : TestUtil, IClassFixture<ModelProvideFixture>,
        IClassFixture<SqlSugarClientProviderFixture>
    {
        private readonly ModelProvideFixture _modelProvideFixture;
        private readonly SqlSugarClientProviderFixture _clientProviderFixture;

        public PolicyEdgeCasesTest(ModelProvideFixture modelProvideFixture,
            SqlSugarClientProviderFixture clientProviderFixture)
        {
            _modelProvideFixture = modelProvideFixture;
            _clientProviderFixture = clientProviderFixture;
        }

        [Fact]
        public void TestCommaPolicy()
        {
            var client = _clientProviderFixture.GetClient("CommaPolicy");
            client.Clear();
            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(client);
            var enforcer = new Enforcer(DefaultModel.CreateFromText(
                """
                    [request_definition]
                    r = _
                
                    [policy_definition]
                    p = rule, a1, a2
                
                    [policy_effect]
                    e = some(where (p.eft == allow))
                
                    [matchers]
                    m = eval(p.rule)
                """
            ), adapter);
            enforcer.AddFunction("equal", (string a1, string a2) => a1 == a2);

            enforcer.AddPolicy("equal(p.a1, p.a2)", "a1", "a1");
            Assert.True(enforcer.Enforce("_"));

            enforcer.LoadPolicy();
            Assert.True(enforcer.Enforce("_"));

            enforcer.RemovePolicy("equal(p.a1, p.a2)", "a1", "a1");
            enforcer.AddPolicy("equal(p.a1, p.a2)", "a1", "a2");
            Assert.False(enforcer.Enforce("_"));

            enforcer.LoadPolicy();
            Assert.False(enforcer.Enforce("_"));
        }

        [Fact]
        public void TestUnexpectedPolicy()
        {
            var client = _clientProviderFixture.GetClient("UnexpectedPolicy");
            client.Clear();
            
            var policies = new List<CasbinRule>
            {
                new CasbinRule
                {
                    PType = "p",
                    V0 = "a1",
                    V1 = "a2",
                    V2 = null,
                },
                new CasbinRule
                {
                    PType = "p",
                    V0 = "a1",
                    V1 = "a2",
                    V2 = "a3",
                },
                new CasbinRule
                {
                    PType = "p",
                    V0 = "a1",
                    V1 = "a2",
                    V2 = "a3",
                    V3 = "a4",
                },
                new CasbinRule
                {
                    PType = "p",
                    V0 = "b1",
                    V1 = "b2",
                    V2 = "b3",
                    V3 = "b4",
                }
            };
            
            client.Insertable(policies).ExecuteCommand();

            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(client);
            var enforcer = new Enforcer(DefaultModel.CreateFromText(
                """
                    [request_definition]
                    r = _
                
                    [policy_definition]
                    p = a1, a2, a3
                
                    [policy_effect]
                    e = some(where (p.eft == allow))
                
                    [matchers]
                    m = true
                """), adapter);

            enforcer.LoadPolicy();
            var policies_result = enforcer.GetPolicy();

            TestGetPolicy(enforcer, AsList(
                AsList("a1", "a2", ""),
                AsList("a1", "a2", "a3"),
                AsList("b1", "b2", "b3")
            ));
        }
    }
}