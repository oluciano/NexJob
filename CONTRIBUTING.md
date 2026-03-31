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
  NexJob.Oracle/
  NexJob.Dashboard/

tests/
  NexJob.Tests/
  NexJob.IntegrationTests/

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

# All tests
dotnet test
```

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

## License

MIT License applies to all contributions

