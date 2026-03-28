# NexJob — Project Context for Claude Code

> This file is read automatically by Claude Code in every session.
> It contains the full architectural decisions, conventions, and roadmap
> agreed upon during the design phase of this project.

---

## What is NexJob?

NexJob is a **background job scheduler library for .NET 8+**, built as a modern,
fully open-source alternative to Hangfire. The core philosophy is:

> Install the NuGet package, implement one interface, add two lines of config — done.

**Key differentiators from Hangfire:**
- MIT license end-to-end (no paid tiers, ever)
- Native `async/await` — not bolted on
- Priority queues built-in (Critical → High → Normal → Low)
- Resource throttling via `[Throttle]` attribute
- OpenTelemetry built-in, not a plugin (🔜 v0.6)
- Idempotency keys on enqueue
- Payload versioning with `IJobMigration<TOld, TNew>` (🔜 v0.6)
- All storage adapters free and open-source

---

## Name & Meaning

**NexJob** — "Nex" comes from *nexus* (latin) and *nexo* (portuguese):
the connection point between your application and its background work.
Also reads as "Next Job" — always knowing what runs next.

---

## Non-Negotiables (Mandatory for every PR/Iteration)

To maintain high code quality and library reliability, the following rules are strictly enforced:

1.  **Zero `NotImplementedException`:** No stubs or placeholders in merged code. Features must be fully functional or not present.
2.  **Zero Compiler Warnings:** `dotnet build --configuration Release` must result in 0 warnings.
3.  **Public API Documentation:** Every public type and member **must** have XML documentation (`///`).
4.  **Testing Coverage:** Every new behavior, bug fix, or feature must be accompanied by corresponding unit or integration tests.
5.  **Strict Async/Await:** * No `.Result` or `.Wait()`. Use `async/await` throughout the entire call stack.
    * `CancellationToken` must always be propagated; never ignored.
6.  **Sealed by Default:** All classes must be marked as `sealed` unless explicit inheritance is part of the architectural design.
7.  **Documentation Sync:** * `README.md` must be updated to remove any "coming soon" symbols (🔜) for features that are now implemented.
    * `CLAUDE.md` must be updated with any new architectural decisions or pattern changes.

## Coding Conventions

- **Language:** C# 12, .NET 8, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`
- **Warnings:** `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
- **Private fields:** `_camelCase` with underscore prefix
- **Async:** every public method that does I/O must be `async Task` or `async Task<T>`
- **CancellationToken:** always propagate, never ignore
- **Internal classes:** go in `src/NexJob/Internal/`, not public API
- **No static state** except `ThrottleRegistry` (singleton service) and invoker cache in `JobDispatcherService`
- **XML docs** on all public types and members
- **Tests:** xUnit + FluentAssertions, one test class per production class

## Repository Structure

```
NexJob/
├── CLAUDE.md                        ← you are here
├── README.md
├── CONTRIBUTING.md
├── LICENSE                          ← MIT
├── Directory.Build.props            ← shared MSBuild props for all projects
├── .editorconfig
├── NexJob.sln
├── .github/
│   ├── workflows/
│   │   └── ci.yml                   ← dotnet build + test on push
│   ├── ISSUE_TEMPLATE/
│   │   ├── bug_report.yml
│   │   ├── feature_request.yml
│   │   └── new_storage_provider.yml
│   └── pull_request_template.md
├── src/
│   ├── NexJob/                      ← core package (IJob, IScheduler, models)
│   │   ├── IJob.cs
│   │   ├── IScheduler.cs
│   │   ├── Models.cs                ← JobId, JobRecord, RecurringJobRecord, enums
│   │   ├── Configuration.cs         ← NexJobOptions, AddNexJob() extension
│   │   ├── Extensibility.cs         ← ThrottleAttribute, IJobMigration<,>
│   │   ├── Storage/
│   │   │   └── IStorageProvider.cs
│   │   └── Internal/                ← not public API
│   │       ├── DefaultScheduler.cs
│   │       ├── JobDispatcherService.cs
│   │       ├── RecurringJobSchedulerService.cs
│   │       ├── OrphanedJobWatcherService.cs
│   │       ├── ThrottleRegistry.cs
│   │       └── InMemoryStorageProvider.cs
│   ├── NexJob.Postgres/             ← ✅ fully implemented
│   ├── NexJob.MongoDB/              ← ✅ fully implemented
│   ├── NexJob.SqlServer/            ← 🔜 stub only
│   ├── NexJob.Redis/                ← 🔜 stub only
│   ├── NexJob.Oracle/               ← 🔜 stub only
│   └── NexJob.Dashboard/            ← ✅ Blazor SSR middleware
├── tests/
│   ├── NexJob.Tests/                ← ✅ 33 unit tests (in-memory + scheduler)
│   ├── NexJob.IntegrationTests/     ← Testcontainers (Postgres + Mongo, requires Docker)
│   └── NexJob.MongoDB.Tests/        ← MongoDB-specific tests
└── samples/
    └── NexJob.Sample.WebApi/        ← ASP.NET minimal API demo (in-memory storage)
```

---

## Core Interfaces (implemented in src/NexJob/)

### IJob<TInput>
```csharp
public interface IJob<TInput>
{
    Task ExecuteAsync(TInput input, CancellationToken cancellationToken);
}
```

### IScheduler
```csharp
public interface IScheduler
{
    Task<JobId> EnqueueAsync<TJob, TInput>(TInput input, string? queue = null,
        JobPriority priority = JobPriority.Normal, string? idempotencyKey = null,
        CancellationToken cancellationToken = default) where TJob : IJob<TInput>;

    Task<JobId> ScheduleAsync<TJob, TInput>(TInput input, TimeSpan delay,
        string? queue = null, string? idempotencyKey = null,
        CancellationToken cancellationToken = default) where TJob : IJob<TInput>;

    Task<JobId> ScheduleAtAsync<TJob, TInput>(TInput input, DateTimeOffset runAt,
        string? queue = null, string? idempotencyKey = null,
        CancellationToken cancellationToken = default) where TJob : IJob<TInput>;

    Task RecurringAsync<TJob, TInput>(string recurringJobId, TInput input,
        string cron, TimeZoneInfo? timeZone = null, string? queue = null,
        CancellationToken cancellationToken = default) where TJob : IJob<TInput>;

    Task<JobId> ContinueWithAsync<TJob, TInput>(JobId parentJobId, TInput input,
        string? queue = null, CancellationToken cancellationToken = default)
        where TJob : IJob<TInput>;

    Task RemoveRecurringAsync(string recurringJobId,
        CancellationToken cancellationToken = default);
}
```

### IStorageProvider
All methods documented in `src/NexJob/Storage/IStorageProvider.cs`.
Critical: `FetchNextAsync` must use atomic dequeue (SELECT FOR UPDATE SKIP LOCKED
or equivalent) to prevent double-processing across multiple workers/servers.

---

## Models

```csharp
readonly record struct JobId(Guid Value)

enum JobPriority  { Critical=1, High=2, Normal=3, Low=4 }
enum JobStatus    { Enqueued, Scheduled, Processing, Succeeded, Failed,
                    Deleted, AwaitingContinuation }

class JobRecord          // persisted job — includes RecurringJobId to link back to its cron definition
class RecurringJobRecord // persisted cron definition — includes LastExecutionStatus/LastExecutionError
```

Key fields worth noting:
- `JobRecord.RecurringJobId` — set when a job is spawned from a recurring definition; used by `JobDispatcherService` to update `LastExecutionStatus` on completion.
- `RecurringJobRecord.LastExecutionStatus` / `LastExecutionError` — populated after each execution; shown as ✓/✗ badge in the dashboard.

---

## Architecture Decisions

### Worker pool
Use `SemaphoreSlim` to control concurrency. Each worker creates a fresh
DI scope (`IServiceScopeFactory.CreateScope()`) per job execution.
Never share scoped services (DbContext, etc.) between jobs.

### Retry policy
Exponential backoff: `delay = pow(attempt, 4) + 15 + random(30) * (attempt + 1)` seconds.
After `MaxAttempts`, move job to dead-letter (status = Failed, no more retries).

### Heartbeat & orphan detection
Workers update `HeartbeatAt` every `HeartbeatInterval` (default 30s).
`OrphanedJobWatcherService` runs periodically and requeues jobs where
`HeartbeatAt < now - HeartbeatTimeout` (default 5min) and status = Processing.

### Active Server Tracking
`ServerHeartbeatService` (an internal `IHostedService`) registers the worker node on startup, updates its node heartbeat (`heartbeat_at`) every 15 seconds, and deregisters upon graceful shutdown. The dashboard aggregates these active servers mapping.

### Recurring jobs
Use `Cronos` NuGet package for cron parsing and next-execution calculation.
`RecurringJobSchedulerService` polls storage for due jobs (`NextExecution <= utcNow`)
and enqueues them as normal `JobRecord`s (with `RecurringJobId` set), then updates `NextExecution`.
After the job finishes, `JobDispatcherService` calls `SetRecurringJobLastExecutionResultAsync`.

### Serialization
Use `System.Text.Json`. Store `SchemaVersion` alongside payload for future migration support.
Never serialize `Expression<Func<>>` — always `JobType + InputType + InputJson`.

### Idempotency
On enqueue, if `IdempotencyKey` is provided and a job with that key already
exists in `Enqueued`, `Processing`, `Scheduled`, or `AwaitingContinuation` state,
skip silently and return existing `JobId`.

### Resource throttling
`[Throttle(resource, maxConcurrent)]` uses a named `SemaphoreSlim` registry
(singleton, `ThrottleRegistry`) keyed by resource name. Enforced in the execution
pipeline before `ExecuteAsync` is called.

### Storage provider registration
`AddNexJob()` uses `TryAddSingleton<IStorageProvider, InMemoryStorageProvider>` —
it only registers in-memory if no provider is already registered. To use Postgres or MongoDB,
call `AddNexJobPostgres(...)` or `AddNexJobMongoDB(...)` **before** `AddNexJob()`.

### Dashboard
Pure C# `IComponent` implementations (no .razor files) rendered via `HtmlRenderer`.
Mounted via `app.UseNexJobDashboard(pathPrefix)`. Default prefix: `/dashboard`.
Supports: overview, queues, jobs (paginated/filtered), job detail, recurring, failed.
Bulk actions (trigger/delete/requeue) via HTML form POST with checkbox selection.

---

## NuGet Package Split

| Package | Contains | Status |
|---|---|---|
| `NexJob` | Core interfaces + models + in-memory provider + DI extensions | ✅ |
| `NexJob.Postgres` | PostgreSQL storage adapter (Npgsql + Dapper) | ✅ |
| `NexJob.MongoDB` | MongoDB adapter (MongoDB.Driver) | ✅ |
| `NexJob.Dashboard` | Blazor SSR dashboard middleware | ✅ |
| `NexJob.SqlServer` | SQL Server adapter (Microsoft.Data.SqlClient) | 🔜 stub |
| `NexJob.Redis` | Redis adapter (StackExchange.Redis) | 🔜 stub |
| `NexJob.Oracle` | Oracle adapter (Oracle.ManagedDataAccess.Core) | 🔜 stub |

---

## Roadmap

```
v0.1  ✅ Core interfaces · in-memory provider · fire-and-forget
v0.2  ✅ PostgreSQL provider · delayed jobs · recurring (cron) · dashboard (Blazor SSR)
v0.3  ✅ Priority queues · resource throttling · continuations · bulk actions
v0.4  ✅ MongoDB provider · integration tests (Testcontainers) · CONTRIBUTING.md
v0.5  ○ Active Servers & Workers mapping · SQL Server · Redis · Oracle providers
v0.6  ○ OpenTelemetry (Activity spans per job) · IJobMigration<TOld,TNew> · SchemaVersion migration
v0.7  ○ Dashboard real-time updates (SSE or SignalR) · NuGet packaging · CI publishing
v1.0  ○ Stable API · production-ready · published to NuGet.org
```

**Current:** between v0.4 and v0.5 — core is solid, focusing on quality and additional providers.

---

## Testing Strategy

### Integration Tests Isolation
To ensure test reliability and prevent "flaky" results in both local environments (Ubuntu/Docker) and GitHub Actions, we follow a **Strict Physical Isolation** strategy for database providers:

- **Database-per-Test (SQL Server & Postgres):**
  - **Problem:** Shared databases suffer from identity cache issues, transaction locks, and leftover data, causing count mismatches (e.g., "Expected 5, found 12").
  - **Solution:** Every test execution must create a unique database using `Guid.NewGuid()`.
  - **Implementation:** Override `CreateStorageAsync` in the test class to generate a dynamic `InitialCatalog`, execute `CREATE DATABASE` on the master node, and then initialize the provider pointing to this new instance.

- **Database-per-Class (MongoDB):**
  - **Solution:** Use `client.DropDatabase("nexjob_test")` in the constructor to ensure a clean slate. Since Mongo handles dynamic collection creation gracefully, dropping the entire database is the most reliable reset.

- **In-Memory Reset (Redis):**
  - **Solution:** Execute `FLUSHDB` via `redis-cli` or the C# driver at the start of each test suite to clear all keys and metadata.

### Test Environment Requirements
- **Docker:** Required for all integration tests via `Testcontainers`.
- **Hardware:** Tests are optimized for high-concurrency environments but designed to pass in resource-constrained CI environments (GitHub Actions) due to the physical isolation of databases.

## Developer Contact

- GitHub: https://github.com/oluciano/NexJob
- Author: Luciano Azevedo
