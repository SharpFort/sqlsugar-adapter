 # NuGet 发布工作流指南

本文档说明如何使用 GitHub Actions 工作流自动发布 NuGet 包。

## 📋 前置要求

在使用此工作流之前，您需要在 GitHub 仓库密钥中设置 NuGet API 密钥。

### 1. 获取 NuGet API 密钥

1. 访问 [NuGet.org](https://www.nuget.org/)
2. 登录您的账户
3. 点击用户名 → **API Keys**
4. 点击 **Create** 生成新的 API 密钥
5. 配置密钥：
   - **Key Name**: `GitHub Actions - SqlSugar Adapter`
   - **Select Scopes**: 勾选 `Push` 和 `Push new packages and package versions`
   - **Select Packages**: 选择 `Casbin.NET.Adapter.SqlSugar`（或选择所有包）
   - **Glob Pattern**: `Casbin.NET.Adapter.SqlSugar*`
6. 点击 **Create**
7. **复制 API 密钥**（您将无法再次查看它！）

### 2. 将 API 密钥添加到 GitHub Secrets

1. 访问您的 GitHub 仓库：`https://github.com/SharpFort/sqlsugar-adapter`
2. 点击 **Settings** → **Secrets and variables** → **Actions**
3. 点击 **New repository secret**
4. 添加密钥：
   - **Name**: `NUGET_API_KEY`
   - **Secret**: 粘贴您的 NuGet API 密钥
5. 点击 **Add secret**

---

## 🚀 发布方法

### 方法 1: 通过 Git 标签自动发布（推荐）

这是生产环境发布的推荐方法。

#### 步骤：

1. **更新项目文件中的版本**（如需要）：
   ```xml
   <!-- Casbin.Adapter.SqlSugar/Casbin.Adapter.SqlSugar.csproj -->
   <PropertyGroup>
     <Version>1.0.0</Version>
   </PropertyGroup>
   ```

2. **提交更改**：
   ```bash
   git add .
   git commit -m "chore: prepare release v1.0.0"
   git push origin main
   ```

3. **创建并推送版本标签**：
   ```bash
   # 创建标签（例如 v1.0.0）
   git tag v1.0.0
   
   # 推送标签到 GitHub
   git push origin v1.0.0
   ```

4. **工作流自动触发**：
   - 构建项目
   - 运行所有单元测试和集成测试
   - 打包 NuGet 包（版本 `1.0.0`）
   - 发布到 NuGet.org
   - 创建 GitHub Release 并附带包文件

#### 标签命名规范：
- 使用语义化版本：`v<主版本>.<次版本>.<修订版本>`
- 示例：`v1.0.0`、`v2.1.3`、`v1.0.0-beta.1`

---

### 方法 2: 通过 GitHub UI 手动发布

用于测试或一次性发布。

#### 步骤：

1. 在 GitHub 上访问您的仓库
2. 点击 **Actions** 标签
3. 选择 **Publish to NuGet** 工作流
4. 点击 **Run workflow** 下拉菜单
5. 输入版本号（例如 `1.0.0`）- **不带 'v' 前缀**
6. 点击 **Run workflow**

工作流将：
- 构建并测试项目
- 使用指定版本打包
- 发布到 NuGet.org
- **注意**：此方法不会创建 GitHub Release

---

## 📊 工作流详情

### 触发条件

```yaml
on:
  push:
    tags:
      - 'v*.*.*'  # 匹配 v1.0.0 等标签
  workflow_dispatch:  # 手动触发
```

### 作业概览

#### 作业 1: 构建和测试
- 检出代码
- 设置 .NET 8.0 和 9.0
- 恢复依赖项
- Release 配置构建
- 运行单元测试
- 运行集成测试
- 上传测试结果为工件

#### 作业 2: 发布
- 仅在测试通过后运行
- 从标签或手动输入提取版本
- 构建并打包 NuGet 包
- 发布到 NuGet.org
- 创建 GitHub Release（仅标签触发）

---

## 🔍 监控工作流

### 查看工作流状态

1. 访问仓库的 **Actions** 标签
2. 点击工作流运行
3. 查看每个作业的进度
4. 检查日志中的错误

### 测试结果

测试结果作为工件上传，可从工作流运行页面下载。

---

## 📦 包验证

发布后验证您的包：

1. **NuGet.org**: 访问 `https://www.nuget.org/packages/Casbin.NET.Adapter.SqlSugar`
2. **检查版本**: 确保新版本出现
3. **下载计数**: 监控采用情况
4. **GitHub Release**: 检查发布页面的包工件

---

## 🛠️ 故障排除

### 常见问题

#### 1. "API key is invalid or has expired"（API 密钥无效或已过期）
- **解决方案**: 重新生成 NuGet API 密钥并更新 GitHub 密钥

#### 2. "Package already exists"（包已存在）
- **解决方案**: 工作流使用 `--skip-duplicate` 标志，因此不会导致失败
- 确保使用新的版本号

#### 3. "Tests failed"（测试失败）
- **解决方案**: 如果测试失败，发布作业不会运行
- 在 Actions 标签中检查测试日志
- 修复失败的测试并重新推送

#### 4. "Version mismatch"（版本不匹配）
- **解决方案**: 确保标签版本与 `.csproj` 文件中的版本匹配
- 工作流使用标签版本进行打包

---

## 📝 最佳实践

### 版本管理

1. **使用语义化版本**：
   - `主版本.次版本.修订版本`
   - 破坏性更改时增加主版本
   - 新功能时增加次版本
   - Bug 修复时增加修订版本

2. **预发布版本**：
   - 使用标签如 `v1.0.0-beta.1`、`v1.0.0-rc.1`
   - 这些将在 NuGet 上标记为预发布

3. **维护变更日志**：
   - 每次发布前更新 CHANGELOG.md
   - GitHub Release 注释从提交自动生成

### 发布检查清单

- [ ] 本地所有测试通过
- [ ] 更新 `.csproj` 中的版本（如适用）
- [ ] 更新 CHANGELOG.md
- [ ] 更新文档
- [ ] 提交并推送更改
- [ ] 创建并推送版本标签
- [ ] 监控工作流执行
- [ ] 在 NuGet.org 上验证包
- [ ] 在示例项目中测试包安装

---

## 🔐 安全注意事项

- **永远不要提交 API 密钥**到仓库
- 使用 GitHub Secrets 存储敏感数据
- 定期轮换 API 密钥
- 使用范围限定的 API 密钥（非完全访问）
- 定期审查工作流权限

---

## 📚 其他资源

- [NuGet 文档](https://docs.microsoft.com/nuget/)
- [GitHub Actions 文档](https://docs.github.com/actions)
- [语义化版本](https://semver.org/)
- [创建 GitHub Releases](https://docs.github.com/repositories/releasing-projects-on-github)

---

## 🎯 快速参考

### 发布新版本

```bash
# 1. 更新版本并提交
git add .
git commit -m "chore: prepare release v1.2.3"
git push

# 2. 创建并推送标签
git tag v1.2.3
git push origin v1.2.3

# 3. 等待工作流完成
# 4. 在 NuGet.org 上验证
```

### 删除标签（如需要）

```bash
# 删除本地标签
git tag -d v1.0.0

# 删除远程标签
git push origin :refs/tags/v1.0.0
```

---

## ✅ 工作流文件位置

工作流文件位于：
```
.github/workflows/nuget-publish.yml
```

对此文件的任何更改都将被 GitHub Actions 自动识别。
