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
- OpenTelemetry built-in, not a plugin
- Idempotency keys on enqueue
- Payload versioning with `IJobMigration<TOld, TNew>`
- All storage adapters free and open-source

---

## Name & Meaning

**NexJob** — "Nex" comes from *nexus* (latin) and *nexo* (portuguese):
the connection point between your application and its background work.
Also reads as "Next Job" — always knowing what runs next.

---

## Repository Structure

```
NexJob/
├── CLAUDE.md                        ← you are here
├── README.md
├── LICENSE                          ← MIT
├── Directory.Build.props            ← shared MSBuild props for all projects
├── .editorconfig
├── NexJob.sln
├── .github/
│   └── workflows/
│       └── ci.yml                   ← dotnet build + test on push
├── src/
│   ├── NexJob/                      ← core package (IJob, IScheduler, models)
│   │   ├── IJob.cs
│   │   ├── IScheduler.cs
│   │   ├── Models.cs                ← JobId, JobRecord, JobStatus, JobPriority...
│   │   ├── Configuration.cs         ← NexJobOptions, AddNexJob() extension
│   │   ├── Extensibility.cs         ← ThrottleAttribute, IJobMigration<,>
│   │   ├── Storage/
│   │   │   └── IStorageProvider.cs
│   │   └── Internal/                ← not public API
│   │       ├── DefaultScheduler.cs
│   │       ├── JobDispatcherService.cs
│   │       ├── RecurringJobSchedulerService.cs
│   │       ├── OrphanedJobWatcherService.cs
│   │       └── InMemoryStorageProvider.cs
│   ├── NexJob.Postgres/
│   ├── NexJob.SqlServer/
│   ├── NexJob.Redis/
│   ├── NexJob.MongoDB/
│   ├── NexJob.Oracle/
│   └── NexJob.Dashboard/
└── tests/
    ├── NexJob.Tests/
    └── NexJob.IntegrationTests/
```

---

## Core Interfaces (already implemented in src/NexJob/)

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

class JobRecord          // persisted job
class RecurringJobRecord // persisted cron definition
```

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

### Recurring jobs
Use `Cronos` NuGet package for cron parsing and next-execution calculation.
`RecurringJobSchedulerService` polls storage for due jobs (`NextExecution <= utcNow`)
and enqueues them as normal `JobRecord`s, then updates `NextExecution`.

### Serialization
Use `System.Text.Json` with source generators where possible.
Store `SchemaVersion` alongside payload for migration support.
Never serialize `Expression<Func<>>` — always `JobType + InputType + InputJson`.

### Idempotency
On enqueue, if `IdempotencyKey` is provided and a job with that key already
exists in `Enqueued` or `Processing` state, skip silently and return existing `JobId`.

### Resource throttling
`[Throttle(resource, maxConcurrent)]` uses a named `SemaphoreSlim` registry
(singleton) keyed by resource name. Enforced in the execution pipeline
before `ExecuteAsync` is called.

---

## Coding Conventions

- **Language:** C# 12, .NET 8, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`
- **Warnings:** `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
- **Private fields:** `_camelCase` with underscore prefix
- **Async:** every public method that does I/O must be `async Task` or `async Task<T>`
- **CancellationToken:** always propagate, never ignore
- **Internal classes:** go in `src/NexJob/Internal/`, not public API
- **No static state** except the throttle semaphore registry (which is a singleton service)
- **XML docs** on all public types and members
- **Tests:** xUnit + FluentAssertions, one test class per production class

---

## NuGet Package Split

| Package | Contains |
|---|---|
| `NexJob` | Core interfaces + models + in-memory provider + DI extensions |
| `NexJob.Postgres` | PostgreSQL storage adapter (Npgsql) |
| `NexJob.SqlServer` | SQL Server adapter (Microsoft.Data.SqlClient) |
| `NexJob.Redis` | Redis adapter (StackExchange.Redis) |
| `NexJob.MongoDB` | MongoDB adapter (MongoDB.Driver) |
| `NexJob.Oracle` | Oracle adapter (Oracle.ManagedDataAccess.Core) |
| `NexJob.Dashboard` | Blazor SSR dashboard middleware |

---

## Current Roadmap

```
v0.1  ◆ Core interfaces · in-memory provider · fire-and-forget     ← WE ARE HERE
v0.2  ○ PostgreSQL provider · delayed jobs · recurring (cron)
v0.3  ○ Priority queues · resource throttling · continuations
v0.4  ○ Dashboard (Blazor SSR) · real-time log streaming
v0.5  ○ SQL Server · Redis · MongoDB · Oracle providers
v0.6  ○ OpenTelemetry · payload versioning · IJobMigration
v1.0  ○ Stable API · production-ready · published to NuGet
```

---

## What to build next (v0.1 completion)

In order of priority:

1. `InMemoryStorageProvider` — implements `IStorageProvider` using
   `ConcurrentDictionary` + `Channel<JobId>` for the queue.
   Must be thread-safe. Used for dev and unit tests.

2. `DefaultScheduler` — implements `IScheduler`, serializes input to JSON,
   creates `JobRecord`, calls `IStorageProvider.EnqueueAsync`.

3. `JobDispatcherService` — `BackgroundService`, runs worker pool,
   calls `FetchNextAsync` in a loop with backoff, resolves `IJob<TInput>`
   from DI scope, calls `ExecuteAsync`, handles retry on failure.

4. `RecurringJobSchedulerService` — `BackgroundService`, polls for due
   recurring jobs, enqueues them, updates next execution time.

5. `OrphanedJobWatcherService` — `BackgroundService`, detects and requeues
   orphaned jobs.

6. Unit tests for `InMemoryStorageProvider` and `DefaultScheduler`.

---

## Developer Contact

- GitHub: https://github.com/oluciano/NexJob
- Author: Luciano Azevedo
