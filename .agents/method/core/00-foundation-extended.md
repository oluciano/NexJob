# NexJob — AI Foundation (Extended)

**Read this for complex scenarios, broker triggers, storage providers, or architectural compliance checks.**

---

## Architecture Reference

See `docs/ARCHITECTURE.md` for the authoritative system design.

---

## Dispatcher & Executor Architecture

### JobDispatcherService manages the polling loop and worker concurrency
`JobDispatcherService` is a stateless polling service that:
- Periodically queries `IJobStorage.FetchNextAsync` for eligible jobs across queues.
- Manages concurrency worker slots and respects queue-level pauses and circuit breakers.
- Hands each fetched job to `IJobExecutor.ExecuteJobAsync`.

### Hosting Lifecycle on .NET 10
In `Microsoft.Extensions.Hosting` 10, `BackgroundService.StartAsync` no longer executes `ExecuteAsync` up to its first `await`.
Any validation or startup check that must fail fast during host startup MUST live in an explicit `StartAsync` override, never in `ExecuteAsync`.

### JobExecutor is the execution pipeline orchestrator
Extracted from the dispatcher in v3, `JobExecutor` executes a single job through defined stages:
- `TryHandleExpirationAsync` — deadline check before execution starts.
- `_invokerFactory.PrepareAsync` — type resolution, schema migration, deserialization, and DI scope (`JobInvocationContext`).
- `ExecuteWithThrottlingAndFiltersAsync` — local/distributed throttle acquisition + filter pipeline + job invocation.
- `HandleFailureAsync` — retry delay calculation via `IJobRetryPolicy` and dead-letter dispatch via `IDeadLetterDispatcher`.
- `RecordSuccessMetrics` — OpenTelemetry metrics and duration recording.

### JobInvocationContext owns the DI scope
`JobInvocationContext` implements `IDisposable` and disposes the job's DI scope on `Dispose()`.
Always use `using var context = await _invokerFactory.PrepareAsync(job, ct)` — never dispose manually.

### Decision logging is mandatory at decision points
The dispatcher must log the *reason* for every automatic decision:
- Queue skipped (paused or outside window) → `LogDebug`
- Throttle wait started → `LogDebug`
- Retry scheduled → `LogInformation` with delay and scheduled time
- Dead-letter → `LogError`

---

## Storage Architecture & Contract Segregation

### Segregated Storage Contracts
NexJob separates storage concerns into three distinct contracts:
- `IJobStorage` — hot-path execution (FetchNext, CommitJobResult, Heartbeat, AcquireLock).
- `IRecurringStorage` — recurring job scheduling and cron evaluation.
- `IDashboardStorage` — read-heavy queries for metrics, job lists, and server status.
- `IStorageProvider` — composed interface uniting all three contracts.

**Rule:** Core interfaces (`IJobStorage`, `IRecurringStorage`, `IDashboardStorage`) are stable contracts and must never be modified without architect approval.

### Read Replica Support
PostgreSQL and SQL Server support `UseDashboardReadReplica()` to route `IDashboardStorage` queries to secondary nodes, protecting hot execution paths from dashboard query loads.

### Mongo Serializer Bootstrap
In MongoDB integration, registering serializers after driver defaults are cached causes `BsonSerializationException`. Always ensure `MongoSerializerBootstrap` registers custom serializers prior to container or provider instantiation.

---

## Trigger Implementation Contract (The 5 Guarantees)

Every trigger provider (RabbitMQ, Kafka, Azure Service Bus, AWS SQS, Google Pub/Sub, Salesforce) must satisfy all 5 guarantees:

1. **Never silently drop:** If `IScheduler.EnqueueAsync` fails, the message must be routed to the broker dead-letter destination or rethrown for broker-level retry.
2. **Idempotency:** Always map the broker's native message ID to `idempotencyKey` when enqueuing.
3. **Trace propagation:** Extract `traceparent` from broker headers and assign to `JobRecord.TraceParent` to ensure distributed tracing continuity.
4. **Signal after enqueue:** Handled internally by `IScheduler.EnqueueAsync` (never invoke `_wakeUpChannel.Signal()` directly from triggers).
5. **Ack only after storage write:** Never acknowledge the broker message before `IScheduler.EnqueueAsync` completes successfully.

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
7. Zero warnings in Release builds (`TreatWarningsAsErrors = true`) on both `net8.0` and `net10.0`

---

## Testing Strategy

### Reliability Suite
- Project: `NexJob.ReliabilityTests`
- Validates scenarios against **real storage providers** via Docker.
- Coverage: Retry & Dead-Letter, Concurrency, Crash Recovery, Deadline Enforcement, Wake-Up Latency.
- Providers: PostgreSQL 16, SQL Server 2022, Redis 7, MongoDB 7.

### Test Integrity & No Fixed Delay
- Never wait with a fixed delay (`Task.Delay(50)`).
- Use `TestWait.UntilAsync`, `TestWait.SucceededAsync`, `TestWait.InvokedAsync`, or `TaskCompletionSource`.
- Every feature or bug fix must provide a **3N Mandatory Matrix** (Positive, Negative, Boundary).

---

## Architecture Compliance Checklist

Verify these during implementation/fix:
- Respects Job model (`IJob` and `IJob<T>`).
- Does not introduce unnecessary DTOs.
- Does not bypass storage as source of truth.
- Does not introduce static/global state.
- Designs don't prevent deterministic testing.
- No in-memory optimization overrides storage truth.
- Compiles with 0 warnings on `net8.0` and `net10.0`.

---

## Code Quality Requirements

### Public API
- All public types and members must have XML documentation (`///`).
- Add overloads, never optional parameters to public interfaces (preserves binary/mock compatibility).

### Async & Concurrency
- Never use `.Result` or `.Wait()`.
- Always use `async/await`.
- Always propagate `CancellationToken`.
- Never ignore cancellation.
- Always use `.ConfigureAwait(false)` in library projects, EXCEPT in Blazor component rendering (`src/NexJob.Dashboard/Pages` and components) where `DispatcherSynchronizationContext` must be preserved.

### Class Design
- Classes must be `sealed` by default.
- Only allow inheritance when explicitly required by architecture.

### Exception Handling
- Never silently swallow exceptions (log or rethrow).
- Dead-letter handler errors are exceptions (swallow only, log always).
