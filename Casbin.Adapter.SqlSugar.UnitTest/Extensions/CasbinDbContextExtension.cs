using SqlSugar;
using Casbin.Adapter.SqlSugar.Entities;

namespace Casbin.Adapter.SqlSugar.UnitTest.Extensions
{
    /// <summary>
    /// SqlSugar 客户端扩展方法，用于测试辅助操作
    /// </summary>
    public static class SqlSugarClientExtension
    {
        /// <summary>
        /// 清空 Casbin 策略表中的所有数据
        /// </summary>
        /// <param name="client">SqlSugar 客户端实例</param>
        internal static void Clear(this ISqlSugarClient client)
        {
            // 确保表存在（自动建表）
            client.CodeFirst.InitTables<CasbinRule>();

            try
            {
                // 删除所有数据（性能更好）
                client.Deleteable<CasbinRule>().ExecuteCommand();
            }
            catch
            {
                // 如果删除失败，尝试 Truncate（某些数据库可能需要特殊权限）
                try
                {
                    client.DbMaintenance.TruncateTable(nameof(CasbinRule));
                }
                catch
                {
                    // 最后的备选方案：重建表
                    client.DbMaintenance.DropTable(nameof(CasbinRule));
                    client.CodeFirst.InitTables<CasbinRule>();
                }
            }
        }

        /// <summary>
        /// 异步清空 Casbin 策略表中的所有数据
        /// </summary>
        /// <param name="client">SqlSugar 客户端实例</param>
        internal static async System.Threading.Tasks.Task ClearAsync(this ISqlSugarClient client)
        {
            // 确保表存在（自动建表）
            client.CodeFirst.InitTables<CasbinRule>();

            try
            {
                // 删除所有数据（性能更好）
                await client.Deleteable<CasbinRule>().ExecuteCommandAsync();
            }
            catch
            {
                // 如果删除失败，尝试 Truncate（某些数据库可能需要特殊权限）
                try
                {
                    client.DbMaintenance.TruncateTable(nameof(CasbinRule));
                }
                catch
                {
                    // 最后的备选方案：重建表
                    client.DbMaintenance.DropTable(nameof(CasbinRule));
                    client.CodeFirst.InitTables<CasbinRule>();
                }
            }
        }
    }
}