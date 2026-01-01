# Versioning and Release Standard

This document defines the standards for version numbering and release management for the `Casbin.Adapter.SqlSugar` project.

## Versioning Strategy (SemVer)

We utilize [Semantic Versioning 2.0.0](https://semver.org/).
Version Number Format: `MAJOR.MINOR.PATCH` (e.g., `1.0.1`)

| Increment Type | Rule | Example Scenario |
| :--- | :--- | :--- |
| **MAJOR** | Incompatible API changes | Removing public methods, changing method signatures, dropping support for a framework version. |
| **MINOR** | Backwards-compatible functionality | Adding new adapter features (e.g., new database support, new API methods), non-breaking internal Refactoring. |
| **PATCH** | Backwards-compatible bug fixes | Fixing data inconsistency, resolving crashes, updating documentation, updating dependencies for security. |

### Decision Logic for this Release (1.0.1 vs 1.1.0)
- **Current Situation**: Added transaction support to key methods (`SavePolicy`, `UpdatePolicies`, etc.).
- **Analysis**: The lack of transactions in these methods constitutes a *data integrity defect* (bug) rather than a new feature. Users expect `SavePolicy` to be atomic.
- **Decision**: **1.0.1 (PATCH)**. This emphasizes that it is a critical fix for existing functionality.

## Commit Message Convention

We follow the [Conventional Commits](https://www.conventionalcommits.org/) specification. This structure allows for automatic changelog generation.

**Format**: `<type>(<scope>): <description>`

### Types
| Type | Description | Release Category |
| :--- | :--- | :--- |
| **feat** | A new feature | 🚀 Features |
| **fix** | A bug fix | 🐛 Bug Fixes |
| **docs** | Documentation only changes | 📚 Documentation |
| **style** | Formatting, missing semi-colons, etc. | 🧰 Maintenance |
| **refactor** | Code change that neither fixes a bug nor adds a feature | 🧰 Maintenance |
| **perf** | A code change that improves performance | 🏃 Performance |
| **test** | Adding missing tests or correcting existing tests | 🧰 Maintenance |
| **chore** | Build process, auxiliary tools, dependency updates | 🧰 Maintenance |

### Example
```text
fix(adapter): enforce transaction consistency in batch operations

- Wrapped UpdatePolicies and RemovePolicies in transactions
- Fixed atomicity issue in UpdatePolicy (delete+insert)
- Added TRANSACTION_FIXES_zh.md documentation
```

## Release Process

1. **Update Version**: Update `<Version>` in `Casbin.Adapter.SqlSugar.csproj`.
2. **Commit**: Commit changes with a standard message.
3. **Tag**: Create a git tag `v1.0.1`.
4. **Push**: Push code and tags to GitHub.
   ```bash
   git push origin main --tags
   ```
5. **Auto-Release**: GitHub Actions will detect the tag and trigger the deployment workflow.
