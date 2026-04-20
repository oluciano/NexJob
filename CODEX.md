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
- **80% Unit Coverage** — strictly enforced via CI for all new code
- **Must-Have Testing Matrix** — every feature must cover: Retry & Dead-Letter, Concurrency, Crash Recovery, Deadline Enforcement, and Wake-Up Latency
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
JobDispatcherService       → polling loop + worker slots (~180 lines, thin orchestrator)
JobExecutor                → single job execution pipeline (~260 lines, clean orchestrator)
  _invokerFactory.PrepareAsync()     → IJobInvokerFactory — type resolution + scope
  ExecuteWithThrottlingAndFiltersAsync → throttle + filter pipeline
  HandleFailureAsync                 → telemetry + delegates to IJobRetryPolicy
  _deadLetterDispatcher.DispatchAsync() → IDeadLetterDispatcher — handler invocation

IJobInvokerFactory / DefaultJobInvokerFactory
  → type resolution, payload migration, DI scope creation, compiled invoker cache

IJobRetryPolicy / DefaultJobRetryPolicy
  → retry delay calculation (RetryAttribute vs NexJobOptions.RetryDelayFactory)
  → pure function: ComputeRetryAt(job, exception) → DateTimeOffset?

IDeadLetterDispatcher / DefaultDeadLetterDispatcher
  → resolves IDeadLetterHandler<TJob> from DI scope
  → invokes handler, swallows exceptions (invariant: never crash dispatcher)

JobFilterPipeline     → builds IJobExecutionFilter chain (clean, do not touch)
ThrottleRegistry      → SemaphoreSlim per resource + optional IDistributedThrottleStore
```

### Key internal types

```
JobInvocationContext  → sealed record: scope, jobInstance, input, invoker, throttleAttrs
                        owned by IJobInvokerFactory — caller must dispose
JobTypeResolver       → static class: ResolveJobType, ResolveInputType
                        used by DefaultJobInvokerFactory and DefaultDeadLetterDispatcher
JobFilterPipeline     → static class: Build() — clean, do not touch
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
- Target: JobExecutorTests has exactly 4 mocks: IJobStorage, IJobInvokerFactory,
  IJobRetryPolicy, IDeadLetterDispatcher — this is the baseline, do not regress
- Avoid: 5+ mocks in a single test constructor — sign of missing interface extraction

### Current mock pattern (Moq — established baseline)

```csharp
// JobExecutorTests baseline — 4 mocks:
private readonly Mock<IJobStorage> _storage = new();
private readonly Mock<IJobInvokerFactory> _invokerFactory = new();
private readonly Mock<IJobRetryPolicy> _retryPolicy = new();
private readonly Mock<IDeadLetterDispatcher> _deadLetterDispatcher = new();

// Default happy-path setup:
_invokerFactory
    .Setup(x => x.PrepareAsync(It.IsAny<JobRecord>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync((JobRecord job, CancellationToken _) => MakeContext(job));
_retryPolicy
    .Setup(x => x.ComputeRetryAt(It.IsAny<JobRecord>(), It.IsAny<Exception>()))
    .Returns(DateTimeOffset.UtcNow.AddMinutes(1));
```

---

## Test Integrity (Universal — All Squad Members)

### 3N Mandatory Matrix
Every feature or bug fix must produce minimum 3 tests:
- **N1 — Positive:** happy path works as expected
- **N2 — Negative:** failure path fails as expected
- **N3 — Invalid Input:** null, empty, boundary — handled gracefully

### Existing Tests Are Immutable Contracts
NEVER rewrite, rename, or delete a passing test to make new code pass.
When a test breaks after a change: fix the production code, not the test.
Only valid reason to change a test: behavior was explicitly changed by the architect.
If changed: add comment `// Behavior changed in vX.Y: <reason>`.

800 tests that can be rewritten on demand are worth less than 10 that cannot.

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
