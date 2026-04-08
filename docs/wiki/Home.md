# NexJob

Reliable background job processing for .NET 8.

**NexJob** is a production-oriented job processing library that gives you predictable execution semantics, built-in retry policies, deadline enforcement, and free storage providers — without the complexity of traditional schedulers.

---

## Quick Start

```csharp
// 1. Install package
// dotnet add package NexJob

// 2. Register NexJob and your jobs
builder.Services.AddNexJob();
builder.Services.AddNexJobJobs(typeof(Program).Assembly);

// 3. Define a job
public sealed class HelloJob : IJob
{
    public async Task ExecuteAsync(CancellationToken ct)
    {
        Console.WriteLine("Hello from NexJob!");
        await Task.CompletedTask;
    }
}

// 4. Enqueue and run
var scheduler = app.Services.GetRequiredService<IScheduler>();
await scheduler.EnqueueAsync<HelloJob>(cancellationToken: ct);
```

See [Getting Started](01-Getting-Started.md) for a complete walkthrough.

---

## When Should I Use NexJob?

### NexJob is a good fit when you need:

- **Predictable retries** — exponential backoff with configurable policies
- **Deadline enforcement** — jobs that expire if not executed in time
- **Dead-letter handling** — automatic fallback when retries are exhausted
- **Free storage providers** — PostgreSQL, SQL Server, Redis, MongoDB, InMemory
- **Low-latency dispatch** — wake-up channel for near-zero latency local enqueue
- **Built-in dashboard** — dark UI, zero configuration required
- **OpenTelemetry** — traces and metrics out of the box

### NexJob vs Alternatives

| Feature | NexJob | Hangfire | Quartz.NET |
|---|---|---|---|
| Free storage providers | 5 | Paid (except InMemory) | DIY |
| Deadline enforcement | Built-in (`deadlineAfter`) | Plugin required | Manual |
| Dead-letter handlers | Automatic | Manual | Manual |
| Wake-up latency | Near-zero (bounded channel) | Polling only | Polling only |
| Dashboard | Dark UI, standalone | Paid | None |
| Concurrency throttling | `[Throttle]` attribute | Queue limits | Trigger listeners |
| OpenTelemetry | Built-in | Plugin required | Plugin required |
| Package size | ~50 KB | ~2 MB | ~3 MB |

### When NOT to use NexJob:

- You need distributed job execution across untrusted networks (use a message broker)
- You require sub-second scheduling precision (NexJob polls at configurable intervals)
- You need a full-featured enterprise scheduler with calendar-based triggers

---

## Core Features

- **`IJob` / `IJob<T>`** — simple and structured job interfaces
- **Retry policies** — global + per-job `[Retry]` attribute with exponential backoff
- **`[Throttle]`** — resource-based concurrency limits
- **`deadlineAfter`** — jobs expire if not executed in time
- **`IDeadLetterHandler<T>`** — automatic fallback on permanent failure
- **Recurring jobs** — via code or `appsettings.json`
- **Continuations** — chain jobs with parent/child relationships
- **Job Filters** — `IJobExecutionFilter` middleware pipeline for cross-cutting behaviour
- **Idempotency** — `DuplicatePolicy` controls re-enqueue behavior
- **Dashboard** — dark UI, standalone for Worker Services
- **OpenTelemetry** — traces and metrics built-in

---

## Documentation

| Page | Purpose |
|---|---|
| [Mental Model](00-Mental-Model.md) ⭐ | How NexJob works — read this first |
| [Getting Started](01-Getting-Started.md) | Run your first job in 2 minutes |
| [Job Types](02-Job-Types.md) | `IJob` vs `IJob<T>`, dead-letter handlers |
| [Scheduling](03-Scheduling.md) | Enqueue, schedule, priority, deadline |
| [Recurring Jobs](04-Recurring-Jobs.md) | Cron-based recurring execution |
| [Continuations](05-Continuations.md) | Chain jobs together |
| [Job Filters](02-Job-Types.md#job-execution-filters) | Cross-cutting middleware for job execution |
| [Retry & Dead Letter](06-Retry-And-Dead-Letter.md) | Retry policies and fallback handlers |
| [Throttling](07-Throttling.md) | Concurrency limits per resource |
| [IJobContext](08-IJobContext.md) | Access runtime context inside jobs |
| [Storage Providers](09-Storage-Providers.md) | PostgreSQL, SQL Server, Redis, MongoDB |
| [Dashboard](10-Dashboard.md) | Monitor and debug jobs |
| [Configuration Reference](11-Configuration-Reference.md) | All options and settings |
| [OpenTelemetry](12-OpenTelemetry.md) | Traces and metrics |
| [Best Practices](13-Best-Practices.md) | Production guidelines |
| [Writing Tests](14-Writing-Tests.md) | Testing patterns |
| [Common Scenarios](15-Common-Scenarios.md) ⭐ | Real-world use cases with code |
| [Troubleshooting](16-Troubleshooting.md) ⭐ | Debug common issues |
| [Idempotency](17-Idempotency.md) ⭐ | Prevent duplicate execution |
| [Migration](18-Migration.md) | Breaking changes and upgrade guide |

---

## Version

**Current:** v0.8.0 | [Changelog](../../CHANGELOG.md) | [GitHub](https://github.com/oluciano/NexJob)
