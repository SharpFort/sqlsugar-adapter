using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Casbin;
using Casbin.Model;
using Npgsql;
using SqlSugar;
using Xunit;
using Xunit.Abstractions;
using Casbin.Adapter.SqlSugar.UnitTest;
using Casbin.Adapter.SqlSugar.Entities;

#nullable enable


namespace Casbin.Adapter.SqlSugar.UnitTest.Integration
{
    /// <summary>
    /// Tests to verify whether HasDefaultSchema() properly distributes policies across PostgreSQL schemas,
    /// both with separate connections and with shared connections.
    ///
    /// Purpose: Determine if explicit SET search_path is necessary or if EF Core's HasDefaultSchema()
    /// generates schema-qualified SQL that works correctly with shared connections.
    /// </summary>
    // [Trait("Category", "Integration")]
    // [Collection("IntegrationTests")]
    // public class SchemaDistributionTests : IClassFixture<TransactionIntegrityTestFixture>, IAsyncLifetime
    // {
    //     private readonly TransactionIntegrityTestFixture _fixture;
    //     private readonly ITestOutputHelper _output;
    //     private const string ModelPath = "examples/multi_context_model.conf";

    //     public SchemaDistributionTests(TransactionIntegrityTestFixture fixture, ITestOutputHelper output)
    //     {
    //         _fixture = fixture;
    //         _output = output;
    //     }

    //     public Task InitializeAsync() => _fixture.ClearAllPoliciesAsync();
    //     public Task DisposeAsync() => _fixture.RunMigrationsAsync();

        // #region Helper: Derived Context Classes

        // /// <summary>
        // /// Derived context for policies schema
        // /// </summary>
        // public class TestCasbinDbContext1 : CasbinDbContext<int>
        // {
        //     public TestCasbinDbContext1(
        //         DbContextOptions<CasbinDbContext<int>> options,
        //         string schemaName,
        //         string tableName)
        //         : base(options, schemaName, tableName)
        //     {
        //     }
        // }

        // /// <summary>
        // /// Derived context for groupings schema
        // /// </summary>
        // public class TestCasbinDbContext2 : CasbinDbContext<int>
        // {
        //     public TestCasbinDbContext2(
        //         DbContextOptions<CasbinDbContext<int>> options,
        //         string schemaName,
        //         string tableName)
        //         : base(options, schemaName, tableName)
        //     {
        //     }
        // }

        // /// <summary>
        // /// Derived context for roles schema
        // /// </summary>
        // public class TestCasbinDbContext3 : CasbinDbContext<int>
        // {
        //     public TestCasbinDbContext3(
        //         DbContextOptions<CasbinDbContext<int>> options,
        //         string schemaName,
        //         string tableName)
        //         : base(options, schemaName, tableName)
        //     {
        //     }
        // }


    [Trait("Category", "Integration")]
    [Collection("IntegrationTests")]
    public class SchemaDistributionTests : TestUtil, IClassFixture<TransactionIntegrityTestFixture>, IAsyncLifetime
    {
        private readonly TransactionIntegrityTestFixture _fixture;
        private readonly ITestOutputHelper _output;
        private const string ModelPath = "examples/multi_context_model.conf";

        public SchemaDistributionTests(TransactionIntegrityTestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        // 初始化和清理逻辑保持一致，调用 Fixture 中已实现的 SqlSugar 版本方法
        public Task InitializeAsync() => _fixture.ClearAllPoliciesAsync();

        public Task DisposeAsync() => _fixture.RunMigrationsAsync();


        #region Helper: SqlSugar Configuration Helpers

        /// <summary>
        /// 在 SqlSugar 中，我们不需要像 EFCore 那样为每个 Schema 创建一个派生类。
        /// 为了保持测试代码结构的一致性，我们定义一个辅助方法来生成带有特定 Schema 映射的配置。
        /// </summary>
        private ConnectionConfig CreateSchemaConfig(string connectionString, string schemaName, string tableName, System.Data.Common.DbConnection? sharedConnection = null)
        {
            return new ConnectionConfig()
            {
                // SqlSugar 的 ConnectionConfig 不直接支持 DbConnection 属性
                // 需要在创建客户端后通过 client.Ado.Connection = sharedConnection 注入外部连接
                ConnectionString = connectionString,
                // DbConnection = sharedConnection, // EFCore风格，SqlSugar不支持
                DbType = DbType.PostgreSQL,
                IsAutoCloseConnection = sharedConnection == null,
                ConfigureExternalServices = new ConfigureExternalServices
                {
                    EntityService = (c, p) =>
                    {
                        if (p.EntityName == nameof(CasbinRule))
                        {
                            // 核心：SqlSugar 通过 "Schema.TableName" 实现跨 Schema 访问
                            p.DbTableName = $"{schemaName}.{tableName}";
                        }
                    }
                }
            };
        }

        // 以下类保留名称，但不再继承 DbContext，仅作为逻辑标识（如果后续测试代码中有显式引用）
        // 或者你可以直接在测试方法中使用上面的 CreateSchemaConfig
        public class TestCasbinSqlSugarClient : SqlSugarClient
        {
            public TestCasbinSqlSugarClient(ConnectionConfig config) : base(config)
            {
            }
        }

        #endregion


        #region Helper: Three-Context Provider

        // /// <summary>
        // /// Provider that routes policy types to three separate contexts
        // /// </summary>
        // private class ThreeWayContextProvider : ICasbinDbContextProvider<int>
        // {
        //     private readonly CasbinDbContext<int> _policyContext;
        //     private readonly CasbinDbContext<int> _groupingContext;
        //     private readonly CasbinDbContext<int> _roleContext;
        //     private readonly System.Data.Common.DbConnection? _sharedConnection;

        //     public ThreeWayContextProvider(
        //         CasbinDbContext<int> policyContext,
        //         CasbinDbContext<int> groupingContext,
        //         CasbinDbContext<int> roleContext,
        //         System.Data.Common.DbConnection? sharedConnection)
        //     {
        //         _policyContext = policyContext;
        //         _groupingContext = groupingContext;
        //         _roleContext = roleContext;
        //         _sharedConnection = sharedConnection;
        //     }

        //     public DbContext GetContextForPolicyType(string policyType)
        //     {
        //         return policyType switch
        //         {
        //             "p" => _policyContext,      // p policies → casbin_policies schema
        //             "g" => _groupingContext,     // g groupings → casbin_groupings schema
        //             "g2" => _roleContext,        // g2 roles → casbin_roles schema
        //             _ => _policyContext
        //         };
        //     }

        //     public IEnumerable<DbContext> GetAllContexts()
        //     {
        //         return new[] { _policyContext, _groupingContext, _roleContext };
        //     }

        //     public System.Data.Common.DbConnection? GetSharedConnection()
        //     {
        //         return _sharedConnection;
        //     }
        // }

        // #endregion



        /// <summary>
        /// Provider that routes policy types to three separate SqlSugar clients.
        /// 将不同的策略类型路由到映射了不同 Schema 的 SqlSugar 客户端。
        /// </summary>
        private class ThreeWaySqlSugarClientProvider : ISqlSugarClientProvider
        {
            private readonly ISqlSugarClient _policyClient;
            private readonly ISqlSugarClient _groupingClient;
            private readonly ISqlSugarClient _roleClient;
            private readonly System.Data.Common.DbConnection? _sharedConnection;

            public ThreeWaySqlSugarClientProvider(
                ISqlSugarClient policyClient,
                ISqlSugarClient groupingClient,
                ISqlSugarClient roleClient,
                System.Data.Common.DbConnection? sharedConnection)
            {
                _policyClient = policyClient;
                _groupingClient = groupingClient;
                _roleClient = roleClient;
                _sharedConnection = sharedConnection;
            }

            public bool SharesConnection => _sharedConnection != null;

            /// <summary>
            /// 根据策略类型获取对应的 SqlSugar 客户端
            /// </summary>
            public ISqlSugarClient GetClientForPolicyType(string policyType)
            {
                return policyType switch
                {
                    "p" => _policyClient,      // p policies → 映射到 casbin_policies schema
                    "g" => _groupingClient,     // g groupings → 映射到 casbin_groupings schema
                    "g2" => _roleClient,        // g2 roles → 映射到 casbin_roles schema
                    _ => _policyClient
                };
            }

            /// <summary>
            /// 获取所有相关的客户端实例
            /// </summary>
            public IEnumerable<ISqlSugarClient> GetAllClients()
            {
                return new[] { _policyClient, _groupingClient, _roleClient };
            }

            /// <summary>
            /// 获取共享的数据库连接
            /// </summary>
            public System.Data.Common.DbConnection? GetSharedConnection()
            {
                return _sharedConnection;
            }

            /// <summary>
            /// 【集成测试多 Schema 支持 - 2024/12/21】
            /// 根据策略类型返回完全限定的表名称（schema.table_name）。
            /// 此方法是使多 Schema 分布测试正常工作的关键。
            /// </summary>
            public string? GetTableNameForPolicyType(string policyType)
            {
                // 根据策略类型返回对应 Schema 的完全限定表名
                // 格式: "schema_name.table_name"
                return policyType switch
                {
                    "p" => $"{TransactionIntegrityTestFixture.PoliciesSchema}.casbin_rule",
                    "g" => $"{TransactionIntegrityTestFixture.GroupingsSchema}.casbin_rule",
                    "g2" => $"{TransactionIntegrityTestFixture.RolesSchema}.casbin_rule",
                    _ => $"{TransactionIntegrityTestFixture.PoliciesSchema}.casbin_rule"
                };
            }
        }

        #endregion

        // #region Helper: SqlSugar Client Factory (Tier A)
        // // Creates schema-specific SqlSugar clients with searchpath
        // private (ISqlSugarClient policies, ISqlSugarClient groupings, ISqlSugarClient roles) 
        //     CreateThreeSchemaClients()
        // {
        //     var policies = CreateClientWithSchema(
        //         _fixture.ConnectionString, 
        //         TransactionIntegrityTestFixture.PoliciesSchema);
            
        //     var groupings = CreateClientWithSchema(
        //         _fixture.ConnectionString,
        //         TransactionIntegrityTestFixture.GroupingsSchema);
            
        //     var roles = CreateClientWithSchema(
        //         _fixture.ConnectionString,
        //         TransactionIntegrityTestFixture.RolesSchema);
            
        //     return (policies, groupings, roles);
        // }
        // private ISqlSugarClient CreateClientWithSchema(string connectionString, string schemaName)
        // {
        //     var config = new ConnectionConfig
        //     {
        //         ConnectionString = connectionString + $";searchpath={schemaName}",
        //         DbType = DbType.PostgreSQL,
        //         IsAutoCloseConnection = false,
        //         InitKeyType = InitKeyType.Attribute
        //     };
        //     var client = new SqlSugarClient(config);
        //     client.CodeFirst.InitTables<CasbinRule>();
        //     return client;
        // }
        // // Provider for Tier A (independent connections)
        // private class ThreeWayClientProvider : ISqlSugarClientProvider
        // {
        //     private readonly ISqlSugarClient _policies;
        //     private readonly ISqlSugarClient _groupings;
        //     private readonly ISqlSugarClient _roles;
        //     public ThreeWayClientProvider(
        //         ISqlSugarClient policies,
        //         ISqlSugarClient groupings,
        //         ISqlSugarClient roles)
        //     {
        //         _policies = policies;
        //         _groupings = groupings;
        //         _roles = roles;
        //     }
        //     public ISqlSugarClient GetClientForPolicyType(string policyType) => policyType switch
        //     {
        //         "p" => _policies,
        //         "g" => _groupings,
        //         "g2" => _roles,
        //         _ =>_policies
        //     };
        //     public IEnumerable<ISqlSugarClient> GetAllClients() => 
        //         new[] { _policies, _groupings, _roles };
        //     public bool SharesConnection => false; // Tier A: independent connections
        // }
        // #endregion

        #region Test 1: Separate Connections (Control/Baseline)

        /// <summary>
        /// BASELINE TEST: Proves that HasDefaultSchema() correctly distributes policies across schemas
        /// when contexts use SEPARATE connections (no shared connection).
        ///
        /// This is the baseline that should work regardless of any SET search_path logic.
        /// </summary>
        // [Fact]
        // public async Task SavePolicy_SeparateConnections_ShouldDistributeAcrossSchemas()
        // {
        //     _output.WriteLine("=== TEST: Separate Connections - Schema Distribution ===");

        //     // Create three contexts with SEPARATE connection strings (no shared connection)
        //     var policyOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
        //         .UseNpgsql(_fixture.ConnectionString)  // Connection #1
        //         .Options;
        //     var policyContext = new TestCasbinDbContext1(policyOptions, TransactionIntegrityTestFixture.PoliciesSchema, "casbin_rule");

        //     var groupingOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
        //         .UseNpgsql(_fixture.ConnectionString)  // Connection #2
        //         .Options;
        //     var groupingContext = new TestCasbinDbContext2(groupingOptions, TransactionIntegrityTestFixture.GroupingsSchema, "casbin_rule");

        //     var roleOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
        //         .UseNpgsql(_fixture.ConnectionString)  // Connection #3
        //         .Options;
        //     var roleContext = new TestCasbinDbContext3(roleOptions, TransactionIntegrityTestFixture.RolesSchema, "casbin_rule");

        //     _output.WriteLine("Created three contexts with SEPARATE connections");

        //     // Verify they are different connection objects
        //     var conn1 = policyContext.Database.GetDbConnection();
        //     var conn2 = groupingContext.Database.GetDbConnection();
        //     var conn3 = roleContext.Database.GetDbConnection();

        //     Assert.False(ReferenceEquals(conn1, conn2), "Connections 1 and 2 should be different objects");
        //     Assert.False(ReferenceEquals(conn2, conn3), "Connections 2 and 3 should be different objects");
        //     _output.WriteLine("Verified: Contexts use DIFFERENT DbConnection objects");

        //     try
        //     {
        //         // Create provider and adapter
        //         // Pass null since these contexts use separate connections
        //         var provider = new ThreeWayContextProvider(policyContext, groupingContext, roleContext, null);
        //         var adapter = new EFCoreAdapter<int>(provider);

        //         // Create enforcer without loading policy (tables might be empty)
        //         var model = DefaultModel.CreateFromFile(ModelPath);
        //         var enforcer = new Enforcer(model);
        //         enforcer.SetAdapter(adapter);

        //         // Add policies to in-memory model (not persisted yet)
        //         enforcer.AddPolicy("alice", "data1", "read");      // → casbin_policies
        //         enforcer.AddGroupingPolicy("alice", "admin");       // → casbin_groupings
        //         enforcer.AddNamedGroupingPolicy("g2", "admin", "role-superuser"); // → casbin_roles

        //         _output.WriteLine("Added policies to in-memory model:");
        //         _output.WriteLine("  p policy → casbin_policies");
        //         _output.WriteLine("  g policy → casbin_groupings");
        //         _output.WriteLine("  g2 policy → casbin_roles");

        //         // Save to database
        //         await enforcer.SavePolicyAsync();
        //         _output.WriteLine("Called SavePolicyAsync()");

        //         // Verify distribution across schemas
        //         var policiesCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.PoliciesSchema);
        //         var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.GroupingsSchema);
        //         var rolesCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.RolesSchema);

        //         _output.WriteLine($"Schema distribution:");
        //         _output.WriteLine($"  casbin_policies: {policiesCount} policy");
        //         _output.WriteLine($"  casbin_groupings: {groupingsCount} policy");
        //         _output.WriteLine($"  casbin_roles: {rolesCount} policy");

        //         // CRITICAL ASSERTION: Policies should be distributed across all three schemas
        //         Assert.Equal(1, policiesCount);
        //         Assert.Equal(1, groupingsCount);
        //         Assert.Equal(1, rolesCount);

        //         _output.WriteLine("✓ BASELINE TEST PASSED: HasDefaultSchema() distributes policies correctly with separate connections");
        //     }
        //     finally
        //     {
        //         await policyContext.DisposeAsync();
        //         await groupingContext.DisposeAsync();
        //         await roleContext.DisposeAsync();
        //     }
        // }

        
        [Fact]
        public async Task SavePolicy_SeparateConnections_ShouldDistributeAcrossSchemas()
        {
            _output.WriteLine("=== TEST: Separate Connections - Schema Distribution ===");

            // 使用之前定义的辅助方法 CreateSchemaConfig 创建三个独立的配置
            // 默认情况下，不传入 sharedConnection，SqlSugar 会根据连接字符串创建独立连接
            // var policyConfig = CreateSchemaConfig(_fixture.ConnectionString, TransactionIntegrityTestFixture.PoliciesSchema, "casbin_rule");
            // 注意：此测试测试的是"独立连接"场景，因此 sharedConnection 应该为 null
            var policyConfig = CreateSchemaConfig(_fixture.ConnectionString, TransactionIntegrityTestFixture.PoliciesSchema, "casbin_rule", null);
            var policyClient = new SqlSugarClient(policyConfig);
            // if (sharedConnection != null) policyClient.Ado.Connection = sharedConnection; // 独立连接测试，无需共享连接

            var groupingConfig = CreateSchemaConfig(_fixture.ConnectionString, TransactionIntegrityTestFixture.GroupingsSchema, "casbin_rule", null);
            var groupingClient = new SqlSugarClient(groupingConfig);
            // if (sharedConnection != null) groupingClient.Ado.Connection = sharedConnection; // 独立连接测试，无需共享连接

            var roleConfig = CreateSchemaConfig(_fixture.ConnectionString, TransactionIntegrityTestFixture.RolesSchema, "casbin_rule", null);
            var roleClient = new SqlSugarClient(roleConfig);
            // if (sharedConnection != null) roleClient.Ado.Connection = sharedConnection; // 独立连接测试，无需共享连接

            _output.WriteLine("Created three SqlSugar clients with SEPARATE connections");

            // 验证它们是不同的连接对象
            // SqlSugar 的 Ado.Connection 会获取当前的 DbConnection 实例
            var conn1 = policyClient.Ado.Connection;
            var conn2 = groupingClient.Ado.Connection;
            var conn3 = roleClient.Ado.Connection;

            Assert.False(ReferenceEquals(conn1, conn2), "Connections 1 and 2 should be different objects");
            Assert.False(ReferenceEquals(conn2, conn3), "Connections 2 and 3 should be different objects");
            _output.WriteLine("Verified: Clients use DIFFERENT DbConnection objects");

            try
            {
                // 创建 Provider 和 Adapter
                // 传入 null 作为 sharedConnection，因为这些客户端使用独立连接
                var provider = new ThreeWaySqlSugarClientProvider(policyClient, groupingClient, roleClient, null);
                // 使用 autoCodeFirst: false 因为表已由 Fixture 预先创建
                var adapter = new SqlSugarAdapter(provider, autoCodeFirst: false);

                // 创建 Enforcer - 使用 Enforcer(model, adapter) 模式
                var model = DefaultModel.CreateFromFile(ModelPath);
                var enforcer = new Enforcer(model, adapter);

                // 【2024/12/21 修复测试隔离问题】
                // Enforcer(model, adapter) 构造函数会自动调用 LoadPolicy() 从数据库加载现有数据。
                // 这可能包含之前测试运行遗留的数据，导致测试结果不正确。
                // 调用 ClearPolicy() 清空内存中的策略，确保测试从干净状态开始。
                enforcer.ClearPolicy();

                // 将策略添加到内存模型中（尚未持久化）
                enforcer.AddPolicy("alice", "data1", "read");      // → 路由到 casbin_policies
                enforcer.AddGroupingPolicy("alice", "admin");       // → 路由到 casbin_groupings
                enforcer.AddNamedGroupingPolicy("g2", "admin", "role-superuser"); // → 路由到 casbin_roles


                _output.WriteLine("Added policies to in-memory model:");
                _output.WriteLine("  p policy → casbin_policies");
                _output.WriteLine("  g policy → casbin_groupings");
                _output.WriteLine("  g2 policy → casbin_roles");

                // 保存到数据库
                await enforcer.SavePolicyAsync();
                _output.WriteLine("Called SavePolicyAsync()");

                // 验证不同 Schema 中的数据分布
                // _fixture.CountPoliciesInSchemaAsync 是集成测试环境提供的辅助方法，保持不变
                var policiesCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.PoliciesSchema);
                var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.GroupingsSchema);
                var rolesCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.RolesSchema);

                _output.WriteLine($"Schema distribution:");
                _output.WriteLine($"  casbin_policies: {policiesCount} policy");
                _output.WriteLine($"  casbin_groupings: {groupingsCount} policy");
                _output.WriteLine($"  casbin_roles: {rolesCount} policy");

                // 核心断言：策略应该被正确分布到三个不同的 Schema 中
                Assert.Equal(1, policiesCount);
                Assert.Equal(1, groupingsCount);
                Assert.Equal(1, rolesCount);

                _output.WriteLine("✓ BASELINE TEST PASSED: SqlSugar distributes policies correctly with separate connections");
            }
            finally
            {
                // 释放客户端资源
                policyClient.Dispose();
                groupingClient.Dispose();
                roleClient.Dispose();
            }
        }

        #endregion

        // #region Test 2: Shared Connection (Critical Test)

        // /// <summary>
        // /// CRITICAL TEST: Determines if HasDefaultSchema() correctly distributes policies across schemas
        // /// when contexts share a SINGLE DbConnection object.
        // ///
        // /// If this test FAILS: SET search_path approach is necessary
        // /// If this test PASSES: SET search_path approach is NOT necessary
        // /// </summary>
        // [Fact]
        // public async Task SavePolicy_SharedConnection_ShouldDistributeAcrossSchemas()
        // {
        //     _output.WriteLine("=== TEST: Shared Connection - Schema Distribution ===");

        //     // Create ONE shared connection (CRITICAL for this test)
        //     var sharedConnection = new NpgsqlConnection(_fixture.ConnectionString);
        //     await sharedConnection.OpenAsync();
        //     _output.WriteLine("Opened shared connection for all three contexts");

        //     try
        //     {
        //         // Create three contexts using SAME connection object
        //         var policyOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
        //             .UseNpgsql(sharedConnection)  // ✅ Shared connection
        //             .Options;
        //         var policyContext = new TestCasbinDbContext1(policyOptions, TransactionIntegrityTestFixture.PoliciesSchema, "casbin_rule");

        //         var groupingOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
        //             .UseNpgsql(sharedConnection)  // ✅ Same connection
        //             .Options;
        //         var groupingContext = new TestCasbinDbContext2(groupingOptions, TransactionIntegrityTestFixture.GroupingsSchema, "casbin_rule");

        //         var roleOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
        //             .UseNpgsql(sharedConnection)  // ✅ Same connection
        //             .Options;
        //         var roleContext = new TestCasbinDbContext3(roleOptions, TransactionIntegrityTestFixture.RolesSchema, "casbin_rule");

        //         _output.WriteLine("Created three contexts sharing the same connection");

        //         // Verify reference equality
        //         var conn1 = policyContext.Database.GetDbConnection();
        //         var conn2 = groupingContext.Database.GetDbConnection();
        //         var conn3 = roleContext.Database.GetDbConnection();

        //         Assert.True(ReferenceEquals(conn1, conn2), "Connections 1 and 2 should be the SAME object");
        //         Assert.True(ReferenceEquals(conn2, conn3), "Connections 2 and 3 should be the SAME object");
        //         _output.WriteLine("Verified: All contexts share the SAME DbConnection object (reference equality)");

        //         // Create provider and adapter
        //         // Pass sharedConnection since all contexts share it
        //         var provider = new ThreeWayContextProvider(policyContext, groupingContext, roleContext, sharedConnection);
        //         var adapter = new EFCoreAdapter<int>(provider);

        //         // Create enforcer without loading policy (tables might be empty)
        //         var model = DefaultModel.CreateFromFile(ModelPath);
        //         var enforcer = new Enforcer(model);
        //         enforcer.SetAdapter(adapter);

        //         // Add policies to in-memory model (not persisted yet)
        //         enforcer.AddPolicy("bob", "data2", "write");         // → casbin_policies
        //         enforcer.AddGroupingPolicy("bob", "developer");      // → casbin_groupings
        //         enforcer.AddNamedGroupingPolicy("g2", "developer", "role-contributor"); // → casbin_roles

        //         _output.WriteLine("Added policies to in-memory model:");
        //         _output.WriteLine("  p policy → should go to casbin_policies");
        //         _output.WriteLine("  g policy → should go to casbin_groupings");
        //         _output.WriteLine("  g2 policy → should go to casbin_roles");

        //         // Save to database
        //         await enforcer.SavePolicyAsync();
        //         _output.WriteLine("Called SavePolicyAsync()");

        //         // Verify distribution across schemas
        //         var policiesCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.PoliciesSchema);
        //         var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.GroupingsSchema);
        //         var rolesCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.RolesSchema);

        //         _output.WriteLine($"Schema distribution:");
        //         _output.WriteLine($"  casbin_policies: {policiesCount} policy");
        //         _output.WriteLine($"  casbin_groupings: {groupingsCount} policy");
        //         _output.WriteLine($"  casbin_roles: {rolesCount} policy");

        //         // CRITICAL ASSERTION: Policies should be distributed across all three schemas
        //         // If all policies end up in ONE schema, HasDefaultSchema() does NOT work with shared connections
        //         // and we NEED the SET search_path approach

        //         if (policiesCount == 1 && groupingsCount == 1 && rolesCount == 1)
        //         {
        //             _output.WriteLine("✓✓✓ SHARED CONNECTION TEST PASSED!");
        //             _output.WriteLine("HasDefaultSchema() correctly distributes policies even with shared connection");
        //             _output.WriteLine("CONCLUSION: SET search_path approach is NOT necessary");
        //         }
        //         else
        //         {
        //             _output.WriteLine("✗✗✗ SHARED CONNECTION TEST FAILED!");
        //             _output.WriteLine($"Expected distribution: (1, 1, 1), Got: ({policiesCount}, {groupingsCount}, {rolesCount})");
        //             _output.WriteLine("CONCLUSION: SET search_path approach IS necessary for shared connections");
        //         }

        //         Assert.Equal(1, policiesCount);
        //         Assert.Equal(1, groupingsCount);
        //         Assert.Equal(1, rolesCount);

        //         await policyContext.DisposeAsync();
        //         await groupingContext.DisposeAsync();
        //         await roleContext.DisposeAsync();
        //     }
        //     finally
        //     {
        //         await sharedConnection.DisposeAsync();
        //     }
        // }

        // #endregion

        #region Test 2: Shared Connection (Critical Test)

        /// <summary>
        /// CRITICAL TEST: Determines if SqlSugar correctly distributes policies across schemas
        /// when clients share a SINGLE DbConnection object.
        ///
        /// If this test FAILS: A "SET search_path" approach would be necessary.
        /// If this test PASSES: SqlSugar's schema-qualified table mapping (schema.table) is sufficient.
        /// </summary>
        [Fact]
        public async Task SavePolicy_SharedConnection_ShouldDistributeAcrossSchemas()
        {
            _output.WriteLine("=== TEST: Shared Connection - Schema Distribution ===");

            // 1. 创建一个唯一的共享连接 (此测试的关键)
            var sharedConnection = new NpgsqlConnection(_fixture.ConnectionString);
            await sharedConnection.OpenAsync();
            _output.WriteLine("Opened shared connection for all three SqlSugar clients");

            try
            {
                // 2. 创建三个 SqlSugar 客户端，全部指向同一个 sharedConnection 对象
                // 使用之前定义的 CreateSchemaConfig 辅助方法
                var policyConfig = CreateSchemaConfig(_fixture.ConnectionString, TransactionIntegrityTestFixture.PoliciesSchema, "casbin_rule", sharedConnection);
                var policyClient = new SqlSugarClient(policyConfig);
                // 关键：必须手动注入共享连接，因为 SqlSugar 的 ConnectionConfig 不支持直接设置 DbConnection
                policyClient.Ado.Connection = sharedConnection;

                var groupingConfig = CreateSchemaConfig(_fixture.ConnectionString, TransactionIntegrityTestFixture.GroupingsSchema, "casbin_rule", sharedConnection);
                var groupingClient = new SqlSugarClient(groupingConfig);
                groupingClient.Ado.Connection = sharedConnection;

                var roleConfig = CreateSchemaConfig(_fixture.ConnectionString, TransactionIntegrityTestFixture.RolesSchema, "casbin_rule", sharedConnection);
                var roleClient = new SqlSugarClient(roleConfig);
                roleClient.Ado.Connection = sharedConnection;

                _output.WriteLine("Created three SqlSugar clients sharing the same connection object");

                // 3. 验证引用一致性 (确保它们确实在用同一个连接对象)
                var conn1 = policyClient.Ado.Connection;
                var conn2 = groupingClient.Ado.Connection;
                var conn3 = roleClient.Ado.Connection;

                Assert.True(ReferenceEquals(conn1, conn2), "Connections 1 and 2 should be the SAME object");
                Assert.True(ReferenceEquals(conn2, conn3), "Connections 2 and 3 should be the SAME object");
                _output.WriteLine("Verified: All clients share the SAME DbConnection object (reference equality)");

                // 4. 创建 Provider 和 Adapter
                // 传入 sharedConnection 以便适配器在 SavePolicyAsync 中处理事务
                // 使用 autoCodeFirst: false 因为表已由 Fixture 预先创建
                var provider = new ThreeWaySqlSugarClientProvider(policyClient, groupingClient, roleClient, sharedConnection);
                var adapter = new SqlSugarAdapter(provider, autoCodeFirst: false);

                // 5. 创建 Enforcer - 使用与单元测试相同的构造函数模式
                // 注意：使用 Enforcer(model, adapter) 而不是 Enforcer(model) + SetAdapter
                var model = DefaultModel.CreateFromFile(ModelPath);
                var enforcer = new Enforcer(model, adapter);

                // 【2024/12/21 修复测试隔离问题】清空自动加载的历史数据
                enforcer.ClearPolicy();

                // 6. 向内存模型添加策略
                enforcer.AddPolicy("bob", "data2", "write");         // → 路由到 casbin_policies
                enforcer.AddGroupingPolicy("bob", "developer");      // → 路由到 casbin_groupings
                enforcer.AddNamedGroupingPolicy("g2", "developer", "role-contributor"); // → 路由到 casbin_roles

                _output.WriteLine("Added policies to in-memory model:");
                _output.WriteLine("  p policy → should go to casbin_policies");
                _output.WriteLine("  g policy → should go to casbin_groupings");
                _output.WriteLine("  g2 policy → should go to casbin_roles");

                // 7. 执行保存
                await enforcer.SavePolicyAsync();
                _output.WriteLine("Called SavePolicyAsync()");

                // 8. 验证不同 Schema 中的数据分布
                var policiesCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.PoliciesSchema);
                var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.GroupingsSchema);
                var rolesCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.RolesSchema);

                _output.WriteLine($"Schema distribution:");
                _output.WriteLine($"  casbin_policies: {policiesCount} policy");
                _output.WriteLine($"  casbin_groupings: {groupingsCount} policy");
                _output.WriteLine($"  casbin_roles: {rolesCount} policy");

                // 核心断言：策略必须被分布到三个不同的 Schema 中
                // 如果 SqlSugar 生成的 SQL 带有正确的 Schema 限定符（如 "casbin_policies"."casbin_rule"），
                // 那么即使连接是共享的，PostgreSQL 也能正确处理。
                
                if (policiesCount == 1 && groupingsCount == 1 && rolesCount == 1)
                {
                    _output.WriteLine("✓✓✓ SHARED CONNECTION TEST PASSED!");
                    _output.WriteLine("SqlSugar correctly distributes policies even with shared connection");
                    _output.WriteLine("CONCLUSION: Schema-qualified table names work correctly; no 'SET search_path' needed.");
                }
                else
                {
                    _output.WriteLine("✗✗✗ SHARED CONNECTION TEST FAILED!");
                    _output.WriteLine($"Expected distribution: (1, 1, 1), Got: ({policiesCount}, {groupingsCount}, {rolesCount})");
                }

                Assert.Equal(1, policiesCount);
                Assert.Equal(1, groupingsCount);
                Assert.Equal(1, rolesCount);

                // 释放客户端
                policyClient.Dispose();
                groupingClient.Dispose();
                roleClient.Dispose();
            }
            finally
            {
                // 9. 释放共享的物理连接
                await sharedConnection.DisposeAsync();
            }
        }

        #endregion
    }
}
