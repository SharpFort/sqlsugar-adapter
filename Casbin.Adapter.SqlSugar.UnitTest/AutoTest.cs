using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Casbin.Adapter.SqlSugar.UnitTest.Extensions;
using SqlSugar;
using Casbin.Adapter.SqlSugar.Entities;
using Casbin.Adapter.SqlSugar.UnitTest.Fixtures;
using Xunit;

namespace Casbin.Adapter.SqlSugar.UnitTest
{
    public class SqlSugarAdapterTest : TestUtil, IClassFixture<ModelProvideFixture>, IClassFixture<SqlSugarClientProviderFixture>
    {
        private readonly ModelProvideFixture _modelProvideFixture;
        private readonly SqlSugarClientProviderFixture _clientProviderFixture;

        public SqlSugarAdapterTest(ModelProvideFixture modelProvideFixture, SqlSugarClientProviderFixture clientProviderFixture)
        {
            _modelProvideFixture = modelProvideFixture;
            _clientProviderFixture = clientProviderFixture;
        }

        private static void InitPolicy(ISqlSugarClient client)
        {
            client.Clear();
            
            var policies = new List<CasbinRule>
            {
                new CasbinRule
                {
                    PType = "p",
                    V0 = "alice",
                    V1 = "data1",
                    V2 = "read",
                },
                new CasbinRule
                {
                    PType = "p",
                    V0 = "bob",
                    V1 = "data2",
                    V2 = "write",
                },
                new CasbinRule
                {
                    PType = "p",
                    V0 = "data2_admin",
                    V1 = "data2",
                    V2 = "read",
                },
                new CasbinRule
                {
                    PType = "p",
                    V0 = "data2_admin",
                    V1 = "data2",
                    V2 = "write",
                },
                new CasbinRule
                {
                    PType = "g",
                    V0 = "alice",
                    V1 = "data2_admin",
                }
            };
            
            client.Insertable(policies).ExecuteCommand();
        }
    }
}
