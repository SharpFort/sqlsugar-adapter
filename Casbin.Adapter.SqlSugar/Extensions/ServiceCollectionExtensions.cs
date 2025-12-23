using System;
using Casbin.Persist;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SqlSugar;

namespace Casbin.Adapter.SqlSugar.Extensions
{
    /// <summary>
    /// SqlSugar Casbin Adapter 的依赖注入扩展方法
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 注册 SqlSugar Casbin Adapter 到服务容器 (单数据库场景)
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configAction">SqlSugar 连接配置</param>
        /// <param name="lifetime">服务生命周期 (默认: Scoped)</param>
        /// <returns>服务集合 (支持链式调用)</returns>
        public static IServiceCollection AddSqlSugarCasbinAdapter(
            this IServiceCollection services,
            Action<ConnectionConfig> configAction,
            ServiceLifetime lifetime = ServiceLifetime.Scoped)
        {
            // 注册 SqlSugarClient
            var sqlSugarDescriptor = new ServiceDescriptor(
                typeof(ISqlSugarClient),
                sp =>
                {
                    var config = new ConnectionConfig();
                    configAction(config);
                    // 确保开启 Attribute 自动建表功能
                    config.InitKeyType = InitKeyType.Attribute;
                    return new SqlSugarClient(config);
                },
                lifetime);

            services.TryAdd(sqlSugarDescriptor);

            // 注册 Adapter
            var adapterDescriptor = new ServiceDescriptor(
                typeof(IAdapter),
                sp => new SqlSugarAdapter(sp.GetRequiredService<ISqlSugarClient>()),
                lifetime);

            services.TryAdd(adapterDescriptor);

            return services;
        }

        /// <summary>
        /// 注册 SqlSugar Casbin Adapter 到服务容器 (使用已注册的 ISqlSugarClient)
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="lifetime">服务生命周期 (默认: Scoped)</param>
        /// <returns>服务集合 (支持链式调用)</returns>
        public static IServiceCollection AddSqlSugarCasbinAdapter(
            this IServiceCollection services,
            ServiceLifetime lifetime = ServiceLifetime.Scoped)
        {
            var adapterDescriptor = new ServiceDescriptor(
                typeof(IAdapter),
                sp => new SqlSugarAdapter(sp.GetRequiredService<ISqlSugarClient>()),
                lifetime);

            services.TryAdd(adapterDescriptor);

            return services;
        }

        /// <summary>
        /// 注册 SqlSugar Casbin Adapter 到服务容器 (多租户场景)
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="providerFactory">客户端提供程序工厂方法</param>
        /// <param name="lifetime">服务生命周期 (默认: Scoped)</param>
        /// <returns>服务集合 (支持链式调用)</returns>
        /// <example>
        /// <code>
        /// services.AddSqlSugarCasbinAdapterWithProvider(sp =>
        /// {
        ///     var policyClient = sp.GetRequiredService&lt;ISqlSugarClient&gt;();
        ///     var groupingClient = sp.GetRequiredService&lt;IGroupingSqlSugarClient&gt;();
        ///     return new PolicyTypeClientProvider(policyClient, groupingClient);
        /// });
        /// </code>
        /// </example>
        public static IServiceCollection AddSqlSugarCasbinAdapterWithProvider(
            this IServiceCollection services,
            Func<IServiceProvider, ISqlSugarClientProvider> providerFactory,
            ServiceLifetime lifetime = ServiceLifetime.Scoped)
        {
            var adapterDescriptor = new ServiceDescriptor(
                typeof(IAdapter),
                sp =>
                {
                    var provider = providerFactory(sp);
                    return new SqlSugarAdapter(provider);
                },
                lifetime);

            services.TryAdd(adapterDescriptor);

            return services;
        }

        /// <summary>
        /// 注册 SqlSugar Casbin Adapter 到服务容器 (使用已注册的 ISqlSugarClientProvider)
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="lifetime">服务生命周期 (默认: Scoped)</param>
        /// <returns>服务集合 (支持链式调用)</returns>
        public static IServiceCollection AddSqlSugarCasbinAdapterWithProvider(
            this IServiceCollection services,
            ServiceLifetime lifetime = ServiceLifetime.Scoped)
        {
            var adapterDescriptor = new ServiceDescriptor(
                typeof(IAdapter),
                sp => new SqlSugarAdapter(sp.GetRequiredService<ISqlSugarClientProvider>()),
                lifetime);

            services.TryAdd(adapterDescriptor);

            return services;
        }
    }
}
