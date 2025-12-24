# Unit Tests for SqlSugar Adapter

This project contains **unit tests** for the `Casbin.Adapter.SqlSugar` library. Unlike integration tests, these tests are designed to run quickly using lightweight databases (SQLite within SqlSugar) to verify logic without complex external dependencies.

## Purpose

- **fast feedback**: Run in seconds to verify core logic.
- **Backward Compatibility**: Ensure APIs from the original EF Core adapter continue to work.
- **Feature Verification**: Test filtered loading, multi-context routing, and dependency injection in isolation.

## Test Structure

| Test Class | Description |
|------------|-------------|
| `BackwardCompatibilityTest` | Verifies that standard single-context operations (Add, Remove, Update) work as expected, maintaining compatibility with the original adapter behavior. |
| `MultiContextTest` | specialized tests for the **Multi-Context** feature, verifying that the `ISqlSugarClientProvider` correctly routes policies to different tables/configs. |
| `DependencyInjectionTest` | Verifies that the library's DI extension methods correctly register services with the standardized Casbin interfaces. |
| `AutoTest` | Validates internal auto-save logic and state management. |

## Prerequisites

- **None**: These tests use SQLite (often in-memory or file-based) handled automatically by SqlSugar. No external database installation is required.

## Running Tests

Supports .NET 8.0, 9.0, and 10.0.

### Run All Unit Tests

```bash
dotnet test Casbin.Adapter.SqlSugar.UnitTest
```

### Run specific test

```bash
dotnet test --filter "FullyQualifiedName~BackwardCompatibilityTest"
```
