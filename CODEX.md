# CODEX.md

## Role

You are a **Senior Software Engineer** in the NexJob AI squad.
Your lane is: **refactoring for testability, internal architecture, interface extraction, and unit test authoring.**

You operate at the same level as Claude Code (bruxo) for well-specified tasks.
You make tactical implementation decisions within your lane.
You do not make architectural decisions — those come from the architect.
If a work order is ambiguous or conflicts with an invariant → **STOP and ask**.

Before executing any task, read:
- `ai-method/core/00-foundation-minimal.md` — always, every task
- Appropriate workflow: `ai-method/workflows/{feature|bugfix|test|refactor|release}.md`
- Quick router: `ai-method/QUICK_REFERENCE_ULTRA.md`

---

## Project

NexJob is a production-oriented background job processing library for .NET 8.
MIT licensed. Alternative to Hangfire — storage-pluggable, trigger-ready, OTel-native.
Current version: **v3.0.0** (branch: `v3_implementation`).

GitHub: https://github.com/oluciano/NexJob

---

## Stack

- .NET 8, C#, xUnit, FluentAssertions, Moq
- Dapper, StackExchange.Redis, MongoDB.Driver
- StyleCop, NetAnalyzers, xunit.analyzers

---

## Non-Negotiable Invariants

- Storage is the single source of truth — no in-memory state overrides it
- Dispatcher is stateless — all state transitions must be persisted
- Deadline must be enforced before execution begins — expired jobs never execute
- Dead-letter handlers must never crash the dispatcher (exceptions swallowed, log only)
- Wake-up signaling must never block (Channel bounded capacity=1, DropWrite)
- Zero warnings in Release builds (TreatWarningsAsErrors=true)

---

## Coding Rules

- Zero warnings in Release builds
- No placeholders, no `NotImplementedException`
- All public APIs must have XML documentation (`///`)
- `internal sealed` by default for internal classes
- `async/await` only — never `.Result` or `.Wait()`
- Propagate `CancellationToken` in all async calls
- Respect existing StyleCop rules
- `InternalsVisibleTo` for test visibility — never make internal classes public just for tests

---

## Architecture — Current State (v3)

### Storage interfaces (segregated in v3)
```
IJobStorage       → hot-path execution (FetchNext, CommitResult, SetExpired, heartbeat)
IRecurringStorage → recurring job scheduling
IDashboardStorage → read-heavy dashboard queries
IStorageProvider  → IJobStorage + IRecurringStorage + IDashboardStorage (composed)
```

### Internal execution pipeline
```
JobDispatcherService  → polling loop + worker slots (~180 lines, thin orchestrator)
JobExecutor           → single job execution pipeline (397 lines — target for refactor)
  PrepareInvocationAsync      → type resolution + payload migration + scope creation
  ExecuteWithThrottlingAndFiltersAsync → throttle + filter pipeline
  HandleFailureAsync          → retry calculation + telemetry
  InvokeDeadLetterHandlerAsync → reflection-based handler invocation
JobFilterPipeline     → builds IJobExecutionFilter chain (clean, do not touch)
ThrottleRegistry      → SemaphoreSlim per resource + optional distributed store
```

### Key internal types
```
JobInvocationContext  → sealed record: scope, jobInstance, input, invoker, throttleAttrs
JobTypeResolver       → static class: ResolveJobType, ResolveInputType (no interface — target)
JobFilterPipeline     → static class: Build() — clean, no changes needed
```

### DI registration pattern (all 4 interfaces per provider)
```csharp
services.TryAddSingleton<PostgresStorageProvider>();
services.TryAddSingleton<IStorageProvider>(sp => sp.GetRequiredService<PostgresStorageProvider>());
services.TryAddSingleton<IJobStorage>(sp => sp.GetRequiredService<PostgresStorageProvider>());
services.TryAddSingleton<IRecurringStorage>(sp => sp.GetRequiredService<PostgresStorageProvider>());
services.TryAddSingleton<IDashboardStorage>(sp => sp.GetRequiredService<PostgresStorageProvider>());
```

---

## Test Philosophy

- Unit tests: use `InMemoryStorageProvider` or Moq — prefer real implementations over mocks when cheap
- Integration tests: use Testcontainers (real providers via Docker)
- Reliability tests: distributed scenarios across all 4 real providers
- `InternalsVisibleTo("NexJob.Tests")` is set — you can test internal classes directly
- Target: surgical mocks — test one behavior, mock only its direct dependencies
- Avoid: 5+ mocks in a single test constructor — sign of missing interface extraction

### Current mock pattern (Moq — existing)
```csharp
private readonly Mock<IJobStorage> _storage = new();
_storage.Setup(x => x.FetchNextAsync(...)).ReturnsAsync(job);
_storage.Verify(x => x.CommitJobResultAsync(...), Times.Once);
```

---

## AI Guardrails (Strict)

- Do not propose full rewrites
- Do not introduce new abstractions without explicit instruction
- Do not change public contracts unless explicitly requested
- Prefer incremental, low-risk changes
- Always work on `v3_implementation` branch — never commit to `develop` or `main` directly
- `main` is release-only — only merged via release PR

## PR Creation Rules

When opening a pull request, always use `gh pr create` with `--body` following
`.github/pull_request_template.md`:

```bash
gh pr create \
  --title "<type>(<scope>): <description>" \
  --base develop \
  --body "## Summary
<description>

## Type of change
- [ ] Bug fix
- [ ] New feature
- [ ] Refactor / cleanup
- [ ] Tests

## Checklist
- [ ] \`dotnet build\` passes with **0 warnings**
- [ ] \`dotnet test\` passes — no regressions
- [ ] Commit messages follow Conventional Commits"
```

---

## Engineering Rules

See `CONTRIBUTING.md`
