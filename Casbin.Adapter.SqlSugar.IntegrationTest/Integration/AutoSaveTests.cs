using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Casbin.Model;
using Casbin.Adapter.SqlSugar.Entities; // 确保指向 SqlSugar 版本的实体
using SqlSugar; // 替换 Microsoft.EntityFrameworkCore
using Xunit;
using Xunit.Abstractions;
using Casbin.Adapter.SqlSugar;
using Casbin.Adapter.SqlSugar.UnitTest;
using Npgsql;

#nullable enable

namespace Casbin.Adapter.SqlSugar.UnitTest.Integration
{
    /// <summary>
    /// SqlSugar 不像 EFCore 需要继承 DbContext。
    /// 这里我们保留这些类名，以便在集成测试中区分不同的配置（Schema 和 Table）。
    /// </summary>
    public abstract class TestCasbinSqlSugarContext
    {
        public string SchemaName { get; }
        public string TableName { get; }

        protected TestCasbinSqlSugarContext(string schemaName, string tableName)
        {
            SchemaName = schemaName;
            TableName = tableName;
        }

        /// <summary>
        /// 获取该上下文对应的 SqlSugar ConnectionConfig
        /// </summary>
        public ConnectionConfig GetConfig(string connectionString, DbType dbType)
        {
            return new ConnectionConfig()
            {
                ConnectionString = connectionString,
                DbType = dbType,
                IsAutoCloseConnection = true,
                ConfigureExternalServices = new ConfigureExternalServices
                {
                    // SqlSugar 处理动态表名和 Schema 的核心逻辑
                    EntityService = (c, p) =>
                    {
                        if (p.EntityName == nameof(CasbinRule))
                        {
                            // 如果有 Schema，SqlSugar 通常格式为 "Schema.TableName"
                            p.DbTableName = string.IsNullOrEmpty(SchemaName) 
                                ? TableName 
                                : $"{SchemaName}.{TableName}";
                        }
                    }
                }
            };
        }
    }

    public class TestCasbinDbContext1 : TestCasbinSqlSugarContext
    {
        public TestCasbinDbContext1(string schemaName, string tableName)
            : base(schemaName, tableName)
        {
        }
    }

    public class TestCasbinDbContext2 : TestCasbinSqlSugarContext
    {
        public TestCasbinDbContext2(string schemaName, string tableName)
            : base(schemaName, tableName)
        {
        }
    }

    public class TestCasbinDbContext3 : TestCasbinSqlSugarContext
    {
        public TestCasbinDbContext3(string schemaName, string tableName)
            : base(schemaName, tableName)
        {
        }
    }


    /// <summary>
    /// Integration tests for AutoSave behavior using PostgreSQL.
    /// These tests verify that the adapter correctly handles AutoSave ON and OFF modes
    /// when working with both regular policies and grouping policies.
    ///
    /// Note: These are integration tests (not unit tests) because they:
    /// - Use the full Casbin Enforcer (not just the adapter in isolation)
    /// - Test the interaction between Enforcer and Adapter
    /// - Use real PostgreSQL database (not SQLite in-memory)
    /// </summary>
    

    [Trait("Category", "Integration")]
    [Collection("IntegrationTests")]
    public class AutoSaveTests : TestUtil, IAsyncLifetime
    {
        // 注意：这里的 Fixture 类型名称建议保持一致，但其内部实现应已改为 SqlSugar
        private readonly TransactionIntegrityTestFixture _fixture;
        private readonly ITestOutputHelper _output;
        private const string ModelPath = "examples/multi_context_model.conf";

        public AutoSaveTests(TransactionIntegrityTestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        public async Task InitializeAsync()
        {
            // 在每个测试开始前清空所有策略
            // 在 SqlSugar 实现中，这通常对应 db.Deleteable<CasbinRule>().ExecuteCommandAsync()
            await _fixture.ClearAllPoliciesAsync();
        }

        public async Task DisposeAsync()
        {
            // 恢复在测试执行期间可能被删除或修改的表结构
            // 在 SqlSugar 中，这对应 db.CodeFirst.InitTables<CasbinRule>()
            // 我们保留 RunMigrationsAsync 这个名字以兼容原有的测试流程
            await _fixture.RunMigrationsAsync();
        }


        /// <summary>
        /// Tests regular policies with AutoSave ON (default behavior).
        /// Verifies that operations immediately persist to the database.
        /// </summary>
        [Fact]
        public async Task TestPolicyAutoSaveOn()
        {
            // SqlSugar 不需要像 EFCore 那样显式管理 NpgsqlConnection 的生命周期来传递给 Context
            // 我们直接通过 Fixture 获取配置并创建 SqlSugarClient
            var config = new ConnectionConfig()
            {
                ConnectionString = _fixture.ConnectionString,
                DbType = DbType.PostgreSQL,
                IsAutoCloseConnection = true,
                ConfigureExternalServices = new ConfigureExternalServices
                {
                    EntityService = (c, p) =>
                    {
                        // 映射 Schema 和表名，模拟 EFCore 的 MigrationsHistoryTable 和 Schema 配置
                        if (p.EntityName == nameof(CasbinRule))
                        {
                            p.DbTableName = $"{TransactionIntegrityTestFixture.PoliciesSchema}.{nameof(CasbinRule)}";
                        }
                    }
                }
            };

            using var db = new SqlSugarClient(config);
            
            // 初始化数据库表结构和初始数据
            // 注意：InitPolicyAsync 需要在你的 TestUtil 或本类中重写，接收 ISqlSugarClient
            await InitPolicyAsync(db);

            // 使用 SqlSugar 适配器
            var adapter = new SqlSugarAdapter(db);
            var model = DefaultModel.CreateFromText(System.IO.File.ReadAllText("examples/rbac_model.conf"));
            var enforcer = new Enforcer(model, adapter);

            #region Load policy test
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("data2_admin", "data2", "read"),
                AsList("data2_admin", "data2", "write")
            ));
            // EFCore: context.Policies.AsNoTracking().CountAsync()
            // SqlSugar: db.Queryable<CasbinRule>().CountAsync()
            Assert.Equal(5, await db.Queryable<CasbinRule>().CountAsync());
            #endregion

            #region Add policy test
            await enforcer.AddPolicyAsync("alice", "data1", "write");
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("data2_admin", "data2", "read"),
                AsList("data2_admin", "data2", "write"),
                AsList("alice", "data1", "write")
            ));
            Assert.Equal(6, await db.Queryable<CasbinRule>().CountAsync());
            #endregion

            #region Remove policy test
            await enforcer.RemovePolicyAsync("alice", "data1", "write");
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("data2_admin", "data2", "read"),
                AsList("data2_admin", "data2", "write")
            ));
            Assert.Equal(5, await db.Queryable<CasbinRule>().CountAsync());

            await enforcer.RemoveFilteredPolicyAsync(0, "data2_admin");
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write")
            ));
            Assert.Equal(3, await db.Queryable<CasbinRule>().CountAsync());
            #endregion

            #region Update policy test
            await enforcer.UpdatePolicyAsync(AsList("alice", "data1", "read"),
                AsList("alice", "data2", "write"));
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data2", "write"),
                AsList("bob", "data2", "write")
            ));
            Assert.Equal(3, await db.Queryable<CasbinRule>().CountAsync());

            await enforcer.UpdatePolicyAsync(AsList("alice", "data2", "write"),
                AsList("alice", "data1", "read"));
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write")
            ));
            Assert.Equal(3, await db.Queryable<CasbinRule>().CountAsync());
            #endregion

            #region Batch APIs test
            await enforcer.AddPoliciesAsync(new []
            {
                new List<string>{"alice", "data2", "write"},
                new List<string>{"bob", "data1", "read"}
            });
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("alice", "data2", "write"),
                AsList("bob", "data1", "read")
            ));
            Assert.Equal(5, await db.Queryable<CasbinRule>().CountAsync());

            await enforcer.RemovePoliciesAsync(new []
            {
                new List<string>{"alice", "data1", "read"},
                new List<string>{"bob", "data2", "write"}
            });
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data2", "write"),
                AsList("bob", "data1", "read")
            ));
            Assert.Equal(3, await db.Queryable<CasbinRule>().CountAsync());
            #endregion
        }

        /// <summary>
        /// Tests async version of regular policies with AutoSave ON.
        /// </summary>
        [Fact]
        public async Task TestPolicyAutoSaveOnAsync()
        {
            // 配置 SqlSugar 连接
            var config = new ConnectionConfig()
            {
                ConnectionString = _fixture.ConnectionString,
                DbType = DbType.PostgreSQL,
                IsAutoCloseConnection = true,
                ConfigureExternalServices = new ConfigureExternalServices
                {
                    EntityService = (c, p) =>
                    {
                        // 映射 Schema 和表名
                        if (p.EntityName == nameof(CasbinRule))
                        {
                            p.DbTableName = $"{TransactionIntegrityTestFixture.PoliciesSchema}.{nameof(CasbinRule)}";
                        }
                    }
                }
            };

            // SqlSugarClient 实现了 IDisposable，建议使用 using
            using var db = new SqlSugarClient(config);
            
            // 初始化策略数据
            await InitPolicyAsync(db);

            // 实例化 SqlSugar 适配器
            var adapter = new SqlSugarAdapter(db);
            var model = DefaultModel.CreateFromText(System.IO.File.ReadAllText("examples/rbac_model.conf"));
            var enforcer = new Enforcer(model, adapter);

            #region Load policy test
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("data2_admin", "data2", "read"),
                AsList("data2_admin", "data2", "write")
            ));
            // 使用 SqlSugar 的异步查询
            Assert.Equal(5, await db.Queryable<CasbinRule>().CountAsync());
            #endregion

            #region Add policy test
            await enforcer.AddPolicyAsync("alice", "data1", "write");
            Assert.Equal(6, await db.Queryable<CasbinRule>().CountAsync());
            #endregion

            #region Remove policy test
            await enforcer.RemovePolicyAsync("alice", "data1", "write");
            Assert.Equal(5, await db.Queryable<CasbinRule>().CountAsync());
            #endregion
        }

        /// <summary>
        /// Tests regular policies with AutoSave OFF.
        /// Verifies that AddPolicy() correctly respects AutoSave OFF setting.
        /// This documents the CORRECT behavior (contrast with grouping policy bug).
        /// </summary>
        [Fact]
        public async Task TestPolicyAutoSaveOff()
        {
            // 配置 SqlSugar 连接
            var config = new ConnectionConfig()
            {
                ConnectionString = _fixture.ConnectionString,
                DbType = DbType.PostgreSQL,
                IsAutoCloseConnection = true,
                ConfigureExternalServices = new ConfigureExternalServices
                {
                    EntityService = (c, p) =>
                    {
                        if (p.EntityName == nameof(CasbinRule))
                        {
                            p.DbTableName = $"{TransactionIntegrityTestFixture.PoliciesSchema}.{nameof(CasbinRule)}";
                        }
                    }
                }
            };

            using var db = new SqlSugarClient(config);
            
            // 初始化策略数据
            await InitPolicyAsync(db);

            // 实例化 SqlSugar 适配器
            var adapter = new SqlSugarAdapter(db);
            var model = DefaultModel.CreateFromText(System.IO.File.ReadAllText("examples/rbac_model.conf"));
            var enforcer = new Enforcer(model, adapter);

            // 禁用自动保存
            enforcer.EnableAutoSave(false);

            // 验证初始状态 (InitPolicyAsync 应该插入了 5 条数据)
            Assert.Equal(5, await db.Queryable<CasbinRule>().CountAsync());

            // 添加策略 - 在 AutoSave OFF 时，不应该保存到数据库
            enforcer.AddPolicy("charlie", "data3", "read");

            // 验证策略尚未保存 (正确行为)
            var countAfterAdd = await db.Queryable<CasbinRule>().CountAsync();
            Assert.Equal(5, countAfterAdd); // 依然是 5 - 正确

            // 验证数据库中确实没有 charlie 的记录
            var charlieBeforeSave = await db.Queryable<CasbinRule>()
                .FirstAsync(p => p.PType == "p" && p.V0 == "charlie"); // 注意：SqlSugar 实体字段名通常映射为 V0, V1 等
            Assert.Null(charlieBeforeSave); // 数据库中尚不存在 - 正确

            // 当调用 SavePolicyAsync 时，它应该将内存中的所有策略持久化到数据库
            await enforcer.SavePolicyAsync();

            // 现在数据库中应该有 6 条策略了
            Assert.Equal(6, await db.Queryable<CasbinRule>().CountAsync()); // 5 + 1 = 6

            // 验证 SavePolicy 后数据已存在
            var charlieAfterSave = await db.Queryable<CasbinRule>()
                .FirstAsync(p => p.PType == "p" && p.V0 == "charlie");
            Assert.NotNull(charlieAfterSave);
        }

        /// <summary>
        /// Tests async version of regular policies with AutoSave OFF.
        /// Verifies that AddPolicyAsync() correctly respects AutoSave OFF setting.
        /// </summary>
        [Fact]
        public async Task TestPolicyAutoSaveOffAsync()
        {
            // 配置 SqlSugar 连接
            var config = new ConnectionConfig()
            {
                ConnectionString = _fixture.ConnectionString,
                DbType = DbType.PostgreSQL,
                IsAutoCloseConnection = true,
                ConfigureExternalServices = new ConfigureExternalServices
                {
                    EntityService = (c, p) =>
                    {
                        // 映射 Schema 和表名
                        if (p.EntityName == nameof(CasbinRule))
                        {
                            p.DbTableName = $"{TransactionIntegrityTestFixture.PoliciesSchema}.{nameof(CasbinRule)}";
                        }
                    }
                }
            };

            using var db = new SqlSugarClient(config);
            
            // 初始化策略数据 (内部应包含 db.CodeFirst.InitTables<CasbinRule>())
            await InitPolicyAsync(db);

            // 实例化 SqlSugar 适配器
            var adapter = new SqlSugarAdapter(db);
            var model = DefaultModel.CreateFromText(System.IO.File.ReadAllText("examples/rbac_model.conf"));
            var enforcer = new Enforcer(model, adapter);

            // 禁用自动保存
            enforcer.EnableAutoSave(false);

            // 验证初始状态 (期望 5 条)
            Assert.Equal(5, await db.Queryable<CasbinRule>().CountAsync());

            // 添加策略 - 在 AutoSave OFF 时，不应该立即保存到数据库
            await enforcer.AddPolicyAsync("charlie", "data3", "read");

            // 验证策略尚未保存 (正确行为)
            var countAfterAdd = await db.Queryable<CasbinRule>().CountAsync();
            Assert.Equal(5, countAfterAdd); // 依然是 5 - 正确

            // 当调用 SavePolicyAsync 时，它应该将内存中的所有策略持久化到数据库
            await enforcer.SavePolicyAsync();

            // 现在数据库中应该有 6 条策略了
            Assert.Equal(6, await db.Queryable<CasbinRule>().CountAsync());

            // 验证数据已存在于数据库中
            // 注意：根据你之前完成的实体转换，字段名应为 PType 和 V0
            var charlieAfterSave = await db.Queryable<CasbinRule>()
                .FirstAsync(p => p.PType == "p" && p.V0 == "charlie");
            Assert.NotNull(charlieAfterSave);
        }

        /// <summary>
        /// Tests grouping policies with AutoSave ON (default behavior).
        /// This test verifies that AddGroupingPolicy() immediately saves to database.
        /// </summary>
        [Fact]
        public async Task TestGroupingPolicyAutoSaveOn()
        {
            // 配置 SqlSugar 连接，注意这里使用了 GroupingsSchema
            var config = new ConnectionConfig()
            {
                ConnectionString = _fixture.ConnectionString,
                DbType = DbType.PostgreSQL,
                IsAutoCloseConnection = true,
                ConfigureExternalServices = new ConfigureExternalServices
                {
                    EntityService = (c, p) =>
                    {
                        // 映射到 GroupingsSchema 对应的表
                        if (p.EntityName == nameof(CasbinRule))
                        {
                            p.DbTableName = $"{TransactionIntegrityTestFixture.GroupingsSchema}.{nameof(CasbinRule)}";
                        }
                    }
                }
            };

            using var db = new SqlSugarClient(config);
            
            // 初始化策略数据
            await InitPolicyAsync(db);

            // 实例化 SqlSugar 适配器
            var adapter = new SqlSugarAdapter(db);
            var model = DefaultModel.CreateFromText(System.IO.File.ReadAllText("examples/rbac_model.conf"));
            var enforcer = new Enforcer(model, adapter);

            // 验证初始分组策略 (g 规则)
            TestGetGroupingPolicy(enforcer, AsList(
                AsList("alice", "data2_admin")
            ));
            // 验证数据库初始总行数
            Assert.Equal(5, await db.Queryable<CasbinRule>().CountAsync());

            // 添加分组策略 - 在 AutoSave ON 时，应该立即保存到数据库
            await enforcer.AddGroupingPolicyAsync("bob", "data2_admin");

            // 验证 Enforcer 内存状态
            TestGetGroupingPolicy(enforcer, AsList(
                AsList("alice", "data2_admin"),
                AsList("bob", "data2_admin")
            ));

            // 验证是否立即保存到了数据库
            Assert.Equal(6, await db.Queryable<CasbinRule>().CountAsync());
            
            // 验证具体的记录是否存在
            // EFCore: p.Type == "g" && p.Value1 == "bob" && p.Value2 == "data2_admin"
            // SqlSugar: p.PType == "g" && p.V0 == "bob" && p.V1 == "data2_admin"
            var bobGrouping = await db.Queryable<CasbinRule>()
                .FirstAsync(p => p.PType == "g" && p.V0 == "bob" && p.V1 == "data2_admin");
            Assert.NotNull(bobGrouping);
        }

        /// <summary>
        /// Tests grouping policies with AutoSave OFF.
        ///
        /// Verifies that AddGroupingPolicy() respects the EnableAutoSave(false) setting.
        ///
        /// Expected behavior (verified by this test):
        /// - AddGroupingPolicy() should NOT save to database when AutoSave is OFF
        /// - Only SavePolicy() should commit changes
        ///
        /// This test now passes with Casbin.NET 2.19.1+ which fixed the AutoSave bug.
        ///
        /// Related: Integration/README.md
        /// </summary>
        // [Fact]
        [Fact]
        public async Task TestGroupingPolicyAutoSaveOff()
        {
            // 配置 SqlSugar 连接，使用 GroupingsSchema
            var config = new ConnectionConfig()
            {
                ConnectionString = _fixture.ConnectionString,
                DbType = DbType.PostgreSQL,
                IsAutoCloseConnection = true,
                ConfigureExternalServices = new ConfigureExternalServices
                {
                    EntityService = (c, p) =>
                    {
                        if (p.EntityName == nameof(CasbinRule))
                        {
                            p.DbTableName = $"{TransactionIntegrityTestFixture.GroupingsSchema}.{nameof(CasbinRule)}";
                        }
                    }
                }
            };

            using var db = new SqlSugarClient(config);
            
            // 初始化策略数据
            await InitPolicyAsync(db);

            // 实例化 SqlSugar 适配器
            var adapter = new SqlSugarAdapter(db);
            var model = DefaultModel.CreateFromText(System.IO.File.ReadAllText("examples/rbac_model.conf"));
            var enforcer = new Enforcer(model, adapter);

            // 禁用自动保存
            enforcer.EnableAutoSave(false);

            // 验证初始状态 (期望 5 条)
            Assert.Equal(5, await db.Queryable<CasbinRule>().CountAsync());

            // 添加普通策略 - 在 AutoSave OFF 时，不应保存到数据库
            enforcer.AddPolicy("charlie", "data3", "read");

            // 验证普通策略尚未保存 (正确行为)
            Assert.Equal(5, await db.Queryable<CasbinRule>().CountAsync());

            // 添加分组策略 - 在 AutoSave OFF 时，也不应保存到数据库
            await enforcer.AddGroupingPolicyAsync("bob", "data2_admin");

            // 测试预期：分组策略不应被保存 (因为 AutoSave 是关闭的)
            // 备注：如果 Casbin.NET 版本低于 2.19.1，这里可能会因为 Bug 导致实际值为 6
            var savedCountAfterAdd = await db.Queryable<CasbinRule>().CountAsync();
            Assert.Equal(5, savedCountAfterAdd); 

            // 验证分组策略确实不在数据库中
            var bobGroupingBeforeSave = await db.Queryable<CasbinRule>()
                .FirstAsync(p => p.PType == "g" && p.V0 == "bob" && p.V1 == "data2_admin");
            Assert.Null(bobGroupingBeforeSave);

            // 当调用 SavePolicyAsync 时，它应该将内存中的两条新策略（1条p，1条g）全部持久化
            await enforcer.SavePolicyAsync();

            // 现在数据库中应该有 7 条记录了 (5 原始 + 1 charlie + 1 bob)
            Assert.Equal(7, await db.Queryable<CasbinRule>().CountAsync());

            // 验证 SavePolicy 后，两条记录都已存在
            var charliePolicy = await db.Queryable<CasbinRule>()
                .FirstAsync(p => p.PType == "p" && p.V0 == "charlie");
            var bobGroupingAfterSave = await db.Queryable<CasbinRule>()
                .FirstAsync(p => p.PType == "g" && p.V0 == "bob" && p.V1 == "data2_admin");

            Assert.NotNull(charliePolicy);
            Assert.NotNull(bobGroupingAfterSave);
        }

        /// <summary>
        /// Tests async version of grouping policies with AutoSave OFF.
        ///
        /// Verifies that AddGroupingPolicyAsync() respects the EnableAutoSave(false) setting.
        ///
        /// Expected behavior (verified by this test): AddGroupingPolicyAsync() should NOT save when AutoSave is OFF.
        ///
        /// This test now passes with Casbin.NET 2.19.1+ which fixed the AutoSave bug.
        /// </summary>
        [Fact]
        public async Task TestGroupingPolicyAutoSaveOffAsync()
        {
            // 配置 SqlSugar 连接，使用 GroupingsSchema
            var config = new ConnectionConfig()
            {
                ConnectionString = _fixture.ConnectionString,
                DbType = DbType.PostgreSQL,
                IsAutoCloseConnection = true,
                ConfigureExternalServices = new ConfigureExternalServices
                {
                    EntityService = (c, p) =>
                    {
                        // 动态映射到指定的 Schema 和表名
                        if (p.EntityName == nameof(CasbinRule))
                        {
                            p.DbTableName = $"{TransactionIntegrityTestFixture.GroupingsSchema}.{nameof(CasbinRule)}";
                        }
                    }
                }
            };

            using var db = new SqlSugarClient(config);
            
            // 初始化策略数据
            await InitPolicyAsync(db);

            // 实例化 SqlSugar 适配器
            var adapter = new SqlSugarAdapter(db);
            var model = DefaultModel.CreateFromText(System.IO.File.ReadAllText("examples/rbac_model.conf"));
            var enforcer = new Enforcer(model, adapter);

            // 禁用自动保存
            enforcer.EnableAutoSave(false);

            // 验证初始状态 (期望 5 条)
            Assert.Equal(5, await db.Queryable<CasbinRule>().CountAsync());

            // 添加普通策略 - 在 AutoSave OFF 时，不应保存到数据库
            await enforcer.AddPolicyAsync("charlie", "data3", "read");

            // 验证普通策略尚未保存 (正确行为)
            Assert.Equal(5, await db.Queryable<CasbinRule>().CountAsync());

            // 添加分组策略 - 在 AutoSave OFF 时，也不应保存到数据库
            await enforcer.AddGroupingPolicyAsync("bob", "data2_admin");

            // 测试预期：分组策略不应被保存
            // 备注：如果底层适配器或 Casbin.NET 逻辑有误，这里可能会变成 6
            var savedCountAfterAdd = await db.Queryable<CasbinRule>().CountAsync();
            Assert.Equal(5, savedCountAfterAdd); 

            // 当调用 SavePolicyAsync 时，手动触发持久化
            await enforcer.SavePolicyAsync();

            // 现在数据库中应该有 7 条记录了 (5 原始 + 1 charlie + 1 bob)
            Assert.Equal(7, await db.Queryable<CasbinRule>().CountAsync());

            // 验证数据已存在于数据库中
            var charliePolicy = await db.Queryable<CasbinRule>()
                .FirstAsync(p => p.PType == "p" && p.V0 == "charlie");
            var bobGrouping = await db.Queryable<CasbinRule>()
                .FirstAsync(p => p.PType == "g" && p.V0 == "bob" && p.V1 == "data2_admin");

            Assert.NotNull(charliePolicy);
            Assert.NotNull(bobGrouping);
        }

        #region Multi-Context AutoSave Tests

        /// <summary>
        /// Provider that routes policy types to three separate SqlSugar clients
        /// 这里的实现逻辑与 EFCore 版本一致，只是将 DbContext 替换为了 ISqlSugarClient
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
            /// 根据策略类型返回对应的 SqlSugar 客户端实例
            /// </summary>
            /// <param name="policyType">p, g, g2 等</param>
            /// <returns></returns>
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
            /// 获取所有参与的客户端实例，用于批量操作（如清空数据）
            /// </summary>
            public IEnumerable<ISqlSugarClient> GetAllClients()
            {
                return new[] { _policyClient, _groupingClient, _roleClient };
            }

            /// <summary>
            /// 获取共享的数据库连接（用于跨 Context 的事务测试）
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
                return policyType switch
                {
                    "p" => $"{TransactionIntegrityTestFixture.PoliciesSchema}.casbin_rule",
                    "g" => $"{TransactionIntegrityTestFixture.GroupingsSchema}.casbin_rule",
                    "g2" => $"{TransactionIntegrityTestFixture.RolesSchema}.casbin_rule",
                    _ => $"{TransactionIntegrityTestFixture.PoliciesSchema}.casbin_rule"
                };
            }
        }


        /// <summary>
        /// Tests AutoSave OFF with multiple contexts and rollback on failure.
        ///
        /// Verifies that:
        /// - With AutoSave OFF, policies batch in memory (not commit)
        /// - SavePolicy() uses shared transaction and rolls back atomically on failure
        ///
        /// This test now passes with Casbin.NET 2.19.1+ which fixed the AutoSave bug.
        /// </summary>
        [Fact]
        public async Task TestAutoSaveOff_MultiContext_RollbackOnFailure()
        {
            _output.WriteLine("=== AUTOSAVE OFF - MULTI-CONTEXT ATOMIC ROLLBACK TEST ===");
            _output.WriteLine("Goal: With AutoSave OFF, SavePolicy should use shared transaction and rollback atomically");
            _output.WriteLine("");

            // 1. 清理所有数据
            await _fixture.ClearAllPoliciesAsync();

            // 2. 创建一个共享的 Npgsql 连接
            // SqlSugar 可以接管现有的 DbConnection
            var sharedConnection = new NpgsqlConnection(_fixture.ConnectionString);
            await sharedConnection.OpenAsync();

            try
            {
                _output.WriteLine($"Shared connection: {sharedConnection.GetHashCode()}");
                _output.WriteLine("");

                // 3. 为三个不同的 Schema 创建三个 SqlSugarClient，共享同一个 connection 对象
                var config1 = CreateConfig(sharedConnection, TransactionIntegrityTestFixture.PoliciesSchema);
                // var client1 = new SqlSugarClient(config1); // 原始名称
                var policyClient = new SqlSugarClient(config1);
                if (sharedConnection != null) policyClient.Ado.Connection = (System.Data.Common.DbConnection)sharedConnection;

                var config2 = CreateConfig(sharedConnection, TransactionIntegrityTestFixture.GroupingsSchema);
                var groupingClient = new SqlSugarClient(config2);
                if (sharedConnection != null) groupingClient.Ado.Connection = (System.Data.Common.DbConnection)sharedConnection;

                var config3 = CreateConfig(sharedConnection, TransactionIntegrityTestFixture.RolesSchema);
                var roleClient = new SqlSugarClient(config3);
                if (sharedConnection != null) roleClient.Ado.Connection = (System.Data.Common.DbConnection)sharedConnection;

                // 4. 创建 Provider 和 Adapter
                // 使用之前转换好的 ThreeWaySqlSugarClientProvider
                var provider = new ThreeWaySqlSugarClientProvider(policyClient, groupingClient, roleClient, sharedConnection);
                var adapter = new SqlSugarAdapter(provider);

                // 5. 创建 Enforcer 并禁用 AutoSave
                var model = DefaultModel.CreateFromFile(ModelPath);
                var enforcer = new Enforcer(model);
                enforcer.SetAdapter(adapter);
                enforcer.EnableAutoSave(false);  // ← 关键：禁用自动保存
                _output.WriteLine("AutoSave disabled");
                _output.WriteLine("");

                // 6. 添加不同类型的策略（此时仅在内存中）
                _output.WriteLine("Adding policies with AutoSave OFF (should batch in memory):");
                enforcer.AddPolicy("alice", "data1", "read");
                enforcer.AddPolicy("bob", "data2", "write");
                _output.WriteLine("  Added 2 p policies");

                enforcer.AddGroupingPolicy("alice", "admin");
                enforcer.AddGroupingPolicy("bob", "user");
                _output.WriteLine("  Added 2 g groupings");

                enforcer.AddNamedGroupingPolicy("g2", "admin", "role-superuser");
                enforcer.AddNamedGroupingPolicy("g2", "user", "role-basic");
                _output.WriteLine("  Added 2 g2 roles");
                _output.WriteLine("");

                // 7. 检查数据库状态 - 应该是空的（因为还没 SavePolicy）
                var beforePoliciesCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.PoliciesSchema);
                var beforeGroupingsCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.GroupingsSchema);
                var beforeRolesCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.RolesSchema);
                _output.WriteLine($"STATE BEFORE DROP (should be 0,0,0): ({beforePoliciesCount}, {beforeGroupingsCount}, {beforeRolesCount})");

                Assert.Equal(0, beforePoliciesCount);
                Assert.Equal(0, beforeGroupingsCount);
                Assert.Equal(0, beforeRolesCount);
                _output.WriteLine("✓ Confirmed: AutoSave OFF prevents immediate commits");
                _output.WriteLine("");

                // 8. 强制制造失败：删除第三个 Schema 的表
                _output.WriteLine("FORCING FAILURE: Dropping casbin_roles.casbin_rule table...");
                // 使用 SqlSugar 的 Ado 功能执行原生 SQL
                await roleClient.Ado.ExecuteCommandAsync($"DROP TABLE {TransactionIntegrityTestFixture.RolesSchema}.casbin_rule");
                _output.WriteLine("Table dropped!");
                _output.WriteLine("");

                // 9. 尝试保存 - 此时应该触发事务并因为表不存在而失败
                _output.WriteLine("Calling SavePolicyAsync()... (expecting exception)");
                var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
                {
                    await enforcer.SavePolicyAsync();
                });
                _output.WriteLine($"✓ Exception caught as expected: {exception.GetType().Name}");
                _output.WriteLine($"  Message: {exception.Message}");
                _output.WriteLine("");

                // 10. 验证回滚结果
                var policiesCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.PoliciesSchema);
                var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.GroupingsSchema);

                _output.WriteLine("RESULTS - Policies per schema after failure:");
                _output.WriteLine($"  casbin_policies:  {policiesCount}");
                _output.WriteLine($"  casbin_groupings: {groupingsCount}");
                _output.WriteLine($"  casbin_roles:     N/A (table dropped)");
                _output.WriteLine("");

                // 断言：如果事务生效，前两个 Schema 的数据也应该被回滚，计数为 0
                if (policiesCount == 0 && groupingsCount == 0)
                {
                    _output.WriteLine("✓✓✓ AUTOSAVE OFF ATOMIC TRANSACTION TEST PASSED!");
                }
                else
                {
                    _output.WriteLine("✗✗✗ AUTOSAVE OFF ATOMIC TRANSACTION TEST FAILED!");
                }

                Assert.Equal(0, policiesCount);
                Assert.Equal(0, groupingsCount);

                // 释放客户端
                policyClient.Dispose();
                groupingClient.Dispose();
                roleClient.Dispose();
            }
            finally
            {
                await sharedConnection.DisposeAsync();
            }
        }

        /// <summary>
        /// 辅助方法：创建共享连接的 SqlSugar 配置
        /// 注意：SqlSugar 的 CodeFirst.InitTables() 需要 ConnectionString，即使使用外部连接
        /// </summary>
        private ConnectionConfig CreateConfig(System.Data.Common.DbConnection? connection, string schema)
        {
            return new ConnectionConfig()
            {
                // SqlSugar 需要 ConnectionString 用于 CodeFirst.InitTables() 等操作
                // 即使我们稍后通过 Ado.Connection 注入外部连接
                ConnectionString = _fixture.ConnectionString,
                DbType = DbType.PostgreSQL,
                // DbConnection = connection, // EFCore风格，SqlSugar不直接支持
                IsAutoCloseConnection = false, // 既然是共享连接，由外部控制关闭
                ConfigureExternalServices = new ConfigureExternalServices
                {
                    EntityService = (c, p) =>
                    {
                        if (p.EntityName == nameof(CasbinRule))
                        {
                            p.DbTableName = $"{schema}.casbin_rule";
                        }
                    }
                }
            };
        }

        /// <summary>
        /// Tests AutoSave ON with multiple contexts showing individual commits.
        /// Verifies that each AddPolicy commits independently (no cross-context atomicity).
        /// </summary>
        [Fact]
        public async Task TestAutoSaveOn_MultiContext_IndividualCommits()
        {
            _output.WriteLine("=== AUTOSAVE ON - INDIVIDUAL COMMITS TEST ===");
            _output.WriteLine("Goal: With AutoSave ON, each Add should commit independently (no atomicity)");
            _output.WriteLine("");

            // 1. 清理所有数据
            await _fixture.ClearAllPoliciesAsync();

            // 2. 创建一个共享的 Npgsql 连接
            var sharedConnection = new NpgsqlConnection(_fixture.ConnectionString);
            await sharedConnection.OpenAsync();

            try
            {
                // 3. 为三个不同的 Schema 创建三个 SqlSugarClient，共享同一个 connection 对象
                // 使用辅助方法 CreateConfig (见上一个代码块的转换)
                var config1 = CreateConfig(sharedConnection, TransactionIntegrityTestFixture.PoliciesSchema);
                // var client1 = new SqlSugarClient(config1); // 原始名称
                var policyClient = new SqlSugarClient(config1);
                if (sharedConnection != null) policyClient.Ado.Connection = (System.Data.Common.DbConnection)sharedConnection;

                var config2 = CreateConfig(sharedConnection, TransactionIntegrityTestFixture.GroupingsSchema);
                var groupingClient = new SqlSugarClient(config2);
                if (sharedConnection != null) groupingClient.Ado.Connection = (System.Data.Common.DbConnection)sharedConnection;

                var config3 = CreateConfig(sharedConnection, TransactionIntegrityTestFixture.RolesSchema);
                var roleClient = new SqlSugarClient(config3);
                if (sharedConnection != null) roleClient.Ado.Connection = (System.Data.Common.DbConnection)sharedConnection;

                // 4. 创建 Provider 和 Adapter
                var provider = new ThreeWaySqlSugarClientProvider(policyClient, groupingClient, roleClient, sharedConnection);
                var adapter = new SqlSugarAdapter(provider);

                // 5. 创建 Enforcer，AutoSave 默认为开启状态
                var model = DefaultModel.CreateFromFile(ModelPath);
                var enforcer = new Enforcer(model);
                enforcer.SetAdapter(adapter);
                // 【2024/12/21 修复】必须调用 LoadPolicyAsync() 才能激活 AutoSave 功能
                // Casbin.NET 的 AutoSave 只有在成功调用 LoadPolicy 之后才会生效
                await enforcer.LoadPolicyAsync();
                // enforcer.EnableAutoSave(true); // 默认即为 true
                _output.WriteLine("AutoSave enabled (default)");
                _output.WriteLine("");

                // 6. 步骤 1：添加 2 条 p 策略（应该立即提交）
                _output.WriteLine("Step 1: Adding 2 p policies (should commit immediately):");
                await enforcer.AddPolicyAsync("alice", "data1", "read");
                await enforcer.AddPolicyAsync("bob", "data2", "write");
                var step1Count = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.PoliciesSchema);
                _output.WriteLine($"  DB state after p policies: ({step1Count}, ?, ?)");
                Assert.Equal(2, step1Count);
                _output.WriteLine("");

                // 7. 步骤 2：添加 2 条 g 策略（应该立即提交）
                _output.WriteLine("Step 2: Adding 2 g policies (should commit immediately):");
                await enforcer.AddGroupingPolicyAsync("alice", "admin");
                await enforcer.AddGroupingPolicyAsync("bob", "user");
                var step2CountPolicy = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.PoliciesSchema);
                var step2CountGrouping = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.GroupingsSchema);
                _output.WriteLine($"  DB state after g policies: ({step2CountPolicy}, {step2CountGrouping}, ?)");
                Assert.Equal(2, step2CountPolicy);
                Assert.Equal(2, step2CountGrouping);
                _output.WriteLine("");

                // 8. 步骤 3：强制制造失败：删除第三个 Schema 的表
                _output.WriteLine("Step 3: Dropping casbin_roles.casbin_rule table...");
                await roleClient.Ado.ExecuteCommandAsync($"DROP TABLE {TransactionIntegrityTestFixture.RolesSchema}.casbin_rule");
                _output.WriteLine("Table dropped!");
                _output.WriteLine("");

                // 9. 步骤 4：尝试向第三个上下文添加策略 - 应该失败并抛出异常
                _output.WriteLine("Step 4: Trying to add g2 policy (expecting exception):");
                var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
                {
                    await enforcer.AddNamedGroupingPolicyAsync("g2", "admin", "role-superuser");
                });
                _output.WriteLine($"✓ Exception caught as expected: {exception.GetType().Name}");
                _output.WriteLine($"  Message: {exception.Message}");
                _output.WriteLine("");

                // 10. 检查最终状态 - 上下文 1 和 2 的数据应该依然存在（因为没有事务回滚它们）
                var finalPoliciesCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.PoliciesSchema);
                var finalGroupingsCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.GroupingsSchema);

                _output.WriteLine("FINAL RESULTS:");
                _output.WriteLine($"  casbin_policies:  {finalPoliciesCount}");
                _output.WriteLine($"  casbin_groupings: {finalGroupingsCount}");
                _output.WriteLine($"  casbin_roles:     N/A (table dropped)");
                _output.WriteLine("");

                // 断言：在 AutoSave ON 时，每个上下文独立提交，没有跨上下文原子性
                if (finalPoliciesCount == 2 && finalGroupingsCount == 2)
                {
                    _output.WriteLine("✓✓✓ AUTOSAVE ON INDIVIDUAL COMMITS TEST PASSED!");
                    _output.WriteLine("Each AddPolicy committed independently, no cross-context atomicity");
                }
                else
                {
                    _output.WriteLine("✗✗✗ AUTOSAVE ON INDIVIDUAL COMMITS TEST FAILED!");
                    _output.WriteLine($"Expected: (2, 2), Got: ({finalPoliciesCount}, {finalGroupingsCount})");
                }

                Assert.Equal(2, finalPoliciesCount);
                Assert.Equal(2, finalGroupingsCount);

                // 释放客户端
                policyClient.Dispose();
                groupingClient.Dispose();
                roleClient.Dispose();
            }
            finally
            {
                await sharedConnection.DisposeAsync();
            }
        }

        /// <summary>
        /// Tests AutoSave OFF success path with batched commit across multiple contexts.
        ///
        /// Verifies that with AutoSave OFF, SavePolicy() batches all operations
        /// in a shared transaction and commits atomically.
        ///
        /// This test now passes with Casbin.NET 2.19.1+ which fixed the AutoSave bug.
        /// </summary>
        [Fact]
        public async Task TestAutoSaveOff_MultiContext_BatchedCommit()
        {
            _output.WriteLine("=== AUTOSAVE OFF - SUCCESS PATH TEST ===");
            _output.WriteLine("Goal: With AutoSave OFF, SavePolicy should batch all operations in shared transaction");
            _output.WriteLine("");

            // 1. 清理所有数据
            await _fixture.ClearAllPoliciesAsync();

            // 2. 创建一个共享的 Npgsql 连接
            var sharedConnection = new NpgsqlConnection(_fixture.ConnectionString);
            await sharedConnection.OpenAsync();

            try
            {
                // 3. 为三个不同的 Schema 创建三个 SqlSugarClient，共享同一个 connection 对象
                // 使用之前定义的 CreateConfig 辅助方法
                var config1 = CreateConfig(sharedConnection, TransactionIntegrityTestFixture.PoliciesSchema);
                // var client1 = new SqlSugarClient(config1); // 原始名称
                var policyClient = new SqlSugarClient(config1);
                if (sharedConnection != null) policyClient.Ado.Connection = (System.Data.Common.DbConnection)sharedConnection;

                var config2 = CreateConfig(sharedConnection, TransactionIntegrityTestFixture.GroupingsSchema);
                var groupingClient = new SqlSugarClient(config2);
                if (sharedConnection != null) groupingClient.Ado.Connection = (System.Data.Common.DbConnection)sharedConnection;

                var config3 = CreateConfig(sharedConnection, TransactionIntegrityTestFixture.RolesSchema);
                var roleClient = new SqlSugarClient(config3);
                if (sharedConnection != null) roleClient.Ado.Connection = (System.Data.Common.DbConnection)sharedConnection;

                // 4. 创建 Provider 和 Adapter
                var provider = new ThreeWaySqlSugarClientProvider(policyClient, groupingClient, roleClient, sharedConnection);
                var adapter = new SqlSugarAdapter(provider);

                // 5. 创建 Enforcer 并禁用 AutoSave
                var model = DefaultModel.CreateFromFile(ModelPath);
                var enforcer = new Enforcer(model);
                enforcer.SetAdapter(adapter);
                enforcer.EnableAutoSave(false);
                _output.WriteLine("AutoSave disabled");
                _output.WriteLine("");

                // 6. 添加策略（此时应仅在内存中）
                _output.WriteLine("Adding policies with AutoSave OFF:");
                enforcer.AddPolicy("alice", "data1", "read");
                enforcer.AddPolicy("bob", "data2", "write");
                enforcer.AddGroupingPolicy("alice", "admin");
                enforcer.AddGroupingPolicy("bob", "user");
                enforcer.AddNamedGroupingPolicy("g2", "admin", "role-superuser");
                enforcer.AddNamedGroupingPolicy("g2", "user", "role-basic");
                _output.WriteLine("  Added 6 policies total");
                _output.WriteLine("");

                // 7. 在调用 SavePolicy 之前检查数据库状态（应该全为 0）
                var beforeCount1 = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.PoliciesSchema);
                var beforeCount2 = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.GroupingsSchema);
                var beforeCount3 = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.RolesSchema);
                _output.WriteLine($"DB state BEFORE SavePolicy: ({beforeCount1}, {beforeCount2}, {beforeCount3})");

                if (beforeCount1 == 0 && beforeCount2 == 0 && beforeCount3 == 0)
                {
                    _output.WriteLine("✓ Confirmed: Policies batched in memory, not committed yet");
                }
                _output.WriteLine("");

                // 8. 调用 SavePolicyAsync，触发批量提交
                _output.WriteLine("Calling SavePolicyAsync()...");
                await enforcer.SavePolicyAsync();
                _output.WriteLine("SavePolicyAsync() completed");
                _output.WriteLine("");

                // 9. 检查最终状态（每个 Schema 应该各有 2 条数据）
                var finalCount1 = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.PoliciesSchema);
                var finalCount2 = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.GroupingsSchema);
                var finalCount3 = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.RolesSchema);

                _output.WriteLine($"DB state AFTER SavePolicy: ({finalCount1}, {finalCount2}, {finalCount3})");
                _output.WriteLine("");

                if (finalCount1 == 2 && finalCount2 == 2 && finalCount3 == 2)
                {
                    _output.WriteLine("✓✓✓ AUTOSAVE OFF SUCCESS PATH TEST PASSED!");
                    _output.WriteLine("SavePolicy committed all batched policies atomically");
                }
                else
                {
                    _output.WriteLine("✗✗✗ AUTOSAVE OFF SUCCESS PATH TEST FAILED!");
                    _output.WriteLine($"Expected: (2, 2, 2), Got: ({finalCount1}, {finalCount2}, {finalCount3})");
                }

                Assert.Equal(2, finalCount1);
                Assert.Equal(2, finalCount2);
                Assert.Equal(2, finalCount3);

                // 10. 释放 SqlSugar 客户端
                policyClient.Dispose();
                groupingClient.Dispose();
                roleClient.Dispose();
            }
            finally
            {
                // 11. 释放共享连接
                await sharedConnection.DisposeAsync();
            }
        }

        #endregion

        #region Helper Methods - SqlSugar

        // Helper to create ISqlSugarClient with schema-specific searchpath (Tier A strategy)
        private ISqlSugarClient CreateClientWithSchema(string connectionString, string schemaName)
        {
            var config = new ConnectionConfig
            {
                ConnectionString = connectionString + $";searchpath={schemaName}",
                DbType = DbType.PostgreSQL,
                IsAutoCloseConnection = false, // We manage connection manually
                InitKeyType = InitKeyType.Attribute
            };

            var client = new SqlSugarClient(config);
            client.CodeFirst.InitTables<CasbinRule>();
            return client;
        }

        #endregion

        /// <summary>
        /// 初始化测试策略数据
        /// </summary>
        /// <param name="db">SqlSugar 客户端实例</param>
        private static async Task InitPolicyAsync(ISqlSugarClient db)
        {
            // 1. 清理现有策略
            // SqlSugar 的 Deleteable 是直接生成 DELETE SQL 语句，不需要像 EFCore 那样先查询再删除
            // 也不需要处理状态跟踪（AsNoTracking/Attach）
            await db.Deleteable<CasbinRule>().ExecuteCommandAsync();

            // 2. 准备测试数据
            // 注意：字段名已转换为 Casbin 标准的 PType, V0, V1, V2
            var testRules = new List<CasbinRule>
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

            // 3. 批量插入数据
            await db.Insertable(testRules).ExecuteCommandAsync();
        }
    }
}
