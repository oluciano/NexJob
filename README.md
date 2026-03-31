<div align="center">

<br/>

```
███╗   ██╗███████╗██╗  ██╗     ██╗ ██████╗ ██████╗
████╗  ██║██╔════╝╚██╗██╔╝     ██║██╔═══██╗██╔══██╗
██╔██╗ ██║█████╗   ╚███╔╝      ██║██║   ██║██████╔╝
██║╚██╗██║██╔══╝   ██╔██╗ ██   ██║██║   ██║██╔══██╗
██║ ╚████║███████╗██╔╝ ██╗╚█████╔╝╚██████╔╝██████╔╝
╚═╝  ╚═══╝╚══════╝╚═╝  ╚═╝ ╚════╝  ╚═════╝ ╚═════╝
```

**Background jobs for .NET that stay out of your way.**

[![NuGet](https://img.shields.io/nuget/v/NexJob.svg?style=flat-square&color=512bd4&label=nuget)](https://www.nuget.org/packages/NexJob)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NexJob?style=flat-square&color=512bd4)](https://www.nuget.org/packages/NexJob)
[![Build](https://img.shields.io/github/actions/workflow/status/oluciano/NexJob/ci.yml?style=flat-square)](https://github.com/oluciano/NexJob/actions)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg?style=flat-square)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%2B-512bd4?style=flat-square)](https://dotnet.microsoft.com)

<br/>

</div>

---

## The Why

Every .NET developer has used Hangfire. Every .NET developer has hit the same walls:

- The Redis adapter costs money. So does the MongoDB one.
- `async/await` is bolted on — Hangfire serializes `Task`, it doesn't await it.
- The dashboard looks like it was designed in 2014. Because it was.
- There's no concept of priority queues, resource throttling, or payload versioning.
- You can't change workers or pause a queue without restarting the server.
- Schema migrations don't exist — add a column and deployments break.
- The license is LGPL for the core and paid for anything production-worthy.

NexJob was built to solve all of that.

```csharp
public class WelcomeEmailJob : IJob<WelcomeEmailInput>
{
    public async Task ExecuteAsync(WelcomeEmailInput input, CancellationToken ct)
        => await _email.SendAsync(input.UserId, ct);
}
```

Install the package, implement one interface, add two lines of config — done.

---

## At a glance

| | NexJob | Hangfire |
|---|:---:|:---:|
| License | MIT | LGPL / paid Pro |
| `async/await` native | ✅ | ❌ |
| Priority queues | ✅ | ❌ |
| Resource throttling (`[Throttle]`) | ✅ | ❌ |
| Per-job retry config (`[Retry]`) | ✅ | ❌ |
| Idempotency keys | ✅ | ❌ |
| `IJob` (no-input interface) | ✅ | ❌ |
| Job continuations (chaining) | ✅ | ❌ |
| `appsettings.json` support | ✅ | ❌ |
| Execution windows per queue | ✅ | ❌ |
| Live config without restart | ✅ | ❌ |
| Schema migrations (auto) | ✅ | ❌ |
| Graceful shutdown | ✅ | ❌ |
| Distributed recurring lock | ✅ | ❌ |
| OpenTelemetry built-in | ✅ | ❌ |
| Payload versioning (`IJobMigration`) | ✅ | ❌ |
| Job progress tracking | ✅ | ✅ paid |
| Job context (`IJobContext`) | ✅ | ✅ |
| Job tags | ✅ | ❌ |
| All storage adapters free | ✅ | ❌ |
| In-memory for testing | ✅ | ✅ |
| Cron / recurring jobs | ✅ | ✅ |
| Dashboard | ✅ dark mode | ✅ legacy |
| Worker Service / Console dashboard | ✅ | ❌ |

---

## Benchmarks

> Measured on Intel Xeon E5-2667 v4 3.20GHz, 16 logical cores, .NET 8.0.25, March 2026.
> Run `dotnet run -c Release --project benchmarks/NexJob.Benchmarks -- --filter '*' --job Short` to reproduce.
> Full raw results in [benchmarks/results/README.md](benchmarks/results/README.md).

### Enqueue latency — single job, in-memory storage

| | NexJob | Hangfire | Difference |
|---|---|---|---|
| Mean latency | 9.28 μs | 26.63 μs | **NexJob is 2.87x faster** |
| Allocated memory | 1.67 KB | 11.2 KB | **NexJob uses 85% less memory** |

---

# Quick Start

## Installation

```bash
# Core (includes in-memory provider for dev/tests)
dotnet add package NexJob

# Pick your storage — all free, all open-source
dotnet add package NexJob.Postgres
dotnet add package NexJob.SqlServer
dotnet add package NexJob.Redis
dotnet add package NexJob.MongoDB

# For Web APIs
dotnet add package NexJob.Dashboard

# For Worker Services and Console Apps
dotnet add package NexJob.Dashboard.Standalone
```

## 5-minute setup

### 1 — Define your job

```csharp
public record SendInvoiceInput(Guid OrderId, string Email);

public class SendInvoiceJob : IJob<SendInvoiceInput>
{
    private readonly IEmailService _email;
    public SendInvoiceJob(IEmailService email) => _email = email;

    public async Task ExecuteAsync(SendInvoiceInput input, CancellationToken ct)
        => await _email.SendAsync(input.Email, "Invoice attached", ct);
}
```

Or without input:

```csharp
public class NightlyCleanupJob : IJob
{
    private readonly ISessionRepository _sessions;
    public NightlyCleanupJob(ISessionRepository sessions) => _sessions = sessions;

    public async Task ExecuteAsync(CancellationToken ct)
        => await _sessions.DeleteExpiredAsync(ct);
}
```

### 2 — Register services

```csharp
builder.Services.AddNexJob(builder.Configuration)
                .AddNexJobJobs(typeof(Program).Assembly);
```

### 3 — Enqueue

```csharp
// Fire and forget
await scheduler.EnqueueAsync<SendInvoiceJob, SendInvoiceInput>(
    new(orderId, email));

// Delayed
await scheduler.ScheduleAsync<SendInvoiceJob, SendInvoiceInput>(
    new(orderId, email), delay: TimeSpan.FromMinutes(5));

// Recurring (cron) — for IJob<T> jobs
await scheduler.RecurringAsync<ReportJob, ReportInput>(
    id: "monthly-report",
    input: new(DateTime.UtcNow.Month),
    cron: "0 2 1 * *");
```

### 4 — Add dashboard

**Web API / ASP.NET Core:**
```csharp
app.UseNexJobDashboard("/dashboard");
```

**Worker Service / Console App:**
```csharp
services.AddNexJobStandaloneDashboard(configuration);
// Available at http://localhost:5005/dashboard
```

### 5 — Configure (optional)

```json
{
  "NexJob": {
    "Workers": 10,
    "DefaultQueue": "default",
    "PollingInterval": "00:00:05"
  }
}
```

For complete examples, see [/samples](samples/).

---

# Features

## Scheduling in depth

### Fire and forget

Enqueue a job to run ASAP — no scheduling involved.

```csharp
await scheduler.EnqueueAsync<SendInvoiceJob, SendInvoiceInput>(
    new(orderId, email));
```

### Delayed jobs

Schedule a job to run after a delay.

```csharp
await scheduler.ScheduleAsync<SendInvoiceJob, SendInvoiceInput>(
    new(orderId, email), 
    delay: TimeSpan.FromMinutes(5));
```

Or at a specific time:

```csharp
await scheduler.ScheduleAtAsync<SendInvoiceJob, SendInvoiceInput>(
    new(orderId, email),
    runAt: DateTimeOffset.UtcNow.AddHours(2));
```

### Recurring jobs (cron)

Run a job on a schedule — NexJob handles the cron parsing and distributed lock.

```csharp
await scheduler.RecurringAsync<MonthlyReportJob, ReportInput>(
    id: "monthly-report",
    input: new(DateTime.UtcNow.Month),
    cron: "0 9 1 * *",  // 9 AM on the 1st of each month
    timeZone: TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
```

Remove it anytime:

```csharp
await scheduler.RemoveRecurringAsync("monthly-report");
```

### Continuations

Chain jobs — the next job only runs if the parent succeeds.

```csharp
var paymentId = await scheduler.EnqueueAsync<ProcessPaymentJob, PaymentInput>(input);
await scheduler.ContinueWithAsync<SendReceiptJob, ReceiptInput>(paymentId, receiptInput);
```

### Idempotency keys

Safe to call multiple times — NexJob deduplicates based on the key.

```csharp
await scheduler.EnqueueAsync<SendInvoiceJob, SendInvoiceInput>(
    new(orderId, email),
    idempotencyKey: $"invoice-{orderId}");
```

### Job tags

Tag jobs with searchable metadata — visible in dashboard and queryable.

```csharp
await scheduler.EnqueueAsync<SendInvoiceJob, SendInvoiceInput>(
    new(orderId, email),
    tags: ["tenant:acme", $"invoice:{invoiceId}", "region:us-east"]);

// Query by tag
var jobs = await scheduler.GetJobsByTagAsync("tenant:acme");
```

### Priority queues

Critical jobs jump the queue.

```csharp
await scheduler.EnqueueAsync<AlertJob, AlertInput>(
    input, 
    priority: JobPriority.Critical);  // Critical → High → Normal → Low
```

### Deadlines (WithDeadlineAfter)

If a job is not executed within the specified window, it is marked as `Expired` and skipped. Useful for time-sensitive operations.

```csharp
await scheduler.EnqueueAsync<PaymentJob, PaymentInput>(
    input,
    deadlineAfter: TimeSpan.FromMinutes(5));
```

---

## Reliability

### Retry policy (global)

By default, jobs retry with exponential backoff. After `MaxAttempts`, they move to dead-letter.

| Attempt | Delay |
|:---:|---|
| 1 | ~16 seconds |
| 2 | ~1 minute |
| 3 | ~5 minutes |
| 4 | ~17 minutes |
| 5 | ~42 minutes |

Override in `appsettings.json`:

```json
{ "NexJob": { "MaxAttempts": 7 } }
```

### Per-job retry configuration

Override the global policy per job type.

```csharp
[Retry(0)]  // Dead-letter immediately
public class WebhookJob : IJob<WebhookInput> { ... }

[Retry(5, InitialDelay = "00:00:30", Multiplier = 2.0, MaxDelay = "01:00:00")]
public class PaymentJob : IJob<PaymentInput> { ... }
```

### Dead-letter handler

Automatically handle jobs that exhaust all retries.

```csharp
public class PaymentDeadLetterHandler : IDeadLetterHandler<PaymentJob>
{
    public async Task HandleAsync(JobRecord failedJob, Exception lastException,
        CancellationToken ct)
    {
        await _alerts.SendAsync($"Payment failed permanently", ct);
    }
}

// Register
builder.Services.AddTransient<IDeadLetterHandler<PaymentJob>, PaymentDeadLetterHandler>();
```

Handler errors are swallowed — they never crash the dispatcher.

### Graceful shutdown

When your host receives SIGTERM (Kubernetes rolling deployments, scale-down), NexJob waits for active jobs to complete before stopping.

```json
{
  "NexJob": {
    "ShutdownTimeoutSeconds": 30
  }
}
```

Jobs still running after the timeout are requeued by the orphan watcher.

### Orphan watcher

Detect and requeue jobs that crashed unexpectedly. Workers send heartbeats every 30 seconds; if a job doesn't update for 5 minutes, it's assumed dead and requeued.

---

## Concurrency & throttling

### Resource throttling

Declare a limit once — NexJob enforces it across all workers globally.

```csharp
[Throttle(resource: "stripe", maxConcurrent: 3)]
public class ChargeCardJob : IJob<ChargeInput> { ... }
```

All jobs sharing the same resource name share the same concurrency slot.

### Execution windows

Restrict queues to specific time windows.

```json
{
  "NexJob": {
    "Queues": [
      {
        "Name": "reports",
        "Workers": 2,
        "ExecutionWindow": {
          "StartTime": "22:00",
          "EndTime": "06:00",
          "TimeZone": "America/Sao_Paulo"
        }
      }
    ]
  }
}
```

The `reports` queue only processes jobs between 10 PM and 6 AM São Paulo time.

---

## Job context & progress

### IJobContext

Inject `IJobContext` to access runtime information inside any job.

```csharp
public class ImportCsvJob : IJob<ImportCsvInput>
{
    private readonly IJobContext _ctx;

    public ImportCsvJob(IJobContext ctx) => _ctx = ctx;

    public async Task ExecuteAsync(ImportCsvInput input, CancellationToken ct)
    {
        await _ctx.ReportProgressAsync(0, "Starting...", ct);
        // ... do work ...
        await _ctx.ReportProgressAsync(100, "Done.", ct);
    }
}
```

Exposes: `JobId`, `Attempt`, `MaxAttempts`, `Queue`, `RecurringJobId`, `Tags`.

### WithProgress

Automatically report progress as you iterate.

```csharp
// With IAsyncEnumerable
await foreach (var record in dbReader.ReadAllAsync(ct).WithProgress(_ctx, ct))
{
    await ImportAsync(record, ct);
}

// With IEnumerable
foreach (var item in items.WithProgress(_ctx))
{
    await ProcessAsync(item, ct);
}
```

The dashboard shows a live progress bar updated in real-time via SSE.

---

## Observability

NexJob emits OpenTelemetry spans and metrics for every job — zero extra configuration.

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource("NexJob"))
    .WithMetrics(m => m.AddMeter("NexJob"));
```

Metrics: `nexjob.jobs.enqueued`, `nexjob.jobs.succeeded`, `nexjob.jobs.failed`, `nexjob.jobs.expired`, `nexjob.job.duration`.

---

## Payload versioning

Your job inputs will change. NexJob handles it gracefully.

```csharp
[SchemaVersion(2)]
public class SendInvoiceJob : IJob<SendInvoiceInputV2> { ... }

public class SendInvoiceMigration : IJobMigration<SendInvoiceInputV1, SendInvoiceInputV2>
{
    public SendInvoiceInputV2 Migrate(SendInvoiceInputV1 old)
        => new(old.OrderId, old.Email, Language: "en-US");
}

// Register
builder.Services.AddJobMigration<SendInvoiceInputV1, SendInvoiceInputV2, SendInvoiceMigration>();
```

---

# Configuration

## appsettings.json reference

```json
{
  "NexJob": {
    "Workers": 10,
    "DefaultQueue": "default",
    "MaxAttempts": 5,
    "ShutdownTimeoutSeconds": 30,
    "PollingInterval": "00:00:05",
    "HeartbeatInterval": "00:00:30",
    "HeartbeatTimeout": "00:05:00",
    "Queues": [
      { "Name": "critical", "Workers": 3 },
      { "Name": "default",  "Workers": 5 },
      {
        "Name": "reports",
        "Workers": 2,
        "ExecutionWindow": {
          "StartTime": "22:00",
          "EndTime": "06:00",
          "TimeZone": "America/Sao_Paulo"
        }
      },
      { "Name": "low", "Workers": 1 }
    ],
    "Dashboard": {
      "Path": "/dashboard",
      "Title": "MyApp Jobs",
      "RequireAuth": false,
      "PollIntervalSeconds": 3
    }
  }
}
```

## Live configuration

Change NexJob behavior at runtime via the dashboard settings page — no restarts needed.

- **Adjust workers** — slider applied instantly
- **Pause a queue** — toggle on/off
- **Pause all recurring jobs** — one click
- **Change polling interval** — tune live
- **Reset to appsettings** — clear all runtime overrides

---

## Storage providers

All open-source. No license walls. Ever.

| Package | Storage | Status |
|---|---|---|
| `NexJob` | In-memory | Production ready |
| `NexJob.Postgres` | PostgreSQL 14+ | Production ready (`SELECT FOR UPDATE SKIP LOCKED` + auto-migrations) |
| `NexJob.SqlServer` | SQL Server 2019+ | Production ready (`UPDLOCK READPAST` + auto-migrations) |
| `NexJob.Redis` | Redis 7+ | Production ready (Lua scripts for atomicity) |
| `NexJob.MongoDB` | MongoDB 6+ | Production ready (atomic `findAndModify`) |
| `NexJob.Oracle` | Oracle 19c+ | Planned |

Bring your own? Implement `IStorageProvider` — fully documented.

---

## Configuration reference (code)

```csharp
builder.Services.AddNexJob(builder.Configuration, opt =>
{
    // Storage — pick one
    opt.UsePostgres(connectionString);
    opt.UseSqlServer(connectionString);
    opt.UseRedis(connectionString);
    opt.UseMongoDB(connectionString, databaseName: "nexjob");
    opt.UseOracle(connectionString);
    opt.UseInMemory();  // default

    // Workers & queues (override appsettings)
    opt.Workers = 10;
    opt.Queues = ["critical", "default", "low"];
    opt.DefaultQueue = "default";

    // Timing
    opt.PollingInterval   = TimeSpan.FromSeconds(5);
    opt.HeartbeatInterval = TimeSpan.FromSeconds(30);
    opt.HeartbeatTimeout  = TimeSpan.FromMinutes(5);
    opt.ShutdownTimeout   = TimeSpan.FromSeconds(30);

    // Retries
    opt.MaxAttempts = 5;
});
```

---

## Testing

The in-memory provider is the default:

```csharp
services.AddNexJob();  // InMemory is automatic

// Or explicit
services.AddNexJob(opt => opt.UseInMemory());
```

---

## Health checks

```csharp
builder.Services.AddHealthChecks()
                .AddNexJob()                        // Healthy / Degraded / Unhealthy
                .AddNexJob(failureThreshold: 100);  // Degraded if > 100 dead-letter jobs
```

---

## Schema migrations

NexJob automatically migrates its storage schema on startup. No manual SQL scripts, no deployment steps.

```csharp
// Nothing to call — migrations run automatically when your app starts
builder.Services.AddNexJob(builder.Configuration);
```

---

## Worker Services & Console Apps

NexJob works in any .NET host — not just Web APIs. For applications without HTTP, use the standalone dashboard:

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddNexJob(builder.Configuration)
    .AddNexJobJobs(typeof(Program).Assembly);

builder.Services.AddNexJobStandaloneDashboard(builder.Configuration);

await builder.Build().RunAsync();
// Dashboard: http://localhost:5005/dashboard
```

Configure in appsettings.json:

```json
{
  "NexJob": {
    "Dashboard": {
      "Port": 5005,
      "Path": "/dashboard",
      "Title": "My Worker Jobs",
      "LocalhostOnly": true
    }
  }
}
```

---

# Project

## Roadmap

```
v0.1.0-alpha  ◆ Core · all storage providers · dashboard · 55 tests
v0.2.0        ◆ Schema migrations · graceful shutdown · [Retry] · distributed lock · 130 tests
v0.3.0        ◆ IJobContext · progress tracking · job tags
v0.4.0        ◆ Wake-up channel · WithDeadlineAfter · IDeadLetterHandler · README reorganized
v1.0.0        ○ Stable API · production-ready
```

## Contributing

NexJob is built in the open. Issues, ideas, and PRs are welcome.

```bash
git clone git@github.com:oluciano/NexJob.git
cd NexJob
dotnet restore
dotnet test
```

Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a PR.

---

## License

MIT © 2025 [Luciano Azevedo](https://github.com/oluciano)

---

<div align="center">
<br/>

*Built with obsession over developer experience.*

**If Hangfire is the past, NexJob is what comes next.**

<br/>
</div>
