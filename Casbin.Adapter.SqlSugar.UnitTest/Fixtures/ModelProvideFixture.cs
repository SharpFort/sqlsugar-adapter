using Casbin.Model;

namespace Casbin.Adapter.SqlSugar.UnitTest.Fixtures
{
    /// <summary>
    /// Casbin Model 提供器 Fixture，用于单元测试
    /// </summary>
    public class ModelProvideFixture
    {
        private readonly string _rbacModelText = System.IO.File.ReadAllText("examples/rbac_model.conf");

        /// <summary>
        /// 获取一个新的 RBAC 模型实例
        /// </summary>
        /// <returns>Casbin RBAC 模型</returns>
        public IModel GetNewRbacModel()
        {
            return DefaultModel.CreateFromText(_rbacModelText);
        }
    }
}