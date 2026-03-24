# Contributing to NexJob

Thank you for your interest in NexJob. Contributions of any kind are welcome — bug reports, feature ideas, documentation improvements, and code.

---

## Before you start

- Search [existing issues](https://github.com/oluciano/NexJob/issues) before opening a new one.
- For significant changes (new features, architectural decisions, new storage providers), open an issue first to discuss the approach.
- All contributions must be compatible with the **MIT license**.

---

## Development setup

```bash
git clone git@github.com:oluciano/NexJob.git
cd NexJob
dotnet restore
dotnet build
dotnet test
```

Requirements:
- .NET 8 SDK or later
- Docker (for integration tests with Testcontainers — optional, skip if unavailable)

---

## Project structure

```
src/
  NexJob/                 ← Core interfaces, models, in-memory provider
  NexJob.Postgres/        ← PostgreSQL storage adapter
  NexJob.MongoDB/         ← MongoDB storage adapter
  NexJob.Dashboard/       ← Blazor SSR dashboard middleware
  NexJob.{Redis,SqlServer,Oracle}/  ← Other adapters (stubs)

tests/
  NexJob.Tests/           ← Unit tests (no external dependencies)
  NexJob.MongoDB.Tests/   ← MongoDB integration tests (requires MongoDB)
  NexJob.IntegrationTests/← Contract tests via Testcontainers (requires Docker)

samples/
  NexJob.Sample.WebApi/   ← Minimal API demonstrating all features
```

---

## Coding conventions

- **C# 12**, .NET 8, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` — PRs must build with zero warnings
- Private fields: `_camelCase` with underscore prefix
- Every public type and member must have XML documentation (`///`)
- `async/await` for all I/O — never `.Result` or `.Wait()`
- Always propagate `CancellationToken` — never ignore it
- Internal implementation classes go in `src/NexJob/Internal/`

---

## Adding a storage provider

1. Create `src/NexJob.{Name}/` with its own `.csproj`
2. Implement `IStorageProvider` — all methods, including the dashboard ones
3. Use an atomic dequeue strategy:
   - PostgreSQL/SQL Server: `SELECT FOR UPDATE SKIP LOCKED`
   - MongoDB: `findOneAndUpdate` with a filter on `status = Enqueued`
   - Redis: Lua scripts
4. Add `AddNexJob{Name}(this IServiceCollection, string connectionString)` extension method
5. Add the project to `NexJob.sln`
6. Your implementation will automatically be exercised by `StorageProviderTestsBase` — add a test class:

```csharp
public sealed class MyProviderTests : StorageProviderTestsBase, IAsyncLifetime
{
    // spin up container, return provider
}
```

---

## Running tests

```bash
# Unit tests only (no Docker required)
dotnet test tests/NexJob.Tests/

# MongoDB integration tests (requires local MongoDB or Docker)
dotnet test tests/NexJob.MongoDB.Tests/

# Full contract tests via Testcontainers (requires Docker)
dotnet test tests/NexJob.IntegrationTests/

# Everything
dotnet test
```

---

## Pull request checklist

- [ ] `dotnet build` passes with **0 warnings**
- [ ] `dotnet test` passes — no regressions
- [ ] New behaviour is covered by tests
- [ ] Public API has XML documentation
- [ ] Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/):
  `feat:`, `fix:`, `docs:`, `chore:`, `test:`, `refactor:`

---

## Commit message format

```
feat: add execution window support per queue
fix: prevent double-dequeue on concurrent workers
docs: update README with appsettings example
test: add contract tests for SetFailedAsync
```

---

## License

By contributing, you agree that your contribution will be licensed under the [MIT License](LICENSE).
