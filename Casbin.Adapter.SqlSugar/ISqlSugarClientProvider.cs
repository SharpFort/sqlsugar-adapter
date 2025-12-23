using System.Collections.Generic;
using SqlSugar;

#nullable enable

namespace Casbin.Adapter.SqlSugar
{
    /// <summary>
    /// 提供 ISqlSugarClient 实例的接口，支持多租户/多数据库场景。
    /// 不同的策略类型 (p, g, p2, g2) 可以存储在不同的数据库或 Schema 中。
    /// </summary>
    public interface ISqlSugarClientProvider
    {
        /// <summary>
        /// 根据策略类型获取对应的 SqlSugar 客户端实例
        /// </summary>
        /// <param name="policyType">策略类型标识符 (如 "p", "p2", "g", "g2")</param>
        /// <returns>负责该策略类型的 ISqlSugarClient 实例</returns>
        ISqlSugarClient GetClientForPolicyType(string policyType);

        /// <summary>
        /// 获取此提供程序管理的所有唯一 ISqlSugarClient 实例。
        /// 用于需要跨所有客户端协调的操作 (如 SavePolicy, LoadPolicy)。
        /// </summary>
        /// <returns>所有不同的 ISqlSugarClient 实例集合</returns>
        IEnumerable<ISqlSugarClient> GetAllClients();

        /// <summary>
        /// 指示所有客户端是否共享同一个物理连接/事务上下文。
        /// </summary>
        /// <remarks>
        /// 当返回 true 时，适配器可以使用单一事务保证原子性。
        /// 当返回 false 时，适配器将为每个客户端使用独立事务。
        /// 
        /// 返回 true 的场景：
        /// - 使用 SqlSugarScope 的多租户配置
        /// - 使用 ITenant 切换的同一数据库连接
        /// 
        /// 返回 false 的场景：
        /// - 完全独立的数据库连接
        /// - 不同物理服务器上的数据库
        /// </remarks>
        bool SharesConnection { get; }

        #region 集成测试多 Schema 支持 - 2024/12/21 新增

        /// <summary>
        /// 【集成测试多 Schema 支持】根据策略类型获取完全限定的表名称。
        /// 例如：对于 PostgreSQL 多 Schema 场景，可能返回 "casbin_policies.casbin_rule"。
        /// 
        /// 此方法是为了支持集成测试中的多 Schema 分布场景而添加的。
        /// 在单 Schema 场景（如单元测试）中，此方法返回 null，适配器将使用默认表名。
        /// </summary>
        /// <param name="policyType">策略类型标识符 (如 "p", "p2", "g", "g2")</param>
        /// <returns>
        /// 完全限定的表名称（如 "schema.table_name"），或返回 null 表示使用默认表名。
        /// 默认实现返回 null，保持向后兼容性。
        /// </returns>
        /// <remarks>
        /// 使用场景：
        /// - 返回 null: 单数据库/单 Schema 场景，使用实体类上定义的默认表名
        /// - 返回 "schema.table": 多 Schema 场景，如 PostgreSQL 的不同 search_path
        /// 
        /// 注意：此方法用于配合 SqlSugar 的 .AS("tableName") 方法，在运行时动态指定表名。
        /// EntityService 配置在初始化时设置，但可能不会在运行时生效，因此需要此显式机制。
        /// </remarks>
        string? GetTableNameForPolicyType(string policyType) => null;

        #endregion
    }
}
