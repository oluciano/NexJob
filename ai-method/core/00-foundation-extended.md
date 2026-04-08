# NexJob — AI Foundation (Extended)

**Read this for complex scenarios or architectural compliance checks.**

---

## Architecture Reference

See `ARCHITECTURE.md` for the authoritative system design.

---

## Dispatcher Architecture

### JobDispatcherService is an orchestrator
`ExecuteJobAsync` is a thin orchestrator — it calls private named methods for each stage.
Do not add inline business logic to it. Each stage has a single responsibility:

- `TryHandleExpirationAsync` — deadline check only
- `PrepareInvocationAsync` — type resolution, migration, deserialization, DI scope
- `ExecuteWithThrottlingAndFiltersAsync` — throttle acquisition + filter pipeline + job invocation
- `HandleFailureAsync` — retry calculation + decision logging
- `RecordSuccessMetrics` — metrics only

### JobInvocationContext owns the DI scope
`JobInvocationContext` implements `IDisposable` and disposes the DI scope on `Dispose()`.
Always use `using var context = await PrepareInvocationAsync(job)` — never dispose manually.

### Decision logging is mandatory at decision points
The dispatcher must log the *reason* for every automatic decision:
- Queue skipped (paused or outside window) → `LogDebug`
- Throttle wait started → `LogDebug`
- Retry scheduled → `LogInformation` with delay and scheduled time
- Dead-letter → `LogError`

---

## Job Filter Pipeline

### IJobExecutionFilter wraps job execution
Filters are resolved from the job's DI scope via `IEnumerable<IJobExecutionFilter>`.
They execute in DI registration order. The job invocation is the terminal step.

**Rule:** Never add inline cross-cutting logic to `ExecuteWithThrottlingAndFiltersAsync`.
Register an `IJobExecutionFilter` instead.

### Fast path when no filters registered
When `_filters.Count == 0`, the job is invoked directly without allocating `JobExecutingContext`
or building the pipeline. Zero overhead for the common case.

**Rule:** Always check `_filters.Count == 0` before building the pipeline.

### Filter exceptions propagate normally
A filter that throws is treated as a job failure. The normal retry and dead-letter flow applies.
Do not swallow exceptions in the dispatcher — let them propagate to `HandleFailureAsync`.

### JobExecutingContext lifecycle
`context.Succeeded` and `context.Exception` are set by the dispatcher after the pipeline runs,
not by the filter itself. Filters read these values after calling `await next(ct)`.

---

## Retention Policy

### JobRetentionService reads effective policy on every cycle
The retention service reads `IRuntimeSettingsStore` on every purge cycle so that
dashboard overrides take effect without a restart.

**Rule:** Never cache the retention policy between cycles. Always read from the store.

### PurgeJobsAsync never touches active jobs
`PurgeJobsAsync` only deletes jobs in terminal states: `Succeeded`, `Failed`, `Expired`.
Jobs in `Enqueued`, `Processing`, `Scheduled`, or `AwaitingContinuation` must never be deleted
by the retention service.

**Rule:** Every `PurgeJobsAsync` implementation must filter by terminal status before deleting.

### TimeSpan.Zero disables purging for that status
When a retention threshold is `TimeSpan.Zero`, skip purging for that status entirely.

**Rule:** Check `policy.RetainX > TimeSpan.Zero` before executing any DELETE.

---

## Runtime Settings Persistence

### IRuntimeSettingsStore is implemented by all persistent providers
PostgreSQL, SQL Server, Redis, and MongoDB each implement `IRuntimeSettingsStore` and persist
settings in a dedicated table/key (`nexjob_settings`). The in-memory store is volatile by design.

**Rule:** When adding a new persistent storage provider, implement `IRuntimeSettingsStore`
and register it in `AddNexJobXxx()`.

### RegisterCore uses TryAdd — provider registration wins
`NexJobServiceCollectionExtensions.RegisterCore` uses `TryAddSingleton<IRuntimeSettingsStore, InMemoryRuntimeSettingsStore>`.
Provider extension methods register before `AddNexJob()` is called, so their registration takes precedence.

**Rule:** Always register `IRuntimeSettingsStore` in `AddNexJobXxx()` alongside `IStorageProvider`.

---

## Design Constraints (Runtime Guarantees)

1. Wake-up signaling must never block (bounded channel, capacity 1, collapses signals)
2. Deadline must be enforced before execution begins (check after fetch, before invoke)
3. Dead-letter handlers must never crash dispatcher (swallow exceptions, log only)
4. Simple jobs must remain simple (`IJob` needs no input complexity)
5. No unnecessary DTO requirements (input must be minimal and reproducible)
6. Storage is authoritative for all state (no cache optimizations override storage)
7. Zero warnings in Release builds (treat warnings as errors)

---

## Testing Strategy

### Reliability Suite
- Project: `NexJob.ReliabilityTests.Distributed`
- Validates scenarios against **real storage providers** via Docker.
- Coverage: Retry & Dead-Letter, Concurrency, Crash Recovery, Deadline Enforcement, Wake-Up Latency.
- Providers: PostgreSQL 16, SQL Server 2022, Redis 7, MongoDB 7.

---

## Architecture Compliance Checklist

Verify these during implementation/fix:
- Respects Job model (`IJob` and `IJob<T>`).
- Does not introduce unnecessary DTOs.
- Does not bypass storage as source of truth.
- Does not introduce static/global state.
- Designs don't prevent deterministic testing.
- No in-memory optimization overrides storage truth.

---

## Code Quality Requirements

### Public API
- All public types and members must have XML documentation (`///`).

### Async & Concurrency
- Never use `.Result` or `.Wait()`.
- Always use `async/await`.
- Always propagate `CancellationToken`.
- Never ignore cancellation.

### Class Design
- Classes must be `sealed` by default.
- Only allow inheritance when explicitly required by architecture.

### Exception Handling
- Never silently swallow exceptions (log or rethrow).
- Dead-letter handler errors are exceptions (swallow only, log always).
