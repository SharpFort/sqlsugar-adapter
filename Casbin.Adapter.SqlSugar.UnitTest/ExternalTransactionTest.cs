using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Casbin;
using Casbin.Model;
using Casbin.Adapter.SqlSugar.Entities;
using SqlSugar;
using Xunit;

namespace Casbin.Adapter.SqlSugar.UnitTest
{
    public class ExternalTransactionTest
    {
        [Fact]
        public void TestSync_ExternalTransaction_Reuse()
        {
            // Arrange
            var dbPath = "TransactionTest_Async.db";
            var client = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = $"DataSource={dbPath}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute
            });
            
             if(System.IO.File.Exists(dbPath)) System.IO.File.Delete(dbPath);
             client.CodeFirst.InitTables<CasbinRule>(); // 内存模式需要保持连接打开，否则表会丢失
            // 但这里我们要测试事务，通常是在同一个连接上
            // 为了简化，我们使用同一实例
            
            var adapter = new SqlSugarAdapter(client);
            // 手动加载模型
            var text = System.IO.File.ReadAllText("examples/rbac_model.conf");
            var model = DefaultModel.CreateFromText(text);
            var enforcer = new Enforcer(model, adapter);
            
            // Act
            client.Ado.BeginTran(); // 开启外部事务
            
            // 执行添加策略，适配器应该检测到事务并复用，而不是开启新事务
            // 如果适配器开启新事务，SQLite 可能会报错（取决于驱动和模式），
            // 或者只是创建嵌套事务。
            // 我们的核心修复是：当有外部事务时，适配器不应提交/回滚它。
            
            enforcer.AddPolicy("alice", "data1", "read");
            
            // Assert
            // 此时不应提交
            var countInTrans = client.Queryable<CasbinRule>().Count();
            Assert.Equal(1, countInTrans);
            
            client.Ado.RollbackTran(); // 回滚外部事务
            
            // 验证回滚生效：说明适配器没有私自提交
            var countAfterRollback = client.Queryable<CasbinRule>().Count();
            Assert.Equal(0, countAfterRollback);
        }

        [Fact]
        public async Task TestAsync_ExternalTransaction_Reuse()
        {
            // Arrange
            var dbPath = "TransactionTest.db";
            var client = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = $"DataSource={dbPath}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute
            });
            
             // Ensure clean state
             if(System.IO.File.Exists(dbPath)) System.IO.File.Delete(dbPath);
             
             client.CodeFirst.InitTables<CasbinRule>();
            
            var adapter = new SqlSugarAdapter(client);
            var text = System.IO.File.ReadAllText("examples/rbac_model.conf");
            var model = DefaultModel.CreateFromText(text);
            var enforcer = new Enforcer(model, adapter);
            
            // Act
            client.Ado.BeginTran(); // 开启外部事务
            
            await enforcer.AddPolicyAsync("bob", "data2", "write");
            
            // Assert
            var countInTrans = client.Queryable<CasbinRule>().Count();
            Assert.Equal(1, countInTrans);
            
            client.Ado.RollbackTran(); // 回滚外部事务
            
            // 验证回滚生效
            var countAfterRollback = client.Queryable<CasbinRule>().Count();
            Assert.Equal(0, countAfterRollback);
        }
    }
}
