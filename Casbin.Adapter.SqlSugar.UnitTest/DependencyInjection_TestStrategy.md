# SqlSugar vs EFCore 依赖注入测试策略分析

## 核心结论

### ✅ SqlSugar 不需要 EFCore 的 `ShouldUseAdapterAcrossMultipleScopesWithServiceProvider` 测试

**原因**: 架构差异导致测试策略不同

---

## 架构差异对比

### EFCore 的"续命"问题

**问题**: `DbContext` 有严格的生命周期限制
- ❌ Scope 释放后 `DbContext.Dispose()` 被调用
- ❌ 连接关闭，无法继续使用
- ✅ 解决方案：适配器持有 `IServiceProvider`，每次操作时重新解析 `DbContext`

**EFCore 原测试目的**:
```csharp
[Fact]
public void ShouldUseAdapterAcrossMultipleScopesWithServiceProvider()
{
    // 适配器持有 IServiceProvider（而非 DbContext）
    var adapter = new EFCoreAdapter<int>(_testHostFixture.Services);
    
    using (var scope1 = _testHostFixture.Services.CreateScope())
    {
        var dbContext = scope1.ServiceProvider.GetRequiredService<CasbinDbContext<int>>();
        dbContext.Database.EnsureCreated();
    } // scope1 释放，dbContext 被 Dispose
    
    // ⚠️ 关键：Scope 释放后仍能工作
    adapter.LoadPolicy(model); // 内部重新解析 DbContext
}
```

### SqlSugar 的"复活甲"机制

**特性**: `IsAutoCloseConnection = true` 自动管理连接
- ✅ 每次操作自动打开/关闭连接
- ✅ `ISqlSugarClient` 无生命周期限制
- ✅ Scope 释放后仍可正常使用

**配置**:
```csharp
// TestHostFixture.cs
.AddSqlSugarCasbinAdapter(config =>
{
    config.ConnectionString = $"Data Source={uniqueDbName}";
    config.DbType = DbType.Sqlite;
    config.IsAutoCloseConnection = true;  // 🛡️ 复活甲
});
```

---

## SqlSugar 替代测试

### 测试 1: `ShouldUseAdapterWithServiceProvider`

**目的**: 验证 Scope 释放后客户端仍可用

```csharp
[Fact]
public void ShouldUseAdapterWithServiceProvider()
{
    ISqlSugarClient client;
    
    using (var scope = _testHostFixture.Services.CreateScope())
    {
        client = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        client.CodeFirst.InitTables<CasbinRule>();
    } // Scope 释放
    
    var adapter = new SqlSugarAdapter(client);
    adapter.LoadPolicy(model); // ✅ 仍能正常工作
}
```

**验证点**:
- ✅ Scope 释放后客户端仍可用
- ✅ 无需 `IServiceProvider` 构造函数
- ✅ `IsAutoCloseConnection` 机制正常工作

### 测试 2: `ShouldWorkWithScopedLifetime`

**目的**: 验证 Scoped 生命周期和跨 Scope 使用

```csharp
[Fact]
public void ShouldWorkWithScopedLifetime()
{
    IAdapter adapter1;
    IAdapter adapter2;
    
    using (var scope1 = _testHostFixture.Services.CreateScope())
    {
        adapter1 = scope1.ServiceProvider.GetRequiredService<IAdapter>();
        adapter1.LoadPolicy(model);
    } // scope1 释放
    
    using (var scope2 = _testHostFixture.Services.CreateScope())
    {
        adapter2 = scope2.ServiceProvider.GetRequiredService<IAdapter>();
        adapter2.LoadPolicy(model);
    } // scope2 释放
    
    Assert.NotSame(adapter1, adapter2); // 验证 Scoped 生命周期
}
```

**验证点**:
- ✅ Scoped 生命周期正确（不同 Scope 产生不同实例）
- ✅ Scope 外仍可使用适配器
- ✅ DI 集成正确

---

## 测试覆盖对比

| 测试场景 | EFCore 原测试 | SqlSugar 替代测试 | 覆盖效果 |
|---------|--------------|------------------|----------|
| 跨 Scope 使用适配器 | `ShouldUseAdapterAcrossMultipleScopesWithServiceProvider` | `ShouldUseAdapterWithServiceProvider` | ✅ 完全覆盖 |
| Scope 释放后仍可用 | 同上 | `ShouldUseAdapterWithServiceProvider` | ✅ 完全覆盖 |
| Scoped 生命周期验证 | 隐含验证 | `ShouldWorkWithScopedLifetime` | ✅ 显式验证 |
| DI 容器集成 | 同上 | `ShouldWorkWithScopedLifetime` | ✅ 显式验证 |

---

## 为什么不需要强行匹配？

### 架构决定测试策略

| 方面 | EFCore | SqlSugar |
|------|--------|----------|
| **连接管理** | 手动（DbContext 生命周期） | 自动（IsAutoCloseConnection） |
| **Scope 依赖** | 强依赖（必须在 Scope 内） | 无依赖 |
| **跨 Scope 使用** | 需要 IServiceProvider 延迟解析 | 直接使用客户端即可 |
| **测试重点** | 验证延迟解析机制 | 验证自动连接管理 |
| **测试复杂度** | 高 | 低 |

### SqlSugar 的优势

```csharp
// EFCore 必须这样做
var adapter = new EFCoreAdapter<int>(serviceProvider); // 传入 IServiceProvider
// 内部每次操作都要重新解析 DbContext

// SqlSugar 直接使用即可
var adapter = new SqlSugarAdapter(client); // 传入 ISqlSugarClient
// IsAutoCloseConnection 自动管理连接
```

---

## 额外测试覆盖

SqlSugar 测试套件还包含以下额外测试:

| 测试 | 目的 |
|------|------|
| `ShouldResolveCasbinClient` | 验证 `ISqlSugarClient` 注册 |
| `ShouldResolveSqlSugarAdapter` | 验证 `IAdapter` 注册 |
| `ShouldUseAdapterAcrossMultipleScopesWithClientDirectly` | 验证不通过 DI 也能跨 Scope 使用 |
| `ShouldResolveAdapterRegisteredWithExtensionMethod` | 验证 `AddSqlSugarCasbinAdapter` 扩展方法 |

---

## 最终结论

### ✅ SqlSugar 的测试策略更优

**不是简单的"移除"，而是基于架构特性的优化**:

1. **完全覆盖原测试目的**: 两个替代测试充分验证了跨 Scope 使用场景
2. **更清晰的验证**: 显式验证 Scoped 生命周期（原测试只是隐含）
3. **更全面的覆盖**: 额外测试了 DI 扩展方法和直接使用场景
4. **更简洁的实现**: 无需复杂的 IServiceProvider 延迟解析机制

### 核心原因

- **EFCore**: DbContext 为了"活下去"需要通过 ServiceProvider 续命
- **SqlSugar**: 自带"复活甲"（`IsAutoCloseConnection`），无需续命

**这是更符合 SqlSugar 架构特性的测试策略！** 🎯
