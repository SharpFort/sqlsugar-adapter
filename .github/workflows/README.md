# NuGet Publishing Workflow Guide

This document explains how to use the GitHub Actions workflow to automatically publish NuGet packages.

## 📋 Prerequisites

Before you can use this workflow, you need to set up a NuGet API key in your GitHub repository secrets.

### 1. Get Your NuGet API Key

1. Go to [NuGet.org](https://www.nuget.org/)
2. Sign in to your account
3. Click on your username → **API Keys**
4. Click **Create** to generate a new API key
5. Configure the key:
   - **Key Name**: `GitHub Actions - SqlSugar Adapter`
   - **Select Scopes**: Check `Push` and `Push new packages and package versions`
   - **Select Packages**: Choose `Casbin.NET.Adapter.SqlSugar` (or select all packages)
   - **Glob Pattern**: `Casbin.NET.Adapter.SqlSugar*`
6. Click **Create**
7. **Copy the API key** (you won't be able to see it again!)

### 2. Add API Key to GitHub Secrets

1. Go to your GitHub repository: `https://github.com/SharpFort/sqlsugar-adapter`
2. Click **Settings** → **Secrets and variables** → **Actions**
3. Click **New repository secret**
4. Add the secret:
   - **Name**: `NUGET_API_KEY`
   - **Secret**: Paste your NuGet API key
5. Click **Add secret**

---

## 🚀 Publishing Methods

### Method 1: Automatic Publishing via Git Tags (Recommended)

This is the recommended approach for production releases.

#### Steps:

1. **Update version in project file** (if needed):
   ```xml
   <!-- Casbin.Adapter.SqlSugar/Casbin.Adapter.SqlSugar.csproj -->
   <PropertyGroup>
     <Version>1.0.0</Version>
   </PropertyGroup>
   ```

2. **Commit your changes**:
   ```bash
   git add .
   git commit -m "chore: prepare release v1.0.0"
   git push origin main
   ```

3. **Create and push a version tag**:
   ```bash
   # Create a tag (e.g., v1.0.0)
   git tag v1.0.0
   
   # Push the tag to GitHub
   git push origin v1.0.0
   ```

4. **Workflow automatically triggers**:
   - Builds the project
   - Runs all unit and integration tests
   - Packs the NuGet package with version `1.0.0`
   - Publishes to NuGet.org
   - Creates a GitHub Release with the package attached

#### Tag Naming Convention:
- Use semantic versioning: `v<major>.<minor>.<patch>`
- Examples: `v1.0.0`, `v2.1.3`, `v1.0.0-beta.1`

---

### Method 2: Manual Publishing via GitHub UI

Use this for testing or one-off releases.

#### Steps:

1. Go to your repository on GitHub
2. Click **Actions** tab
3. Select **Publish to NuGet** workflow
4. Click **Run workflow** dropdown
5. Enter the version (e.g., `1.0.0`) - **without the 'v' prefix**
6. Click **Run workflow**

The workflow will:
- Build and test the project
- Pack with the specified version
- Publish to NuGet.org
- **Note**: This method does NOT create a GitHub Release

---

## 📊 Workflow Details

### Workflow Triggers

```yaml
on:
  push:
    tags:
      - 'v*.*.*'  # Triggers on tags like v1.0.0
  workflow_dispatch:  # Manual trigger
```

### Jobs Overview

#### Job 1: Build and Test
- Checks out code
- Sets up .NET 8.0 and 9.0
- Restores dependencies
- Builds in Release configuration
- Runs unit tests
- Runs integration tests
- Uploads test results as artifacts

#### Job 2: Publish
- Runs only if tests pass
- Extracts version from tag or manual input
- Builds and packs the NuGet package
- Publishes to NuGet.org
- Creates GitHub Release (for tag-based triggers)

---

## 🔍 Monitoring the Workflow

### View Workflow Status

1. Go to **Actions** tab in your repository
2. Click on the workflow run
3. View the progress of each job
4. Check logs for any errors

### Test Results

Test results are uploaded as artifacts and can be downloaded from the workflow run page.

---

## 📦 Package Verification

After publishing, verify your package:

1. **NuGet.org**: Visit `https://www.nuget.org/packages/Casbin.NET.Adapter.SqlSugar`
2. **Check version**: Ensure the new version appears
3. **Download count**: Monitor adoption
4. **GitHub Release**: Check the release page for the package artifact

---

## 🛠️ Troubleshooting

### Common Issues

#### 1. "API key is invalid or has expired"
- **Solution**: Regenerate your NuGet API key and update the GitHub secret

#### 2. "Package already exists"
- **Solution**: The workflow uses `--skip-duplicate` flag, so this shouldn't fail the workflow
- Ensure you're using a new version number

#### 3. "Tests failed"
- **Solution**: The publish job won't run if tests fail
- Check the test logs in the Actions tab
- Fix the failing tests and push again

#### 4. "Version mismatch"
- **Solution**: Ensure the tag version matches the version in the `.csproj` file
- The workflow uses the tag version for packaging

---

## 📝 Best Practices

### Version Management

1. **Use Semantic Versioning**:
   - `MAJOR.MINOR.PATCH`
   - Increment MAJOR for breaking changes
   - Increment MINOR for new features
   - Increment PATCH for bug fixes

2. **Pre-release Versions**:
   - Use tags like `v1.0.0-beta.1`, `v1.0.0-rc.1`
   - These will be marked as pre-release on NuGet

3. **Keep Changelog**:
   - Update CHANGELOG.md before each release
   - GitHub Release notes are auto-generated from commits

### Release Checklist

- [ ] All tests passing locally
- [ ] Version updated in `.csproj` (if applicable)
- [ ] CHANGELOG.md updated
- [ ] Documentation updated
- [ ] Commit and push changes
- [ ] Create and push version tag
- [ ] Monitor workflow execution
- [ ] Verify package on NuGet.org
- [ ] Test package installation in a sample project

---

## 🔐 Security Notes

- **Never commit API keys** to the repository
- Use GitHub Secrets for sensitive data
- Rotate API keys periodically
- Use scoped API keys (not full access)
- Review workflow permissions regularly

---

## 📚 Additional Resources

- [NuGet Documentation](https://docs.microsoft.com/nuget/)
- [GitHub Actions Documentation](https://docs.github.com/actions)
- [Semantic Versioning](https://semver.org/)
- [Creating GitHub Releases](https://docs.github.com/repositories/releasing-projects-on-github)

---

## 🎯 Quick Reference

### Publish a New Version

```bash
# 1. Update version and commit
git add .
git commit -m "chore: prepare release v1.2.3"
git push

# 2. Create and push tag
git tag v1.2.3
git push origin v1.2.3

# 3. Wait for workflow to complete
# 4. Verify on NuGet.org
```

### Delete a Tag (if needed)

```bash
# Delete local tag
git tag -d v1.0.0

# Delete remote tag
git push origin :refs/tags/v1.0.0
```

---

## ✅ Workflow File Location

The workflow file is located at:
```
.github/workflows/nuget-publish.yml
```

Any changes to this file will be automatically picked up by GitHub Actions.
