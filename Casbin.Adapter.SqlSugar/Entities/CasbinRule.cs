using System.Collections.Generic;
using SqlSugar;
using Casbin.Persist;

// CasbinRule 对应数据库表名 casbin_rule  ai提供的sqlsugar orm 的相关代码
namespace Casbin.Adapter.SqlSugar.Entities
{
    [SugarTable("casbin_rule")] // 对应数据库表名
    public class CasbinRule : IPersistPolicy
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] // 主键，自增
        public int Id { get; set; }

        [SugarColumn(Length = 254, IsNullable = true, IndexGroupNameList = new[] { "ux_casbin_rule" })]
        public string PType { get; set; }

        // 建立索引可以显著提高 LoadPolicy 和 RemovePolicy 的速度
        [SugarColumn(Length = 254, IsNullable = true, IndexGroupNameList = new[] { "index_v0", "ux_casbin_rule" })]
        public string V0 { get; set; }

        [SugarColumn(Length = 254, IsNullable = true, IndexGroupNameList = new[] { "index_v1", "ux_casbin_rule" })]
        public string V1 { get; set; }

        [SugarColumn(Length = 254, IsNullable = true, IndexGroupNameList = new[] { "index_v2", "ux_casbin_rule" })]
        public string V2 { get; set; }

        [SugarColumn(Length = 254, IsNullable = true, IndexGroupNameList = new[] { "index_v3", "ux_casbin_rule" })]
        public string V3 { get; set; }

        [SugarColumn(Length = 254, IsNullable = true, IndexGroupNameList = new[] { "index_v4", "ux_casbin_rule" })]
        public string V4 { get; set; }

        [SugarColumn(Length = 254, IsNullable = true, IndexGroupNameList = new[] { "index_v5", "ux_casbin_rule" })]
        public string V5 { get; set; }

        [SugarColumn(Length = 254, IsNullable = true)] 
        public string V6 { get; set; }

        [SugarColumn(Length = 254, IsNullable = true)] 
        public string V7 { get; set; }

        [SugarColumn(Length = 254, IsNullable = true)] 
        public string V8 { get; set; }
    
        [SugarColumn(Length = 254, IsNullable = true)] 
        public string V9 { get; set; }

        [SugarColumn(Length = 254, IsNullable = true)] 
        public string V10 { get; set; }

        [SugarColumn(Length = 254, IsNullable = true)] 
        public string V11 { get; set; }

        [SugarColumn(Length = 254, IsNullable = true)] 
        public string V12 { get; set; }
        
        [SugarColumn(Length = 254, IsNullable = true)] 
        public string V13 { get; set; }

        [SugarColumn(Length = 254, IsNullable = true)] 
        public string V14 { get; set; }



        // IPersistPolicy implementation
        [SugarColumn(IsIgnore = true)]
        public string Type 
        { 
            get => PType; 
            set => PType = value; 
        }
        
        [SugarColumn(IsIgnore = true)]
        public string Section 
        { 
            get => !string.IsNullOrEmpty(PType) && PType.Length > 0 ? PType[0].ToString() : string.Empty;
            set { } // Section 是计算属性，不需要实际设置值
        }
        
        // IPersistPolicy 接口要求的 Value0-Value14 属性
        // 这些属性映射到数据库字段 V0-V14
        [SugarColumn(IsIgnore = true)]
        public string Value0 { get => V0; set => V0 = value; }
        
        [SugarColumn(IsIgnore = true)]
        public string Value1 { get => V1; set => V1 = value; }
        
        [SugarColumn(IsIgnore = true)]
        public string Value2 { get => V2; set => V2 = value; }
        
        [SugarColumn(IsIgnore = true)]
        public string Value3 { get => V3; set => V3 = value; }
        
        [SugarColumn(IsIgnore = true)]
        public string Value4 { get => V4; set => V4 = value; }
        
        [SugarColumn(IsIgnore = true)]
        public string Value5 { get => V5; set => V5 = value; }
        
        [SugarColumn(IsIgnore = true)]
        public string Value6 { get => V6; set => V6 = value; }
        
        [SugarColumn(IsIgnore = true)]
        public string Value7 { get => V7; set => V7 = value; }
        
        [SugarColumn(IsIgnore = true)]
        public string Value8 { get => V8; set => V8 = value; }
        
        [SugarColumn(IsIgnore = true)]
        public string Value9 { get => V9; set => V9 = value; }
        
        [SugarColumn(IsIgnore = true)]
        public string Value10 { get => V10; set => V10 = value; }
        
        [SugarColumn(IsIgnore = true)]
        public string Value11 { get => V11; set => V11 = value; }
        
        [SugarColumn(IsIgnore = true)]
        public string Value12 { get => V12; set => V12 = value; }
        
        [SugarColumn(IsIgnore = true)]
        public string Value13 { get => V13; set => V13 = value; }
        
        [SugarColumn(IsIgnore = true)]
        public string Value14 { get => V14; set => V14 = value; }

        
        public override string ToString()
        {
             return string.Join(", ", PType, V0, V1, V2, V3, V4, V5);
        }
    }
}