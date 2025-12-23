using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Casbin;
using Casbin.Adapter.SqlSugar.Entities; // 确保指向 SqlSugar 版本的实体
using Npgsql;
using SqlSugar; // 替换 Microsoft.EntityFrameworkCore
using Xunit;
using Xunit.Abstractions;
using Casbin.Adapter.SqlSugar.UnitTest;


// {
//     /// <summary>
//     /// Integration tests verifying transaction integrity guarantees for multi-context scenarios.
//     /// These tests prove that shared DbConnection objects enable atomic transactions across multiple contexts.
//     ///
//     /// IMPORTANT: These tests are excluded from CI/CD via the "Integration" trait.
//     /// Run locally with: dotnet test --filter "Category=Integration"
//     /// </summary>
//     [Trait("Category", "Integration")]
//     [Collection("IntegrationTests")]
//     public class TransactionIntegrityTests : IClassFixture<TransactionIntegrityTestFixture>, IAsyncLifetime
//     {
//         private readonly TransactionIntegrityTestFixture _fixture;
//         private const string ModelPath = "examples/multi_context_model.conf";

//         public TransactionIntegrityTests(TransactionIntegrityTestFixture fixture)
//         {
//             _fixture = fixture;
//         }

//         public Task InitializeAsync() => _fixture.ClearAllPoliciesAsync();

//         public Task DisposeAsync() => _fixture.RunMigrationsAsync();

//         #region Helper: Derived Context Classes

//         /// <summary>
//         /// Derived context for policies schema
//         /// </summary>
//         public class TestCasbinDbContext1 : CasbinDbContext<int>
//         {
//             public TestCasbinDbContext1(
//                 DbContextOptions<CasbinDbContext<int>> options,
//                 string schemaName,
//                 string tableName)
//                 : base(options, schemaName, tableName)
//             {
//             }
//         }

//         /// <summary>
//         /// Derived context for groupings schema
//         /// </summary>
//         public class TestCasbinDbContext2 : CasbinDbContext<int>
//         {
//             public TestCasbinDbContext2(
//                 DbContextOptions<CasbinDbContext<int>> options,
//                 string schemaName,
//                 string tableName)
//                 : base(options, schemaName, tableName)
//             {
//             }
//         }

//         /// <summary>
//         /// Derived context for roles schema
//         /// </summary>
//         public class TestCasbinDbContext3 : CasbinDbContext<int>
//         {
//             public TestCasbinDbContext3(
//                 DbContextOptions<CasbinDbContext<int>> options,
//                 string schemaName,
//                 string tableName)
//                 : base(options, schemaName, tableName)
//             {
//             }
//         }

//         #endregion
#nullable enable

namespace Casbin.Adapter.SqlSugar.UnitTest.Integration
{
    /// <summary>
    /// Integration tests verifying transaction integrity guarantees for multi-context scenarios.
    /// These tests prove that shared DbConnection objects enable atomic transactions across multiple clients.
    ///
    /// IMPORTANT: These tests are excluded from CI/CD via the "Integration" trait.
    /// Run locally with: dotnet test --filter "Category=Integration"
    /// </summary>
    [Trait("Category", "Integration")]
    [Collection("IntegrationTests")]
    public class TransactionIntegrityTests : TestUtil, IClassFixture<TransactionIntegrityTestFixture>, IAsyncLifetime
    {
        private readonly TransactionIntegrityTestFixture _fixture;
        private readonly ITestOutputHelper _output;
        private const string ModelPath = "examples/multi_context_model.conf";

        public TransactionIntegrityTests(TransactionIntegrityTestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        // 初始化和清理逻辑，调用 Fixture 中已实现的 SqlSugar 版本方法
        public Task InitializeAsync() => _fixture.ClearAllPoliciesAsync();

        public Task DisposeAsync() => _fixture.RunMigrationsAsync();
    

        #region Helper: SqlSugar Client Configuration

        /// <summary>
        /// 在 SqlSugar 中，我们不需要像 EFCore 那样为每个 Schema 创建派生类。
        /// 我们可以通过配置不同的 SqlSugarClient 实例来实现相同的效果。
        /// 为了保持测试代码结构的一致性，这里提供一个创建配置的辅助方法。
        /// </summary>
        // private ConnectionConfig CreateSchemaConfig(System.Data.Common.DbConnection sharedConnection, string schemaName)
        private ConnectionConfig CreateSchemaConfig(string schemaName)
        {
            return new ConnectionConfig()
            {
                // SqlSugar 需要 ConnectionString 用于 CodeFirst.InitTables() 等操作
                ConnectionString = _fixture.ConnectionString,
                DbType = DbType.PostgreSQL,
                IsAutoCloseConnection = false,   // 手动管理连接的生命周期
                ConfigureExternalServices = new ConfigureExternalServices
                {
                    EntityService = (c, p) =>
                    {
                        if (p.EntityName == nameof(CasbinRule))
                        {
                            // 动态映射 Schema.TableName
                            p.DbTableName = $"{schemaName}.casbin_rule";
                        }
                    }
                }
            };
        }

        // 如果后续代码中仍需使用特定类型作为标识，可以定义简单的包装类，
        // 但在 SqlSugar 中通常直接使用 ISqlSugarClient。
        public class TestCasbinSqlSugarClient1 : SqlSugarClient { public TestCasbinSqlSugarClient1(ConnectionConfig config) : base(config) { } }
        public class TestCasbinSqlSugarClient2 : SqlSugarClient { public TestCasbinSqlSugarClient2(ConnectionConfig config) : base(config) { } }
        public class TestCasbinSqlSugarClient3 : SqlSugarClient { public TestCasbinSqlSugarClient3(ConnectionConfig config) : base(config) { } }

        #endregion

        #region Helper Methods

        // /// <summary>
        // /// Creates a three-way context provider routing policies to different schemas:
        // /// - p, p2, p3... → policies schema
        // /// - g → groupings schema
        // /// - g2, g3, g4... → roles schema
        // /// </summary>
        // private class ThreeWayPolicyTypeProvider : ICasbinDbContextProvider<int>
        // {
        //     private readonly DbContext _policyContext;
        //     private readonly DbContext _groupingContext;
        //     private readonly DbContext _roleContext;
        //     private readonly System.Data.Common.DbConnection? _sharedConnection;

        //     public ThreeWayPolicyTypeProvider(
        //         DbContext policyContext,
        //         DbContext groupingContext,
        //         DbContext roleContext,
        //         System.Data.Common.DbConnection? sharedConnection)
        //     {
        //         _policyContext = policyContext ?? throw new ArgumentNullException(nameof(policyContext));
        //         _groupingContext = groupingContext ?? throw new ArgumentNullException(nameof(groupingContext));
        //         _roleContext = roleContext ?? throw new ArgumentNullException(nameof(roleContext));
        //         _sharedConnection = sharedConnection;
        //     }

        //     public DbContext GetContextForPolicyType(string policyType)
        //     {
        //         if (string.IsNullOrEmpty(policyType))
        //             return _policyContext;

        //         // Route p policies to policy context
        //         if (policyType.StartsWith("p", StringComparison.OrdinalIgnoreCase))
        //             return _policyContext;

        //         // Route g2+ to role context
        //         if (policyType.StartsWith("g2", StringComparison.OrdinalIgnoreCase) ||
        //             policyType.StartsWith("g3", StringComparison.OrdinalIgnoreCase) ||
        //             policyType.StartsWith("g4", StringComparison.OrdinalIgnoreCase))
        //             return _roleContext;

        //         // Route g to grouping context
        //         return _groupingContext;
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
        // #region Helper Methods

        /// <summary>
        /// 策略类型提供者，将不同的策略路由到不同的 SqlSugar 客户端（对应不同的 Schema）：
        /// - p, p2, p3... → policies schema
        /// - g → groupings schema
        /// - g2, g3, g4... → roles schema
        /// </summary>
        private class ThreeWayPolicyTypeProvider : ISqlSugarClientProvider
        {
            private readonly ISqlSugarClient _policyClient;
            private readonly ISqlSugarClient _groupingClient;
            private readonly ISqlSugarClient _roleClient;
            private readonly System.Data.Common.DbConnection? _sharedConnection;

            public ThreeWayPolicyTypeProvider(
                ISqlSugarClient policyClient,
                ISqlSugarClient groupingClient,
                ISqlSugarClient roleClient,
                System.Data.Common.DbConnection? sharedConnection)
            {
                _policyClient = policyClient ?? throw new ArgumentNullException(nameof(policyClient));
                _groupingClient = groupingClient ?? throw new ArgumentNullException(nameof(groupingClient));
                _roleClient = roleClient ?? throw new ArgumentNullException(nameof(roleClient));
                _sharedConnection = sharedConnection;
            }

            public bool SharesConnection => _sharedConnection != null;

            /// <summary>
            /// 根据策略类型获取对应的 SqlSugar 客户端
            /// </summary>
            public ISqlSugarClient GetClientForPolicyType(string policyType)
            {
                if (string.IsNullOrEmpty(policyType))
                    return _policyClient;

                // 将 p 开头的策略路由到 policy 客户端
                if (policyType.StartsWith("p", StringComparison.OrdinalIgnoreCase))
                    return _policyClient;

                // 将 g2, g3, g4 开头的策略路由到 role 客户端
                if (policyType.StartsWith("g2", StringComparison.OrdinalIgnoreCase) ||
                    policyType.StartsWith("g3", StringComparison.OrdinalIgnoreCase) ||
                    policyType.StartsWith("g4", StringComparison.OrdinalIgnoreCase))
                    return _roleClient;

                // 将 g 路由到 grouping 客户端
                return _groupingClient;
            }

            /// <summary>
            /// 获取所有参与的 SqlSugar 客户端
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
            /// 【集成测试多 Schema 支持 - 2024/12/21 新增】
            /// 根据策略类型返回完全限定的表名称（格式："schema_name.table_name"）。
            /// 
            /// 这是解决多 Schema 分布测试失败的关键方法。
            /// 当 SqlSugarAdapter 执行 Insertable/Deleteable 操作时，会调用此方法获取目标表名，
            /// 然后使用 SqlSugar 的 .AS(tableName) 方法在运行时动态指定表。
            /// 
            /// 路由规则：
            /// - p, p2, p3... 策略  → casbin_policies.casbin_rule
            /// - g 策略             → casbin_groupings.casbin_rule
            /// - g2, g3, g4... 策略 → casbin_roles.casbin_rule
            /// </summary>
            /// <param name="policyType">策略类型标识符，如 "p", "g", "g2" 等</param>
            /// <returns>完全限定的表名（schema.table）；如果返回 null 则使用默认表名</returns>
            public string? GetTableNameForPolicyType(string policyType)
            {
                if (string.IsNullOrEmpty(policyType))
                    return $"{TransactionIntegrityTestFixture.PoliciesSchema}.casbin_rule";

                // 将 p 开头的策略路由到 policies schema
                if (policyType.StartsWith("p", StringComparison.OrdinalIgnoreCase))
                    return $"{TransactionIntegrityTestFixture.PoliciesSchema}.casbin_rule";

                // 将 g2, g3, g4 开头的策略路由到 roles schema
                if (policyType.StartsWith("g2", StringComparison.OrdinalIgnoreCase) ||
                    policyType.StartsWith("g3", StringComparison.OrdinalIgnoreCase) ||
                    policyType.StartsWith("g4", StringComparison.OrdinalIgnoreCase))
                    return $"{TransactionIntegrityTestFixture.RolesSchema}.casbin_rule";

                // 将 g 路由到 groupings schema
                return $"{TransactionIntegrityTestFixture.GroupingsSchema}.casbin_rule";
            }
        }

        // /// <summary>
        // /// Creates an enforcer with three contexts sharing the same DbConnection
        // /// </summary>
        // private async Task<(Enforcer enforcer, NpgsqlConnection connection)> CreateEnforcerWithSharedConnectionAsync()
        // {
        //     // Create ONE shared connection
        //     var connection = new NpgsqlConnection(_fixture.ConnectionString);
        //     await connection.OpenAsync();

        //     // Create three contexts sharing the same connection
        //     var policyContext = CreateContext(connection, TransactionIntegrityTestFixture.PoliciesSchema);
        //     var groupingContext = CreateContext(connection, TransactionIntegrityTestFixture.GroupingsSchema);
        //     var roleContext = CreateContext(connection, TransactionIntegrityTestFixture.RolesSchema);

        //     // Create provider routing policy types to appropriate contexts
        //     var provider = new ThreeWayPolicyTypeProvider(policyContext, groupingContext, roleContext, connection);

        //     // Create adapter and enforcer
        //     var adapter = new EFCoreAdapter<int>(provider);
        //     var enforcer = new Enforcer(ModelPath, adapter);

        //     await enforcer.LoadPolicyAsync();

        //     return (enforcer, connection);
        // }

        // /// <summary>
        // /// Creates an enforcer with three contexts using SEPARATE DbConnections (same connection string)
        // /// This is used to demonstrate non-atomic behavior when connections are not shared.
        // /// </summary>
        // private async Task<(Enforcer enforcer,
        //                      NpgsqlConnection policyConnection,
        //                      NpgsqlConnection groupingConnection,
        //                      NpgsqlConnection roleConnection,
        //                      CasbinDbContext<int> policyContext,
        //                      CasbinDbContext<int> groupingContext,
        //                      CasbinDbContext<int> roleContext)> CreateEnforcerWithSeparateConnectionsAsync()
        // {
        //     // Create THREE separate connections with same connection string
        //     var policyConnection = new NpgsqlConnection(_fixture.ConnectionString);
        //     var groupingConnection = new NpgsqlConnection(_fixture.ConnectionString);
        //     var roleConnection = new NpgsqlConnection(_fixture.ConnectionString);

        //     await policyConnection.OpenAsync();
        //     await groupingConnection.OpenAsync();
        //     await roleConnection.OpenAsync();

        //     // Create three contexts with different connection objects
        //     var policyContext = CreateContext(policyConnection, TransactionIntegrityTestFixture.PoliciesSchema);
        //     var groupingContext = CreateContext(groupingConnection, TransactionIntegrityTestFixture.GroupingsSchema);
        //     var roleContext = CreateContext(roleConnection, TransactionIntegrityTestFixture.RolesSchema);

        //     // Create provider routing policy types to appropriate contexts
        //     // Pass null for shared connection since these contexts use separate connections
        //     var provider = new ThreeWayPolicyTypeProvider(policyContext, groupingContext, roleContext, null);

        //     // Create adapter and enforcer
        //     var adapter = new EFCoreAdapter<int>(provider);
        //     var enforcer = new Enforcer(ModelPath, adapter);

        //     await enforcer.LoadPolicyAsync();

        //     return (enforcer, policyConnection, groupingConnection, roleConnection,
        //             policyContext, groupingContext, roleContext);
        // }

        // private CasbinDbContext<int> CreateContext(NpgsqlConnection connection, string schemaName)
        // {
        //     var options = new DbContextOptionsBuilder<CasbinDbContext<int>>()
        //         .UseNpgsql(connection, b => b.MigrationsHistoryTable("__EFMigrationsHistory", schemaName))
        //         .Options;

        //     // Return appropriate derived context based on schema name
        //     if (schemaName == TransactionIntegrityTestFixture.PoliciesSchema)
        //         return new TestCasbinDbContext1(options, schemaName, "casbin_rule");
        //     else if (schemaName == TransactionIntegrityTestFixture.GroupingsSchema)
        //         return new TestCasbinDbContext2(options, schemaName, "casbin_rule");
        //     else if (schemaName == TransactionIntegrityTestFixture.RolesSchema)
        //         return new TestCasbinDbContext3(options, schemaName, "casbin_rule");
        //     else
        //         throw new ArgumentException($"Unknown schema name: {schemaName}", nameof(schemaName));
        // }


        /// <summary>
        /// 创建一个 Enforcer，其下属的三个 SqlSugar 客户端共享同一个 DbConnection。
        /// 用于验证跨 Schema 的原子事务。
        /// </summary>
        /// <param name="clearPolicy">If true, clears loaded policies to ensure clean test state. Set to false when testing duplicate detection.</param>
        private async Task<(Enforcer enforcer, NpgsqlConnection connection)> CreateEnforcerWithSharedConnectionAsync(bool clearPolicy = true)
        {
            // 1. 创建一个唯一的共享连接
            var connection = new NpgsqlConnection(_fixture.ConnectionString);
            await connection.OpenAsync();

            // 2. 创建三个共享该连接的 SqlSugar 客户端
            var policyClient = CreateClient(connection, TransactionIntegrityTestFixture.PoliciesSchema);
            var groupingClient = CreateClient(connection, TransactionIntegrityTestFixture.GroupingsSchema);
            var roleClient = CreateClient(connection, TransactionIntegrityTestFixture.RolesSchema);

            // 3. 创建 Provider，将策略类型路由到对应的客户端
            var provider = new ThreeWayPolicyTypeProvider(policyClient, groupingClient, roleClient, connection);

            // 4. 创建适配器和 Enforcer
            var adapter = new SqlSugarAdapter(provider);
            var enforcer = new Enforcer(ModelPath, adapter);

            await enforcer.LoadPolicyAsync();
            
            // 【2024/12/21 修复测试隔离问题】清空自动加载的历史数据，确保测试从干净状态开始
            // 但在测试重复检测时需要保留加载的策略
            if (clearPolicy)
            {
                enforcer.ClearPolicy();
            }

            return (enforcer, connection);
        }

        /// <summary>
        /// 创建一个 Enforcer，其下属的三个 SqlSugar 客户端使用独立的 DbConnection（相同的连接字符串）。
        /// 用于演示当连接不共享时，事务无法跨客户端保持原子性。
        /// </summary>
        private async Task<(Enforcer enforcer,
                             NpgsqlConnection policyConnection,
                             NpgsqlConnection groupingConnection,
                             NpgsqlConnection roleConnection,
                             ISqlSugarClient policyClient,
                             ISqlSugarClient groupingClient,
                             ISqlSugarClient roleClient)> CreateEnforcerWithSeparateConnectionsAsync()
        {
            // 1. 创建三个独立的连接对象
            var policyConnection = new NpgsqlConnection(_fixture.ConnectionString);
            var groupingConnection = new NpgsqlConnection(_fixture.ConnectionString);
            var roleConnection = new NpgsqlConnection(_fixture.ConnectionString);

            await policyConnection.OpenAsync();
            await groupingConnection.OpenAsync();
            await roleConnection.OpenAsync();

            // 2. 创建三个使用不同连接对象的客户端
            var policyClient = CreateClient(policyConnection, TransactionIntegrityTestFixture.PoliciesSchema);
            var groupingClient = CreateClient(groupingConnection, TransactionIntegrityTestFixture.GroupingsSchema);
            var roleClient = CreateClient(roleConnection, TransactionIntegrityTestFixture.RolesSchema);

            // 3. 创建 Provider（共享连接传 null，因为它们是独立的）
            var provider = new ThreeWayPolicyTypeProvider(policyClient, groupingClient, roleClient, null);

            // 4. 创建适配器和 Enforcer
            var adapter = new SqlSugarAdapter(provider);
            var enforcer = new Enforcer(ModelPath, adapter);

            await enforcer.LoadPolicyAsync();
            
            // 【2024/12/21 修复测试隔离问题】清空自动加载的历史数据，确保测试从干净状态开始
            enforcer.ClearPolicy();

            return (enforcer, policyConnection, groupingConnection, roleConnection,
                    policyClient, groupingClient, roleClient);
        }

        /// <summary>
        /// 替代 EFCore 的 CreateContext。
        /// 创建一个配置好 Schema 映射和指定连接的 SqlSugarClient。
        /// </summary>
        // private ISqlSugarClient CreateClient(string schemaName)
        private ISqlSugarClient CreateClient(System.Data.Common.DbConnection connection, string schemaName)
        {
            var config = new ConnectionConfig()
            {
                // SqlSugar 需要 ConnectionString 用于 CodeFirst.InitTables() 等操作
                ConnectionString = _fixture.ConnectionString,
                DbType = DbType.PostgreSQL,
                IsAutoCloseConnection = false, // 手动管理连接生命周期
                ConfigureExternalServices = new ConfigureExternalServices
                {
                    EntityService = (c, p) =>
                    {
                        if (p.EntityName ==  "CasbinRule")
                        {
                            // 动态映射：PostgreSQL 中跨 Schema 访问的标准写法 "schema"."table"
                            p.DbTableName = $"{schemaName}.casbin_rule";
                        }
                    }
                }
            };

            // SqlSugar 不需要像 EFCore 那样定义派生类（TestCasbinDbContext1等）
            // 直接返回 SqlSugarClient 实例即可
            // return new SqlSugarClient(config);
            var client = new SqlSugarClient(config);
            client.Ado.Connection = connection; // 关键：在这里注入外部连接
            return client;
        }

        #endregion

        // #region Test 1: Atomicity - Happy Path

        // [Fact]
        // public async Task SavePolicy_WithSharedConnection_ShouldWriteToAllContextsAtomically()
        // {
        //     // Arrange
        //     var (enforcer, connection) = await CreateEnforcerWithSharedConnectionAsync();

        //     try
        //     {
        //         // Add policies that will route to different contexts
        //         await enforcer.AddPolicyAsync("alice", "data1", "read");         // → policies schema (p)
        //         await enforcer.AddGroupingPolicyAsync("alice", "admin");         // → groupings schema (g)
        //         await enforcer.AddNamedGroupingPolicyAsync("g2", "admin", "superuser"); // → roles schema (g2)

        //         // Act
        //         await enforcer.SavePolicyAsync();

        //         // Assert - Verify each schema has exactly the expected policies
        //         var policiesCount = await _fixture.CountPoliciesInSchemaAsync(
        //             TransactionIntegrityTestFixture.PoliciesSchema, "p");
        //         var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(
        //             TransactionIntegrityTestFixture.GroupingsSchema, "g");
        //         var rolesCount = await _fixture.CountPoliciesInSchemaAsync(
        //             TransactionIntegrityTestFixture.RolesSchema, "g2");

        //         Assert.Equal(1, policiesCount);
        //         Assert.Equal(1, groupingsCount);
        //         Assert.Equal(1, rolesCount);
        //     }
        //     finally
        //     {
        //         await connection.CloseAsync();
        //         await connection.DisposeAsync();
        //     }
        // }

        // #endregion
        #region Test 1: Atomicity - Happy Path

        [Fact]
        public async Task SavePolicy_WithSharedConnection_ShouldWriteToAllContextsAtomically()
        {
            // Arrange
            // 使用之前转换好的辅助方法创建 Enforcer（已配置 SqlSugarAdapter）和共享连接
            var (enforcer, connection) = await CreateEnforcerWithSharedConnectionAsync();

            try
            {
                // 添加不同类型的策略，这些策略会根据路由规则进入不同的 SqlSugar 客户端内存中
                await enforcer.AddPolicyAsync("alice", "data1", "read");         // → 路由到 policies schema (p)
                await enforcer.AddGroupingPolicyAsync("alice", "admin");         // → 路由到 groupings schema (g)
                await enforcer.AddNamedGroupingPolicyAsync("g2", "admin", "superuser"); // → 路由到 roles schema (g2)

                // Act
                // 在 SqlSugarAdapter 内部，SavePolicyAsync 应该检测到共享连接，
                // 开启事务，并依次清空/写入三个 Schema 的表。
                await enforcer.SavePolicyAsync();

                // Assert - 验证每个 Schema 中是否确实写入了预期的策略数量
                // 注意：_fixture.CountPoliciesInSchemaAsync 内部应已适配 SqlSugar 查询逻辑
                var policiesCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.PoliciesSchema, "p");
                var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.GroupingsSchema, "g");
                var rolesCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.RolesSchema, "g2");

                Assert.Equal(1, policiesCount);
                Assert.Equal(1, groupingsCount);
                Assert.Equal(1, rolesCount);
            }
            finally
            {
                // 显式关闭并释放共享连接，防止连接泄露
                if (connection.State != System.Data.ConnectionState.Closed)
                {
                    await connection.CloseAsync();
                }
                await connection.DisposeAsync();
            }
        }

        #endregion

        // #region Test 2: Connection Sharing Verification

        // [Fact]
        // public async Task MultiContextSetup_WithSharedConnection_ShouldShareSamePhysicalConnection()
        // {
        //     // Arrange
        //     var connection = new NpgsqlConnection(_fixture.ConnectionString);
        //     await connection.OpenAsync();

        //     try
        //     {
        //         var policyContext = CreateContext(connection, TransactionIntegrityTestFixture.PoliciesSchema);
        //         var groupingContext = CreateContext(connection, TransactionIntegrityTestFixture.GroupingsSchema);
        //         var roleContext = CreateContext(connection, TransactionIntegrityTestFixture.RolesSchema);

        //         // Act & Assert - Verify reference equality (not just connection string equality)
        //         var policyConn = policyContext.Database.GetDbConnection();
        //         var groupingConn = groupingContext.Database.GetDbConnection();
        //         var roleConn = roleContext.Database.GetDbConnection();

        //         Assert.Same(connection, policyConn);
        //         Assert.Same(connection, groupingConn);
        //         Assert.Same(connection, roleConn);
        //         Assert.Same(policyConn, groupingConn);
        //         Assert.Same(groupingConn, roleConn);
        //     }
        //     finally
        //     {
        //         await connection.CloseAsync();
        //         await connection.DisposeAsync();
        //     }
        // }

        // #endregion

        #region Test 2: Connection Sharing Verification

        [Fact]
        public async Task MultiContextSetup_WithSharedConnection_ShouldShareSamePhysicalConnection()
        {
            // Arrange
            // 创建一个唯一的物理连接
            var connection = new NpgsqlConnection(_fixture.ConnectionString);
            await connection.OpenAsync();

            try
            {
                // 使用之前定义的 CreateClient 辅助方法创建三个客户端
                // 内部通过 ConnectionConfig.DbConnection 注入了同一个 connection 对象
                var policyClient = CreateClient(connection, TransactionIntegrityTestFixture.PoliciesSchema);
                var groupingClient = CreateClient(connection, TransactionIntegrityTestFixture.GroupingsSchema);
                var roleClient = CreateClient(connection, TransactionIntegrityTestFixture.RolesSchema);

                // Act & Assert - 验证引用一致性（Reference Equality）
                // SqlSugar 通过 Ado.Connection 暴露底层的 DbConnection
                var policyConn = policyClient.Ado.Connection;
                var groupingConn = groupingClient.Ado.Connection;
                var roleConn = roleClient.Ado.Connection;

                // 验证所有客户端持有的连接对象与原始连接对象是同一个实例
                Assert.Same(connection, policyConn);
                Assert.Same(connection, groupingConn);
                Assert.Same(connection, roleConn);
                
                // 验证客户端之间的连接对象也是同一个实例
                Assert.Same(policyConn, groupingConn);
                Assert.Same(groupingConn, roleConn);
                
                _output.WriteLine("Verified: All SqlSugar clients share the exact same physical DbConnection instance.");
            }
            finally
            {
                // 显式关闭并释放连接
                if (connection.State != System.Data.ConnectionState.Closed)
                {
                    await connection.CloseAsync();
                }
                await connection.DisposeAsync();
            }
        }

        #endregion

        // #region Test 3: Rollback - Missing Table (CRITICAL TEST)

        // [Fact]
        // public async Task SavePolicy_WhenTableDroppedInOneContext_ShouldRollbackAllContexts()
        // {
        //     // Arrange
        //     var (enforcer, connection) = await CreateEnforcerWithSharedConnectionAsync();

        //     try
        //     {
        //         // Disable AutoSave so policies stay in-memory until SavePolicyAsync() is called
        //         enforcer.EnableAutoSave(false);

        //         // Add policies to all contexts (in memory only, AutoSave is OFF)
        //         await enforcer.AddPolicyAsync("alice", "data1", "read");           // → policies schema
        //         await enforcer.AddGroupingPolicyAsync("alice", "admin");            // → groupings schema
        //         await enforcer.AddNamedGroupingPolicyAsync("g2", "admin", "superuser"); // → roles schema

        //         // Drop table in roles schema AFTER policies are in memory but BEFORE SavePolicy
        //         // This simulates a catastrophic failure scenario where database schema is inconsistent
        //         await _fixture.DropTableAsync(TransactionIntegrityTestFixture.RolesSchema);

        //         Exception? caughtException = null;

        //         // Act - Try to save, should throw due to missing table in roles schema
        //         try
        //         {
        //             await enforcer.SavePolicyAsync();
        //         }
        //         catch (Exception ex)
        //         {
        //             caughtException = ex;
        //         }

        //         // Assert - Verify exception was thrown
        //         Assert.NotNull(caughtException);

        //         // Recreate table for verification queries
        //         await _fixture.RunMigrationsAsync();

        //         // CRITICAL ASSERTION - Verify ZERO policies in all contexts (rollback successful)
        //         // This proves that when one context fails, ALL contexts roll back atomically
        //         var policiesCount = await _fixture.CountPoliciesInSchemaAsync(
        //             TransactionIntegrityTestFixture.PoliciesSchema, "p");
        //         var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(
        //             TransactionIntegrityTestFixture.GroupingsSchema, "g");
        //         var rolesCount = await _fixture.CountPoliciesInSchemaAsync(
        //             TransactionIntegrityTestFixture.RolesSchema, "g2");

        //         // Verify atomicity: All contexts rolled back (no partial commits)
        //         Assert.Equal(0, policiesCount);   // Should be 0 (rolled back)
        //         Assert.Equal(0, groupingsCount);  // Should be 0 (rolled back)
        //         Assert.Equal(0, rolesCount);      // Should be 0 (rolled back)

        //         // If we got here, atomicity is PROVEN!
        //     }
        //     finally
        //     {
        //         await connection.CloseAsync();
        //         await connection.DisposeAsync();

        //         // Restore table for subsequent tests
        //         await _fixture.RunMigrationsAsync();
        //     }
        // }

        // #endregion

        #region Test 3: Rollback - Missing Table (CRITICAL TEST)

        [Fact]
        public async Task SavePolicy_WhenTableDroppedInOneContext_ShouldRollbackAllContexts()
        {
            // Arrange
            // 创建共享连接的 Enforcer，此时内部使用的是 SqlSugarAdapter
            var (enforcer, connection) = await CreateEnforcerWithSharedConnectionAsync();

            try
            {
                // 禁用自动保存，使策略保留在内存中，直到调用 SavePolicyAsync()
                enforcer.EnableAutoSave(false);

                // 添加策略到各个上下文（此时仅在内存中，因为 AutoSave 已关闭）
                await enforcer.AddPolicyAsync("alice", "data1", "read");           // → 路由到 policies schema
                await enforcer.AddGroupingPolicyAsync("alice", "admin");            // → 路由到 groupings schema
                await enforcer.AddNamedGroupingPolicyAsync("g2", "admin", "superuser"); // → 路由到 roles schema

                // 在内存中有数据但尚未调用 SavePolicy 之前，手动删除 roles schema 中的表
                // 这模拟了数据库架构不一致导致的灾难性故障场景
                await _fixture.DropTableAsync(TransactionIntegrityTestFixture.RolesSchema);

                Exception? caughtException = null;

                // Act - 尝试保存。由于 roles 表已不存在，这里应该抛出异常
                try
                {
                    // 在 SqlSugarAdapter 内部，SavePolicyAsync 应该：
                    // 1. 开启基于共享连接的事务
                    // 2. 尝试清空并写入 policies 表 (成功)
                    // 3. 尝试清空并写入 groupings 表 (成功)
                    // 4. 尝试清空并写入 roles 表 (失败，抛出异常)
                    // 5. 触发 Catch 块执行 Rollback
                    await enforcer.SavePolicyAsync();
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }

                // Assert - 验证是否捕获到了预期的异常（如 PostgresException: table does not exist）
                Assert.NotNull(caughtException);

                // 为了能够执行后续的查询验证，我们需要恢复被删除的表结构
                await _fixture.RunMigrationsAsync();

                // 核心断言 - 验证所有 Schema 中的策略数量是否都为 0（回滚成功）
                // 这证明了即使前两个操作在技术上是成功的，但由于第三个操作失败，全局事务执行了原子回滚
                var policiesCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.PoliciesSchema, "p");
                var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.GroupingsSchema, "g");
                var rolesCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.RolesSchema, "g2");

                // 验证原子性：所有上下文都已回滚，没有产生部分提交
                Assert.Equal(0, policiesCount);   // 预期为 0
                Assert.Equal(0, groupingsCount);  // 预期为 0
                Assert.Equal(0, rolesCount);      // 预期为 0

                _output.WriteLine("✓ CRITICAL TEST PASSED: SqlSugarAdapter successfully rolled back all schemas atomically.");
            }
            finally
            {
                // 显式关闭并释放共享连接
                if (connection.State != System.Data.ConnectionState.Closed)
                {
                    await connection.CloseAsync();
                }
                await connection.DisposeAsync();

                // 确保为后续的其他测试恢复数据库表结构
                await _fixture.RunMigrationsAsync();
            }
        }

        #endregion

        // #region Test 4: Rollback - Missing Table

        // [Fact]
        // public async Task SavePolicy_WhenTableMissingInOneContext_ShouldRollbackAllContexts()
        // {
        //     // Arrange
        //     var (enforcer, connection) = await CreateEnforcerWithSharedConnectionAsync();

        //     try
        //     {
        //         // Disable AutoSave so policies stay in-memory until SavePolicyAsync() is called
        //         enforcer.EnableAutoSave(false);

        //         // Add policies to all contexts
        //         await enforcer.AddPolicyAsync("alice", "data1", "read");
        //         await enforcer.AddGroupingPolicyAsync("alice", "admin");
        //         await enforcer.AddNamedGroupingPolicyAsync("g2", "admin", "superuser");

        //         // Drop table in roles schema AFTER enforcer is created
        //         await _fixture.DropTableAsync(TransactionIntegrityTestFixture.RolesSchema);

        //         Exception? caughtException = null;

        //         // Act - Try to save, should throw due to missing table
        //         try
        //         {
        //             await enforcer.SavePolicyAsync();
        //         }
        //         catch (Exception ex)
        //         {
        //             caughtException = ex;
        //         }

        //         // Assert - Verify exception was thrown
        //         Assert.NotNull(caughtException);

        //         // Recreate table for verification queries
        //         await _fixture.RunMigrationsAsync();

        //         // CRITICAL ASSERTION - Verify ZERO policies in all contexts (rollback successful)
        //         var policiesCount = await _fixture.CountPoliciesInSchemaAsync(
        //             TransactionIntegrityTestFixture.PoliciesSchema, "p");
        //         var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(
        //             TransactionIntegrityTestFixture.GroupingsSchema, "g");
        //         var rolesCount = await _fixture.CountPoliciesInSchemaAsync(
        //             TransactionIntegrityTestFixture.RolesSchema, "g2");

        //         Assert.Equal(0, policiesCount);
        //         Assert.Equal(0, groupingsCount);
        //         Assert.Equal(0, rolesCount);
        //     }
        //     finally
        //     {
        //         await connection.CloseAsync();
        //         await connection.DisposeAsync();

        //         // Restore table for subsequent tests
        //         await _fixture.RunMigrationsAsync();
        //     }
        // }

        // #endregion
        #region Test 4: Rollback - Missing Table (Verification of Dynamic Failure)

        [Fact]
        public async Task SavePolicy_WhenTableMissingInOneContext_ShouldRollbackAllContexts()
        {
            // Arrange
            // 使用共享连接创建 Enforcer 和 SqlSugar 适配器
            var (enforcer, connection) = await CreateEnforcerWithSharedConnectionAsync();

            try
            {
                // 禁用自动保存，确保策略仅存在于内存中
                enforcer.EnableAutoSave(false);

                // 添加策略到各个上下文（内存操作）
                await enforcer.AddPolicyAsync("alice", "data1", "read");
                await enforcer.AddGroupingPolicyAsync("alice", "admin");
                await enforcer.AddNamedGroupingPolicyAsync("g2", "admin", "superuser");

                // 在 Enforcer 创建之后、SavePolicy 调用之前，删除 roles schema 中的表
                // 模拟运行时的数据库结构损坏或权限丢失
                await _fixture.DropTableAsync(TransactionIntegrityTestFixture.RolesSchema);

                Exception? caughtException = null;

                // Act - 尝试保存，由于 roles 表缺失，预期会抛出异常
                try
                {
                    await enforcer.SavePolicyAsync();
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }

                // Assert - 验证异常是否被捕获
                Assert.NotNull(caughtException);

                // 恢复表结构，以便能够执行查询来验证回滚结果
                await _fixture.RunMigrationsAsync();

                // 核心验证：验证所有 Schema 中的策略数量是否都为 0
                // 如果事务工作正常，即使 policies 和 groupings 表的操作在技术上先执行且成功了，
                // 也会因为最后的 roles 表操作失败而全部回滚。
                var policiesCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.PoliciesSchema, "p");
                var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.GroupingsSchema, "g");
                var rolesCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.RolesSchema, "g2");

                // 验证原子性：所有上下文都已回滚
                Assert.Equal(0, policiesCount);
                Assert.Equal(0, groupingsCount);
                Assert.Equal(0, rolesCount);
                
                _output.WriteLine("✓ Test 4 Passed: Atomic rollback confirmed for dynamic table loss.");
            }
            finally
            {
                // 显式关闭共享连接
                if (connection.State != System.Data.ConnectionState.Closed)
                {
                    await connection.CloseAsync();
                }
                await connection.DisposeAsync();

                // 恢复环境，确保不影响其他测试用例
                await _fixture.RunMigrationsAsync();
            }
        }

        #endregion

        // #region Test 5: Consistency Verification

        // [Fact]
        // public async Task MultipleSaveOperations_WithSharedConnection_ShouldMaintainDataConsistency()
        // {
        //     // Arrange
        //     var (enforcer, connection) = await CreateEnforcerWithSharedConnectionAsync();

        //     try
        //     {
        //         // Act - Perform multiple incremental saves
        //         // Save 1
        //         await enforcer.AddPolicyAsync("alice", "data1", "read");
        //         await enforcer.AddGroupingPolicyAsync("alice", "admin");
        //         await enforcer.SavePolicyAsync();

        //         // Save 2
        //         await enforcer.AddPolicyAsync("bob", "data2", "write");
        //         await enforcer.AddGroupingPolicyAsync("bob", "user");
        //         await enforcer.SavePolicyAsync();

        //         // Save 3
        //         await enforcer.AddPolicyAsync("charlie", "data3", "read");
        //         await enforcer.AddGroupingPolicyAsync("charlie", "user");
        //         await enforcer.SavePolicyAsync();

        //         // Assert - Verify all 6 policies present (3 p policies, 3 g policies)
        //         var policiesCount = await _fixture.CountPoliciesInSchemaAsync(
        //             TransactionIntegrityTestFixture.PoliciesSchema, "p");
        //         var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(
        //             TransactionIntegrityTestFixture.GroupingsSchema, "g");

        //         Assert.Equal(3, policiesCount);
        //         Assert.Equal(3, groupingsCount);

        //         // Verify all policies are enforced correctly
        //         Assert.True(await enforcer.EnforceAsync("alice", "data1", "read"));
        //         Assert.True(await enforcer.EnforceAsync("bob", "data2", "write"));
        //         Assert.True(await enforcer.EnforceAsync("charlie", "data3", "read"));
        //     }
        //     finally
        //     {
        //         await connection.CloseAsync();
        //         await connection.DisposeAsync();
        //     }
        // }

        // #endregion

        #region Test 5: Consistency Verification

        [Fact]
        public async Task MultipleSaveOperations_WithSharedConnection_ShouldMaintainDataConsistency()
        {
            // Arrange
            // 使用共享连接创建 Enforcer 和 SqlSugar 适配器
            var (enforcer, connection) = await CreateEnforcerWithSharedConnectionAsync();

            try
            {
                // Act - 执行多次增量保存操作
                
                // 第一次保存：添加 alice 的策略
                await enforcer.AddPolicyAsync("alice", "data1", "read");
                await enforcer.AddGroupingPolicyAsync("alice", "admin");
                await enforcer.SavePolicyAsync();

                // 第二次保存：添加 bob 的策略
                await enforcer.AddPolicyAsync("bob", "data2", "write");
                await enforcer.AddGroupingPolicyAsync("bob", "user");
                await enforcer.SavePolicyAsync();

                // 第三次保存：添加 charlie 的策略
                await enforcer.AddPolicyAsync("charlie", "data3", "read");
                await enforcer.AddGroupingPolicyAsync("charlie", "user");
                await enforcer.SavePolicyAsync();

                // Assert - 验证数据库中的最终数据量
                // 预期：3 条 p 策略分布在 policies schema，3 条 g 策略分布在 groupings schema
                var policiesCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.PoliciesSchema, "p");
                var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.GroupingsSchema, "g");

                Assert.Equal(3, policiesCount);
                Assert.Equal(3, groupingsCount);

                // 验证 Enforcer 的内存状态和权限判定逻辑是否正确
                // 这确保了适配器在 Save 之后 Load 的数据是完整的
                Assert.True(await enforcer.EnforceAsync("alice", "data1", "read"));
                Assert.True(await enforcer.EnforceAsync("bob", "data2", "write"));
                Assert.True(await enforcer.EnforceAsync("charlie", "data3", "read"));
                
                _output.WriteLine("✓ Test 5 Passed: Data consistency maintained across multiple SavePolicy operations.");
            }
            finally
            {
                // 显式关闭并释放共享连接
                if (connection.State != System.Data.ConnectionState.Closed)
                {
                    await connection.CloseAsync();
                }
                await connection.DisposeAsync();
            }
        }

        #endregion

        // #region Test 6: Non-Atomic Behavior Without Shared Connection

        // [Fact]
        // public async Task SavePolicy_WithSeparateConnections_ShouldNotBeAtomic()
        // {
        //     // Arrange - Create enforcer with SEPARATE connection objects
        //     var (enforcer, policyConnection, groupingConnection, roleConnection,
        //          policyContext, groupingContext, roleContext) = await CreateEnforcerWithSeparateConnectionsAsync();

        //     try
        //     {
        //         // Add policies to all contexts
        //         await enforcer.AddPolicyAsync("alice", "data1", "read");
        //         await enforcer.AddGroupingPolicyAsync("alice", "admin");
        //         await enforcer.AddNamedGroupingPolicyAsync("g2", "admin", "superuser");

        //         // Drop table in roles schema to force failure
        //         await _fixture.DropTableAsync(TransactionIntegrityTestFixture.RolesSchema);

        //         Exception? caughtException = null;

        //         // Act - Try to save, should throw due to missing table in roles schema
        //         try
        //         {
        //             await enforcer.SavePolicyAsync();
        //         }
        //         catch (Exception ex)
        //         {
        //             caughtException = ex;
        //         }

        //         // Assert - Verify exception was thrown
        //         Assert.NotNull(caughtException);

        //         // Recreate table for verification queries
        //         await _fixture.RunMigrationsAsync();

        //         // CRITICAL ASSERTION - Verify policies WERE written to functioning contexts (NOT atomic!)
        //         var policiesCount = await _fixture.CountPoliciesInSchemaAsync(
        //             TransactionIntegrityTestFixture.PoliciesSchema, "p");
        //         var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(
        //             TransactionIntegrityTestFixture.GroupingsSchema, "g");
        //         var rolesCount = await _fixture.CountPoliciesInSchemaAsync(
        //             TransactionIntegrityTestFixture.RolesSchema, "g2");

        //         // This test DOCUMENTS the non-atomic behavior without shared connections
        //         // Policies and groupings were committed despite roles context failure
        //         Assert.Equal(1, policiesCount);   // Written (NOT rolled back)
        //         Assert.Equal(1, groupingsCount);  // Written (NOT rolled back)
        //         Assert.Equal(0, rolesCount);      // Failed to write (table dropped)

        //         // This proves that connection string matching alone is INSUFFICIENT for atomicity
        //         // Must use shared DbConnection OBJECT for atomic transactions
        //     }
        //     finally
        //     {
        //         await policyContext.DisposeAsync();
        //         await groupingContext.DisposeAsync();
        //         await roleContext.DisposeAsync();
        //         await policyConnection.DisposeAsync();
        //         await groupingConnection.DisposeAsync();
        //         await roleConnection.DisposeAsync();
        //     }
        // }

        // #endregion

        #region Test 6: Non-Atomic Behavior Without Shared Connection

        [Fact]
        public async Task SavePolicy_WithSeparateConnections_ShouldNotBeAtomic()
        {
            // Arrange - 创建一个使用三个独立连接对象的 Enforcer
            // 使用之前转换好的辅助方法 CreateEnforcerWithSeparateConnectionsAsync
            var (enforcer, policyConnection, groupingConnection, roleConnection,
                 policyClient, groupingClient, roleClient) = await CreateEnforcerWithSeparateConnectionsAsync();

            try
            {
                // 添加策略到各个上下文的内存中
                await enforcer.AddPolicyAsync("alice", "data1", "read");
                await enforcer.AddGroupingPolicyAsync("alice", "admin");
                await enforcer.AddNamedGroupingPolicyAsync("g2", "admin", "superuser");

                // 在保存之前，手动删除 roles schema 中的表以制造故障
                await _fixture.DropTableAsync(TransactionIntegrityTestFixture.RolesSchema);

                Exception? caughtException = null;

                // Act - 尝试保存。由于 roles 表缺失，预期会抛出异常
                try
                {
                    // 在 SqlSugarAdapter 内部，由于没有共享连接：
                    // 1. policyClient 执行清空和写入 (成功并立即提交)
                    // 2. groupingClient 执行清空和写入 (成功并立即提交)
                    // 3. roleClient 执行操作 (失败，抛出异常)
                    await enforcer.SavePolicyAsync();
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }

                // Assert - 验证异常是否被捕获
                Assert.NotNull(caughtException);

                // 【2024/12/22 修复】只恢复 roles 表结构，不要动 policies 和 groupings
                // RunMigrationsAsync 会 DROP 并重建所有表，导致已提交的数据丢失
                using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
                {
                    await conn.OpenAsync();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = $@"
                        CREATE TABLE IF NOT EXISTS {TransactionIntegrityTestFixture.RolesSchema}.casbin_rule (
                            id SERIAL PRIMARY KEY,
                            ptype VARCHAR(254) NOT NULL,
                            v0 VARCHAR(254), v1 VARCHAR(254), v2 VARCHAR(254),
                            v3 VARCHAR(254), v4 VARCHAR(254), v5 VARCHAR(254)
                        )";
                    await cmd.ExecuteNonQueryAsync();
                }

                // 核心断言 - 验证数据是否产生了“部分提交”（非原子性）
                // 在没有共享连接的情况下，前两个成功的操作不会被回滚
                var policiesCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.PoliciesSchema, "p");
                var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.GroupingsSchema, "g");
                var rolesCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.RolesSchema, "g2");

                // 此测试记录了非原子行为：
                // 尽管 roles 操作失败了，但 policies 和 groupings 的数据已经被持久化了
                Assert.Equal(1, policiesCount);   // 已写入 (未回滚)
                Assert.Equal(1, groupingsCount);  // 已写入 (未回滚)
                Assert.Equal(0, rolesCount);      // 写入失败 (表已删除)

                _output.WriteLine("✓ Test 6 Passed: Non-atomic behavior confirmed for separate connections.");
                _output.WriteLine("This proves that a shared DbConnection OBJECT is mandatory for cross-schema atomicity.");
            }
            finally
            {
                // 释放所有 SqlSugar 客户端
                policyClient.Dispose();
                groupingClient.Dispose();
                roleClient.Dispose();

                // 释放所有独立的物理连接
                await policyConnection.DisposeAsync();
                await groupingConnection.DisposeAsync();
                await roleConnection.DisposeAsync();
            }
        }

        #endregion

    //     #region Test 7: Casbin In-Memory vs Database State

    //     [Fact]
    //     public async Task SavePolicy_ShouldReflectDatabaseStateNotCasbinMemory()
    //     {
    //         // Arrange - Create first enforcer and save policies
    //         var (enforcer1, connection1) = await CreateEnforcerWithSharedConnectionAsync();

    //         try
    //         {
    //             await enforcer1.AddPolicyAsync("alice", "data1", "read");
    //             await enforcer1.AddGroupingPolicyAsync("alice", "admin");
    //             await enforcer1.SavePolicyAsync();
    //         }
    //         finally
    //         {
    //             await connection1.CloseAsync();
    //             await connection1.DisposeAsync();
    //         }

    //         // Create second enforcer - loads existing policies from database
    //         var (enforcer2, connection2) = await CreateEnforcerWithSharedConnectionAsync();

    //         try
    //         {
    //             // Act - Try to add same policies again
    //             var addedPolicy = await enforcer2.AddPolicyAsync("alice", "data1", "read");
    //             var addedGrouping = await enforcer2.AddGroupingPolicyAsync("alice", "admin");

    //             // Assert - Casbin's in-memory check should prevent duplicates
    //             Assert.False(addedPolicy);
    //             Assert.False(addedGrouping);

    //             // Verify database unchanged (validates tests check database, not just Casbin memory)
    //             var policiesCount = await _fixture.CountPoliciesInSchemaAsync(
    //                 TransactionIntegrityTestFixture.PoliciesSchema, "p");
    //             var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(
    //                 TransactionIntegrityTestFixture.GroupingsSchema, "g");

    //             Assert.Equal(1, policiesCount);
    //             Assert.Equal(1, groupingsCount);
    //         }
    //         finally
    //         {
    //             await connection2.CloseAsync();
    //             await connection2.DisposeAsync();
    //         }
    //     }

    //     #endregion
        #region Test 7: Casbin In-Memory vs Database State

        [Fact]
        public async Task SavePolicy_ShouldReflectDatabaseStateNotCasbinMemory()
        {
            // Arrange - 第一阶段：创建第一个 Enforcer 并持久化一些策略
            // 使用共享连接创建第一个 Enforcer（内部使用 SqlSugarAdapter）
            var (enforcer1, connection1) = await CreateEnforcerWithSharedConnectionAsync();

            try
            {
                await enforcer1.AddPolicyAsync("alice", "data1", "read");
                await enforcer1.AddGroupingPolicyAsync("alice", "admin");
                // 触发 SqlSugarAdapter 将内存数据全量同步到数据库
                await enforcer1.SavePolicyAsync();
            }
            finally
            {
                // 彻底关闭第一个连接，确保环境隔离
                if (connection1.State != System.Data.ConnectionState.Closed)
                {
                    await connection1.CloseAsync();
                }
                await connection1.DisposeAsync();
            }

            // 第二阶段：创建第二个 Enforcer - 它会从数据库重新加载刚才保存的策略
            // Pass clearPolicy: false to keep loaded policies for duplicate detection
            var (enforcer2, connection2) = await CreateEnforcerWithSharedConnectionAsync(clearPolicy: false);

            try
            {
                // Act - 尝试再次添加完全相同的策略
                // 由于 enforcer2 在初始化时通过 SqlSugarAdapter.LoadPolicy 加载了数据，
                // Casbin 的内存检查应该能识别出这些策略已存在。
                var addedPolicy = await enforcer2.AddPolicyAsync("alice", "data1", "read");
                var addedGrouping = await enforcer2.AddGroupingPolicyAsync("alice", "admin");

                // Assert - Casbin 的内存检查应该返回 false（表示未添加重复项）
                Assert.False(addedPolicy);
                Assert.False(addedGrouping);

                // 验证数据库状态未发生变化（依然只有最初的那 1 条 p 和 1 条 g）
                // 这步验证了测试是在检查真实的数据库，而不仅仅是 Casbin 的内存缓存
                var policiesCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.PoliciesSchema, "p");
                var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.GroupingsSchema, "g");

                Assert.Equal(1, policiesCount);
                Assert.Equal(1, groupingsCount);
                
                _output.WriteLine("✓ Test 7 Passed: Casbin memory correctly reflects SqlSugar-persisted state.");
            }
            finally
            {
                // 释放第二个连接
                if (connection2.State != System.Data.ConnectionState.Closed)
                {
                    await connection2.CloseAsync();
                }
                await connection2.DisposeAsync();
            }
        }
        #endregion
    }
}