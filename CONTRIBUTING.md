# Contributing to NexJob

Thank you for your interest in NexJob. Contributions of any kind are welcome — bug reports, feature ideas, documentation improvements, and code.

---

## Before you start

* Search existing issues before opening a new one
* For significant changes, open an issue first
* All contributions must be compatible with the MIT license

---

## Development setup

```bash
git clone git@github.com:oluciano/NexJob.git
cd NexJob
dotnet restore
dotnet build
dotnet test
```

### Requirements

* .NET 8 SDK or later
* Docker (optional, for integration tests via Testcontainers)

---

## Project structure

```
src/
  NexJob/
  NexJob.Postgres/
  NexJob.MongoDB/
  NexJob.Redis/
  NexJob.SqlServer/
  NexJob.Dashboard/

tests/
  NexJob.Tests/
  NexJob.IntegrationTests/
  NexJob.ReliabilityTests/
  NexJob.ReliabilityTests.Distributed/

samples/
  NexJob.Sample.WebApi/
```

---

## Running tests

```bash
# Unit tests
dotnet test tests/NexJob.Tests/

# Integration tests (requires Docker)
dotnet test tests/NexJob.IntegrationTests/

# Distributed reliability tests (requires Docker)
# Runs all scenarios across real storage providers via Testcontainers
dotnet test tests/NexJob.ReliabilityTests.Distributed -c Release --verbosity normal

# Single provider reliability tests
dotnet test tests/NexJob.ReliabilityTests.Distributed -c Release \
  --filter "Category=Reliability.Distributed&ClassName~Postgres"

dotnet test tests/NexJob.ReliabilityTests.Distributed -c Release \
  --filter "Category=Reliability.Distributed&ClassName~SqlServer"

dotnet test tests/NexJob.ReliabilityTests.Distributed -c Release \
  --filter "Category=Reliability.Distributed&ClassName~Redis"

dotnet test tests/NexJob.ReliabilityTests.Distributed -c Release \
  --filter "Category=Reliability.Distributed&ClassName~Mongo"

# Single test category across all providers
dotnet test tests/NexJob.ReliabilityTests.Distributed -c Release \
  --filter "Category=Reliability.Distributed&ClassName~RetryAndDeadLetter"

dotnet test tests/NexJob.ReliabilityTests.Distributed -c Release \
  --filter "Category=Reliability.Distributed&ClassName~Concurrency"

dotnet test tests/NexJob.ReliabilityTests.Distributed -c Release \
  --filter "Category=Reliability.Distributed&ClassName~Recovery"

dotnet test tests/NexJob.ReliabilityTests.Distributed -c Release \
  --filter "Category=Reliability.Distributed&ClassName~Deadline"

dotnet test tests/NexJob.ReliabilityTests.Distributed -c Release \
  --filter "Category=Reliability.Distributed&ClassName~WakeUpLatency"

# All tests
dotnet test
```

## Distributed Reliability Testing

The `NexJob.ReliabilityTests.Distributed` project validates all scenarios against **real storage providers** via Docker (Testcontainers). This ensures production readiness across all supported backends.

### What's Tested

- **Retry & Dead-Letter**: Retry execution, handler invocation, exception resilience
- **Concurrency**: Duplicate prevention, concurrent enqueue, stress scenarios
- **Crash Recovery**: Job persistence, state consistency across restarts
- **Deadline Enforcement**: Expiration handling, deadline before execution
- **Wake-Up Latency**: Signaling efficiency, queue-specific dispatch

### Providers

- PostgreSQL 16
- SQL Server 2022
- Redis 7
- MongoDB 7

### Requirements

- Docker (Testcontainers manages container lifecycle)
- `.NET 8 SDK`
- `dotnet test` (xUnit runs with full isolation per fixture)

---

## Adding a storage provider

1. Create `src/NexJob.{Name}/`
2. Implement `IStorageProvider`
3. Ensure atomic dequeue strategy
4. Add DI extension
5. Add to solution
6. Add integration tests

---

## Branch workflow

```
feat/...
fix/...
docs/...
chore/...
test/...
```

```bash
git checkout -b feat/my-feature
git push -u origin feat/my-feature
gh pr create
```

## Branch Protection & Release Workflow

- `main` — protected. PR required. CI must pass. No direct pushes.
- `develop` — protected. PR required. CI must pass. No direct pushes.
- All work starts from `develop`. Features branch off `develop`.
- `main` only receives merges from `develop` at release time.

### Release Process (Avoiding commit divergence)

⚠️ **Critical**: `main` must NEVER have commits that `develop` doesn't have. This prevents permanent desynchronization.

**After release in `develop` (when tag is created):**

```bash
# 1. Create a clean release branch (no merge commit)
git checkout -b release/vX.Y.Z-to-main develop

# 2. Push the branch
git push -u origin release/vX.Y.Z-to-main

# 3. Create PR from release/vX.Y.Z-to-main → main
gh pr create --title "release: merge vX.Y.Z to main" \
  --base main \
  --body "Release vX.Y.Z from develop to main (fast-forward)"

# 4. Merge the PR via GitHub UI (will be fast-forward since branch tracks develop)

# 5. After PR is merged, cleanup
git checkout develop
git branch -d release/vX.Y.Z-to-main
```

**Why this works:**
- Release branch is created FROM develop (same commits)
- PR from release branch → main is always fast-forward
- No extra merge commit is created in main
- main stays synchronized with develop
- Both branches point to the same release tags

---

## Pull request checklist

* [ ] Build passes with 0 warnings
* [ ] Tests pass
* [ ] Feature covered by tests
* [ ] Public API documented
* [ ] Commit messages follow Conventional Commits

---

## Commit message format

```
feat: add execution window support
fix: prevent double-dequeue
docs: update README
test: add tests
```

---

## Non-Negotiables (Mandatory for every PR/Iteration)

1. Zero `NotImplementedException`
2. Zero compiler warnings
3. Public API must have XML docs
4. All changes must have tests
5. Strict async/await usage
6. Classes must be sealed by default
7. Documentation must be updated

---

## Coding Conventions

* C# 12 / .NET 8
* Nullable enabled
* Treat warnings as errors
* `_camelCase` private fields
* Async everywhere for I/O
* Always propagate `CancellationToken`
* Internal code → `Internal/`
* No static state (except allowed cases)
* Tests: xUnit + FluentAssertions

---

## Testing Strategy

### Isolation

* SQL: database-per-test
* Mongo: drop database
* Redis: flush DB

### Requirements

* Docker required for integration tests
* Testcontainers used for all providers

---

---

## SQL storage provider conventions

### Read replica support

SQL providers support read replica via a constructor overload that skips migrations:

```csharp
// Primary — runs migrations
public MyProvider(string connectionString, NexJobOptions options) { ... }

// Read replica — skips migrations, declared with explicit remarks
/// <remarks>Migrations are NOT applied. Intended for read replica use only.</remarks>
public MyProvider(DbConnection connection, NexJobOptions options) { ... }
```

`UseDashboardReadReplica` registers this overload as `IDashboardStorage` only.

### Dapper configuration

Set `Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true` in the **primary
constructor only**. This is a process-wide static — one call is sufficient.

## License

MIT License applies to all contributions

