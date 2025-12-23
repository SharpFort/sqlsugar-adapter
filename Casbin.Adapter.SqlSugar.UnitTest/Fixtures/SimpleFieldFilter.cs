using System;
using System.Linq;
using Casbin.Model;
using Casbin.Persist;
using Casbin.Adapter.SqlSugar.Entities;

#nullable enable

namespace Casbin.Adapter.SqlSugar.UnitTest.Fixtures
{
    /// <summary>
    /// Simple field-based policy filter for testing.
    /// Implements "empty string = ignore this field" semantics.
    /// </summary>
    public class SimpleFieldFilter : IPolicyFilter
    {
        public string PolicyType { get; }
        public int FieldIndex { get; }
        public IPolicyValues Values { get; }

        public SimpleFieldFilter(string policyType, int fieldIndex, IPolicyValues values)
        {
            PolicyType = policyType;
            FieldIndex = fieldIndex;
            Values = values ?? throw new ArgumentNullException(nameof(values));
        }

        public IQueryable<T> Apply<T>(IQueryable<T> policies) where T : IPersistPolicy
        {
            // Database-friendly path: IQueryable<CasbinRule>
            if (policies is IQueryable<CasbinRule> q)
            {
                var query = q;

                if (!string.IsNullOrWhiteSpace(PolicyType))
                {
                    // Use PType (mapped column), not Type (ignored by ORM)
                    query = query.Where(r => r.PType == PolicyType);
                }

                for (int i = 0; i < Values.Count; i++)
                {
                    var v = Values[i];
                    if (string.IsNullOrWhiteSpace(v)) continue;

                    int idx = FieldIndex + i;
                    switch (idx)
                    {
                        case 0:  query = query.Where(r => r.V0  == v); break;
                        case 1:  query = query.Where(r => r.V1  == v); break;
                        case 2:  query = query.Where(r => r.V2  == v); break;
                        case 3:  query = query.Where(r => r.V3  == v); break;
                        case 4:  query = query.Where(r => r.V4  == v); break;
                        case 5:  query = query.Where(r => r.V5  == v); break;
                        case 6:  query = query.Where(r => r.V6  == v); break;
                        case 7:  query = query.Where(r => r.V7  == v); break;
                        case 8:  query = query.Where(r => r.V8  == v); break;
                        case 9:  query = query.Where(r => r.V9  == v); break;
                        case 10: query = query.Where(r => r.V10 == v); break;
                        case 11: query = query.Where(r => r.V11 == v); break;
                        case 12: query = query.Where(r => r.V12 == v); break;
                        case 13: query = query.Where(r => r.V13 == v); break;
                        case 14: query = query.Where(r => r.V14 == v); break;
                        default: break;
                    }
                }

                return query.Cast<T>();
            }

            // In-memory path: use reflection and ignore blank values
            var filtered = policies.AsEnumerable()
                .Where(r =>
                {
                    var t = r!.GetType();

                    if (!string.IsNullOrWhiteSpace(PolicyType))
                    {
                        var typeProp = t.GetProperty("Type") ?? t.GetProperty("PType");
                        var typeVal = (string)(typeProp?.GetValue(r) ?? string.Empty);
                        if (!string.Equals(typeVal, PolicyType, StringComparison.Ordinal))
                            return false;
                    }

                    for (int i = 0; i < Values.Count; i++)
                    {
                        var v = Values[i];
                        if (string.IsNullOrWhiteSpace(v)) continue;

                        int idx = FieldIndex + i;
                        var prop = t.GetProperty($"Value{idx}") ?? t.GetProperty($"V{idx}");
                        var val = prop?.GetValue(r) as string;
                        if (!string.Equals(val, v, StringComparison.Ordinal))
                            return false;
                    }

                    return true;
                })
                .AsQueryable();

            return filtered;
        }
    }
}
