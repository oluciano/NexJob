# NexJob

Predictable and observable background job processing for .NET 8.

**NexJob** gives you predictable execution, built-in retries, deadline enforcement, and operational visibility — without the complexity of traditional schedulers. If a job must run, fail safely, and leave a trace, NexJob handles it as a first-class concern.

---

## Quick Start

```bash
dotnet add package NexJob
```

```csharp
// Program.cs — register NexJob and scan for jobs
builder.Services.AddNexJob();
builder.Services.AddNexJobJobs(typeof(Program).Assembly);
```

```csharp
// Define a job
public sealed class HelloJob : IJob
{
    public Task ExecuteAsync(CancellationToken ct)
    {
        Console.WriteLine("Hello from NexJob!");
        return Task.CompletedTask;
    }
}
```

```csharp
// Enqueue from anywhere
var scheduler = app.Services.GetRequiredService<IScheduler>();
await scheduler.EnqueueAsync<HelloJob>();
```

See [Getting Started](01-Getting-Started.md) for a complete walkthrough.

---

## When Should I Use NexJob?

### NexJob is a good fit when you need:

- **Predictable execution** — retries, deadlines, and failure handling built into the model
- **Deadline enforcement** — jobs expire if not executed in time (`deadlineAfter`)
- **Dead-letter handling** — automatic fallback when all retries are exhausted
- **Operational visibility** — built-in dashboard, traces, and metrics
- **Low-latency dispatch** — wake-up channel for near-zero latency local enqueue
- **Free storage providers** — PostgreSQL, SQL Server, Redis, MongoDB, InMemory
- **Event-driven triggers** — enqueue jobs from Azure Service Bus, SQS, RabbitMQ, Kafka, or Pub/Sub

### NexJob vs Alternatives

| Feature | NexJob | Hangfire | Quartz.NET |
|---|---|---|---|
| Execution model | Stateless dispatcher, atomic commits | Polling-based | Trigger-based |
| Retry & dead-letter | Built-in, configurable | Plugin required | Manual |
| Deadline enforcement | Built-in (`deadlineAfter`) | Plugin required | Manual |
| Scheduling | Interval-based polling | Polling | Cron + calendar |
| Dashboard | Built-in, standalone | Paid | None |
| OpenTelemetry | Built-in | Plugin required | Plugin required |

### When NOT to use NexJob:

- You need distributed job execution across untrusted networks
- You require sub-second scheduling precision (NexJob polls at configurable intervals)
- You need a full-featured enterprise scheduler with calendar-based triggers

---

## Core Features

### Job Model
- **`IJob` / `IJob<T>`** — simple and structured execution
- **Recurring jobs** — via code or `appsettings.json`
- **Continuations** — chain jobs with parent/child relationships
- **External Triggers** — enqueue jobs from message brokers automatically

### Reliability
- **Retry policies** — global + per-job `[Retry]` with exponential backoff
- **`deadlineAfter`** — jobs expire if not executed in time
- **`IDeadLetterHandler<T>`** — automatic fallback on permanent failure
- **Idempotency** — `DuplicatePolicy` controls re-enqueue behavior

### Operations
- **OpenTelemetry** — traces and metrics built-in
- **Dashboard** — standalone UI for monitoring
- **`[Throttle]`** — concurrency limits per resource

### Extensibility
- **Job Filters** — `IJobExecutionFilter` middleware pipeline for cross-cutting behaviour

---

## Documentation

### Start here

| Page | Purpose |
|---|---|
| [Mental Model](00-Mental-Model.md) ⭐ | How NexJob works — read this first |
| [Getting Started](01-Getting-Started.md) | Run your first job in 2 minutes |
| [Job Types](02-Job-Types.md) | `IJob` vs `IJob<T>`, dead-letter handlers |

### Build & schedule

| Page | Purpose |
|---|---|
| [Scheduling](03-Scheduling.md) | Enqueue, schedule, priority, deadline |
| [Recurring Jobs](04-Recurring-Jobs.md) | Cron-based recurring execution |
| [Continuations](05-Continuations.md) | Chain jobs together |
| [Job Filters](02-Job-Types.md#job-execution-filters) | Cross-cutting middleware for job execution |
| [External Triggers](19-Triggers.md) | Enqueue jobs from message brokers |

### Operate in production

| Page | Purpose |
|---|---|
| [Retry & Dead Letter](06-Retry-And-Dead-Letter.md) | Retry policies and fallback handlers |
| [Throttling](07-Throttling.md) | Concurrency limits per resource |
| [IJobContext](08-IJobContext.md) | Access runtime context inside jobs |
| [Storage Providers](09-Storage-Providers.md) | PostgreSQL, SQL Server, Redis, MongoDB |
| [Dashboard](10-Dashboard.md) | Monitor and debug jobs |
| [Configuration Reference](11-Configuration-Reference.md) | All options and settings |
| [OpenTelemetry](12-OpenTelemetry.md) | Traces and metrics |
| [Best Practices](13-Best-Practices.md) | Production guidelines |
| [Writing Tests](14-Writing-Tests.md) | Testing patterns |

### Reference

| Page | Purpose |
|---|---|
| [Common Scenarios](15-Common-Scenarios.md) ⭐ | Real-world use cases with code |
| [Troubleshooting](16-Troubleshooting.md) ⭐ | Debug common issues |
| [Idempotency](17-Idempotency.md) ⭐ | Prevent duplicate execution |
| [Migration](18-Migration.md) | Breaking changes and upgrade guide |

---

## Next Steps

- **I want to understand how NexJob works** → [Mental Model](00-Mental-Model.md)
- **I want to run a job now** → [Getting Started](01-Getting-Started.md)

---

**v2.0.0** · [Changelog](../../CHANGELOG.md) · [GitHub](https://github.com/oluciano/NexJob)
