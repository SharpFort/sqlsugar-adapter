using Microsoft.Extensions.DependencyInjection;
using Casbin.Adapter.SqlSugar.UnitTest.Fixtures;
using Xunit;
using Casbin.Model;
using Casbin.Persist;
using SqlSugar;
using Casbin.Adapter.SqlSugar;

namespace Casbin.Adapter.SqlSugar.UnitTest
{
    public class DependencyInjectionTest : IClassFixture<TestHostFixture>, IClassFixture<ModelProvideFixture>
    {
        private readonly TestHostFixture _testHostFixture;
        private readonly ModelProvideFixture _modelProvideFixture;

        public DependencyInjectionTest(TestHostFixture testHostFixture, ModelProvideFixture modelProvideFixture)
        {
            _testHostFixture = testHostFixture;
            _modelProvideFixture = modelProvideFixture;
        }

        [Fact]
        public void ShouldResolveSqlSugarClient()
        {
            var client = _testHostFixture.Services.GetService<ISqlSugarClient>();
            Assert.NotNull(client);
            client.CodeFirst.InitTables<Casbin.Adapter.SqlSugar.Entities.CasbinRule>();
        }

        [Fact]
        public void ShouldResolveSqlSugarAdapter()
        {
            var adapter = _testHostFixture.Services.GetService<IAdapter>();
            Assert.NotNull(adapter);
            Assert.IsType<Casbin.Adapter.SqlSugar.SqlSugarAdapter>(adapter);
        }

        [Fact]
        public void ShouldUseAdapterAcrossMultipleScopesWithClientDirectly()
        {
            // 模拟在一个 scope 中创建适配器，但在另一个 scope 中使用的情况
            // （如在 casbin-aspnetcore 中）
            IAdapter adapter;
            
            // 在第一个 scope 中使用 SqlSugarClient 创建适配器
            using (var scope1 = _testHostFixture.Services.CreateScope())
            {
                var client = scope1.ServiceProvider.GetRequiredService<ISqlSugarClient>();
                client.CodeFirst.InitTables<Casbin.Adapter.SqlSugar.Entities.CasbinRule>();
                adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(client);
            }
            
            // SqlSugar 的 IsAutoCloseConnection 设置为 true 时，
            // 即使 scope 被释放，客户端也应该能正常工作
            var model = _modelProvideFixture.GetNewRbacModel();
            adapter.LoadPolicy(model); // 应该不会抛出异常（与 EFCore 不同）
        }

        [Fact]
        public void ShouldUseAdapterWithServiceProvider()
        {
            // SqlSugar 不需要 IServiceProvider 构造函数，因为客户端管理自己的连接
            // 但我们仍然可以从 DI 容器获取客户端
            ISqlSugarClient client;
            
            using (var scope = _testHostFixture.Services.CreateScope())
            {
                client = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
                client.CodeFirst.InitTables<Casbin.Adapter.SqlSugar.Entities.CasbinRule>();
            }
            
            var adapter = new Casbin.Adapter.SqlSugar.SqlSugarAdapter(client);
            var model = _modelProvideFixture.GetNewRbacModel();
            adapter.LoadPolicy(model); // 应该正常工作
        }

        [Fact]
        public void ShouldResolveAdapterRegisteredWithExtensionMethod()
        {
            // 通过 AddSqlSugarCasbinAdapter 扩展方法注册的适配器应该可以解析
            var adapter = _testHostFixture.Services.GetService<IAdapter>();
            Assert.NotNull(adapter);
            
            // 确保数据库存在
            using (var scope = _testHostFixture.Services.CreateScope())
            {
                var client = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
                client.CodeFirst.InitTables<Casbin.Adapter.SqlSugar.Entities.CasbinRule>();
            }
            
            // 应该能够使用适配器
            var model = _modelProvideFixture.GetNewRbacModel();
            adapter.LoadPolicy(model); // 应该不会抛出异常
        }

        [Fact]
        public void ShouldWorkWithScopedLifetime()
        {
            // 测试 Scoped 生命周期是否正常工作
            IAdapter adapter1;
            IAdapter adapter2;
            
            using (var scope1 = _testHostFixture.Services.CreateScope())
            {
                adapter1 = scope1.ServiceProvider.GetRequiredService<IAdapter>();
                var model = _modelProvideFixture.GetNewRbacModel();
                adapter1.LoadPolicy(model);
            }
            
            using (var scope2 = _testHostFixture.Services.CreateScope())
            {
                adapter2 = scope2.ServiceProvider.GetRequiredService<IAdapter>();
                var model = _modelProvideFixture.GetNewRbacModel();
                adapter2.LoadPolicy(model);
            }
            
            // 两个适配器实例应该是不同的（Scoped）
            Assert.NotSame(adapter1, adapter2);
        }
    }
}