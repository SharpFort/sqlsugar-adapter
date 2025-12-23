using System.Collections.Generic;
using Casbin.Model;
using Casbin.Persist;
using Casbin.Adapter.SqlSugar.Entities;

namespace Casbin.Adapter.SqlSugar.Extensions
{
    /// <summary>
    /// CasbinRule 实体与 Casbin PolicyStore 之间的转换辅助方法
    /// </summary>
    public static class CasbinRuleExtension
    {
        /// <summary>
        /// 定义策略加载委托类型
        /// </summary>
        public delegate void LoadPolicyLineHandler<TKey, TValue>(TKey key, TValue value);

        /// <summary>
        /// 将数据库中的 CasbinRule 实体加载到 Casbin PolicyStore 中
        /// </summary>
        /// <param name="rule">数据库中的策略规则实体</param>
        /// <param name="store">Casbin 策略存储</param>
        public static void LoadPolicyLine(CasbinRule rule, IPolicyStore store)
        {
            var values = new List<string>();
            if (!string.IsNullOrEmpty(rule.V0)) values.Add(rule.V0);
            if (!string.IsNullOrEmpty(rule.V1)) values.Add(rule.V1);
            if (!string.IsNullOrEmpty(rule.V2)) values.Add(rule.V2);
            if (!string.IsNullOrEmpty(rule.V3)) values.Add(rule.V3);
            if (!string.IsNullOrEmpty(rule.V4)) values.Add(rule.V4);
            if (!string.IsNullOrEmpty(rule.V5)) values.Add(rule.V5);

            // 根据 PType 的首字母判断 section: 
            // - "p", "p2", "p3"... 属于 "p" section (策略规则)
            // - "g", "g2", "g3"... 属于 "g" section (角色/分组规则)
            var section = rule.PType.StartsWith("g") ? "g" : "p";
            
            // 使用 Policy.ValuesFrom() 创建 IPolicyValues
            var requiredCount = store.GetRequiredValuesCount(section, rule.PType);
            var policyValues = Policy.ValuesFrom(values, requiredCount);
            store.AddPolicy(section, rule.PType, policyValues);
        }

        /// <summary>
        /// 将策略值列表转换为 CasbinRule 实体
        /// </summary>
        /// <param name="ptype">策略类型 (如 "p", "g", "p2", "g2" 等)</param>
        /// <param name="rule">策略值列表</param>
        /// <returns>CasbinRule 实体</returns>
        public static CasbinRule ToCasbinRule(string ptype, IList<string> rule)
        {
            static string Normalize(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

            var entity = new CasbinRule { PType = ptype };

            if (rule.Count > 0) entity.V0 = Normalize(rule[0]);
            if (rule.Count > 1) entity.V1 = Normalize(rule[1]);
            if (rule.Count > 2) entity.V2 = Normalize(rule[2]);
            if (rule.Count > 3) entity.V3 = Normalize(rule[3]);
            if (rule.Count > 4) entity.V4 = Normalize(rule[4]);
            if (rule.Count > 5) entity.V5 = Normalize(rule[5]);

            return entity;
        }

        /// <summary>
        /// 将策略值(IPolicyValues)转换为 CasbinRule 实体
        /// </summary>
        /// <param name="ptype">策略类型 (如 "p", "g", "p2", "g2" 等)</param>
        /// <param name="values">策略值</param>
        /// <returns>CasbinRule 实体</returns>
        public static CasbinRule ToCasbinRule(string ptype, IPolicyValues values)
        {
            static string Normalize(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

            var entity = new CasbinRule 
            { 
                PType = ptype,
                V0 = values.Count > 0 ? Normalize(values[0]) : null,
                V1 = values.Count > 1 ? Normalize(values[1]) : null,
                V2 = values.Count > 2 ? Normalize(values[2]) : null,
                V3 = values.Count > 3 ? Normalize(values[3]) : null,
                V4 = values.Count > 4 ? Normalize(values[4]) : null,
                V5 = values.Count > 5 ? Normalize(values[5]) : null
            };
            return entity;
        }
    }
}
