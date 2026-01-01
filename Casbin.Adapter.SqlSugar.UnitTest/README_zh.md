# SqlSugar 适配器单元测试

本项目包含 `Casbin.Adapter.SqlSugar` 类库的 **单元测试**。与集成测试不同，这些测试旨在利用轻量级数据库（SqlSugar 内置的 SQLite 支持）快速验证逻辑，无需复杂的外部依赖。

## 测试目的

- **快速反馈**：秒级运行，验证核心逻辑正确性。
- **向后兼容性**：确保标准单上下文 API 的行为与原版 EF Core 适配器保持一致。
- **功能验证**：隔离测试过滤加载 (Filtered Loading)、多上下文路由 (Multi-Context) 和依赖注入逻辑。

## 测试结构

| 测试类 | 描述 |
|--------|------|
| `BackwardCompatibilityTest` | **向后兼容性测试**。验证标准的单上下文操作 (Add, Remove, Update) 是否按预期工作，确保现有用户的无缝迁移。 |
| `MultiContextTest` | **多上下文测试**。专门验证 `ISqlSugarClientProvider` 是否能正确地将策略路由到不同的表或配置中。 |
| `DependencyInjectionTest` | **依赖注入测试**。验证类库的 DI 扩展方法能否正确注册 Casbin 标准接口服务。 |
| `AutoTest` | 验证内部的自动保存 (Auto-Save) 逻辑和状态管理。 |

## 前置要求

- **无**：这些测试使用 SqlSugar 自动管理的 SQLite（通常是内存或文件数据库）。无需安装外部数据库软件。

## 运行测试

支持 .NET 8.0, 9.0 和 10.0。

### 运行所有单元测试

```bash
dotnet test Casbin.Adapter.SqlSugar.UnitTest
or
dotnet test Casbin.Adapter.SqlSugar.UnitTest -c Release
```

### 运行特定测试

```bash
dotnet test --filter "FullyQualifiedName~BackwardCompatibilityTest"
```
