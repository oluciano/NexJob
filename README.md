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

NexJob is the background job library that .NET deserved from day one.

No expression trees. No lock-in. No paid tiers for Redis. No wrestling with serialization. Just a clean interface, two lines of configuration, and a scheduler that handles the hard parts — retries, concurrency, cron, observability — while you focus on what your job actually does.

```csharp
public class WelcomeEmailJob : IJob<WelcomeEmailInput>
{
    public async Task ExecuteAsync(WelcomeEmailInput input, CancellationToken ct)
        => await _email.SendAsync(input.UserId, ct);
}
```

That's your entire job. NexJob handles the rest.

---

## Why NexJob exists

Every .NET developer has used Hangfire. And every .NET developer has hit the same walls:

- The Redis adapter costs money. So does the MongoDB one.
- `async/await` is bolted on — Hangfire serializes `Task`, it doesn't await it.
- The dashboard looks like it was designed in 2014. Because it was.
- There's no concept of priority queues, resource throttling, or payload versioning.
- You can't change workers or pause a queue without restarting the server.
- Schema migrations don't exist — add a column and deployments break.
- The license is LGPL for the core and paid for anything production-worthy.

NexJob was built to solve all of that.

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

> `Job progress tracking`, `IJobContext`, and `Job tags` are new in v0.3.

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

## Installation

```bash
# Core (includes in-memory provider for dev/tests)
dotnet add package NexJob

# Pick your storage — all free, all open-source
dotnet add package NexJob.Postgres
dotnet add package NexJob.SqlServer
dotnet add package NexJob.Redis
dotnet add package NexJob.MongoDB
dotnet add package NexJob.Oracle

# Optional dashboard
dotnet add package NexJob.Dashboard

# Or scaffold a complete starter project
dotnet new install NexJob.Templates
dotnet new nexjob -n MyApp
```

---

## Getting started

### 1 — Define your job

```csharp
public record SendInvoiceInput(Guid OrderId, string CustomerEmail);

public class SendInvoiceJob : IJob<SendInvoiceInput>
{
    private readonly IInvoiceService _invoices;
    private readonly IEmailService _email;

    public SendInvoiceJob(IInvoiceService invoices, IEmailService email)
    {
        _invoices = invoices;
        _email = email;
    }

    public async Task ExecuteAsync(SendInvoiceInput input, CancellationToken ct)
    {
        var pdf = await _invoices.GenerateAsync(input.OrderId, ct);
        await _email.SendAsync(input.CustomerEmail, pdf, ct);
    }
}
```

### 2 — Register

```csharp
// From appsettings.json (recommended)
builder.Services.AddNexJob(builder.Configuration)
                .AddNexJobJobs(typeof(Program).Assembly);

// Or fluent
builder.Services.AddNexJob(opt =>
{
    opt.UsePostgres(connectionString);
    opt.Workers = 10;
});
```

### 3 — Schedule

```csharp
// Fire and forget
await scheduler.EnqueueAsync<SendInvoiceJob, SendInvoiceInput>(new(orderId, email));

// Delayed
await scheduler.ScheduleAsync<SendInvoiceJob, SendInvoiceInput>(
    new(orderId, email), delay: TimeSpan.FromMinutes(5));

// Recurring (cron)
await scheduler.RecurringAsync<MonthlyReportJob, MonthlyReportInput>(
    id: "monthly-report",
    input: new(DateTime.UtcNow.Month),
    cron: "0 9 1 * *");

// Continuation — runs only after parent succeeds
var jobId = await scheduler.EnqueueAsync<ProcessPaymentJob, PaymentInput>(paymentInput);
await scheduler.ContinueWithAsync<SendReceiptJob, ReceiptInput>(jobId, receiptInput);

// With idempotency key — safe to call multiple times
await scheduler.EnqueueAsync<SendInvoiceJob, SendInvoiceInput>(
    new(orderId, email),
    idempotencyKey: $"invoice-{orderId}");

// With tags — searchable metadata
await scheduler.EnqueueAsync<SendInvoiceJob, SendInvoiceInput>(
    new(orderId, email),
    tags: ["tenant:acme", $"invoice:{invoiceId}"]);
```

### 4 — Dashboard

```csharp
app.UseNexJobDashboard("/jobs");
```

Open `/jobs` to see every queue, every job state, every retry — live.

---

## Configuration via appsettings.json

Every NexJob setting can live in `appsettings.json`. No code changes to tune behavior across environments.

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
      "Path": "/jobs",
      "Title": "MyApp Jobs",
      "RequireAuth": false,
      "PollIntervalSeconds": 3
    }
  }
}
```

Use different files per environment — no code changes needed:

```json
// appsettings.Development.json
{ "NexJob": { "Workers": 2, "PollingInterval": "00:00:01" } }
```

---

## Execution windows

Restrict queues to specific time windows — without touching a single cron expression.

```json
{
  "NexJob": {
    "Queues": [
      {
        "Name": "reports",
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
Windows that cross midnight work naturally: `22:00 → 06:00` runs after 10 PM **or** before 6 AM.

---

## Live configuration via dashboard

Change NexJob behavior at runtime — no restarts, no redeployments.

Open `/jobs/settings` in the dashboard to:

- **Adjust workers** — drag a slider, applied instantly across all instances
- **Pause a queue** — toggle any queue on/off without touching code
- **Pause all recurring jobs** — one click to freeze all cron-based jobs
- **Change polling interval** — tune live
- **View effective config** — see the merged result of appsettings + runtime overrides
- **Reset to appsettings** — clear all runtime overrides in one click

---

## Priority queues

Jobs with `Critical` priority jump the queue. No workarounds, no separate deployments.

```csharp
await scheduler.EnqueueAsync<AlertJob, AlertInput>(
    input, priority: JobPriority.Critical);  // Critical → High → Normal → Low
```

---

## Resource throttling

Don't overwhelm external APIs. Declare a limit once, NexJob enforces it across all workers.

```csharp
[Throttle(resource: "stripe", maxConcurrent: 3)]
public class ChargeCardJob : IJob<ChargeInput> { ... }

[Throttle(resource: "sendgrid", maxConcurrent: 5)]
public class BulkEmailJob : IJob<EmailInput> { ... }
```

All jobs sharing the same `resource` name share the same concurrency slot — globally, across all server instances.

---

## Per-job retry configuration

Override the global retry policy per job type.

```csharp
// 5 retries, doubling delay from 30s up to 1h
[Retry(5, InitialDelay = "00:00:30", Multiplier = 2.0, MaxDelay = "01:00:00")]
public class PaymentJob : IJob<PaymentInput> { ... }

// Dead-letter immediately on first failure — no retries
[Retry(0)]
public class WebhookJob : IJob<WebhookInput> { ... }
```

Global default (when no `[Retry]` is applied):

| Attempt | Delay |
|:---:|---|
| 1 | ~16 seconds |
| 2 | ~1 minute |
| 3 | ~5 minutes |
| 4 | ~17 minutes |
| 5 | ~42 minutes |

---

## Job context

Inject `IJobContext` via DI to access runtime information inside any job — no changes to the `IJob<TInput>` interface required.

```csharp
public class ImportCsvJob : IJob<ImportCsvInput>
{
    private readonly IJobContext _ctx;
    private readonly ILogger<ImportCsvJob> _logger;

    public ImportCsvJob(IJobContext ctx, ILogger<ImportCsvJob> logger)
    {
        _ctx = ctx;
        _logger = logger;
    }

    public async Task ExecuteAsync(ImportCsvInput input, CancellationToken ct)
    {
        _logger.LogInformation(
            "Job {JobId} — attempt {Attempt}/{Max} — queue {Queue}",
            _ctx.JobId, _ctx.Attempt, _ctx.MaxAttempts, _ctx.Queue);

        await _ctx.ReportProgressAsync(0, "Starting import...", ct);

        var rows = await LoadRowsAsync(input.FilePath, ct);

        await foreach (var row in rows.WithProgress(_ctx, ct))
        {
            await ProcessRowAsync(row, ct);
        }

        await _ctx.ReportProgressAsync(100, "Done.", ct);
    }
}
```

`IJobContext` exposes:

- `JobId` — the current job's identifier
- `Attempt` — current attempt number (1-based)
- `MaxAttempts` — total attempts allowed
- `Queue` — queue the job was fetched from
- `RecurringJobId` — set when the job was fired by a recurring definition
- `Tags` — tags attached at enqueue time
- `ReportProgressAsync(int percent, string? message, CancellationToken)` — updates the dashboard live

---

## Job progress tracking

`WithProgress` automatically reports progress as you iterate — no manual calls needed.

```csharp
// Works with IAsyncEnumerable
await foreach (var record in dbReader.ReadAllAsync(ct).WithProgress(_ctx, ct))
{
    await ImportAsync(record, ct);
}

// Works with IEnumerable
foreach (var item in items.WithProgress(_ctx))
{
    await ProcessAsync(item, ct);
}
```

The dashboard shows a live progress bar for any job that reports progress, updated in real time via SSE — no polling, no page refresh.

---

## Job tags

Tag jobs at enqueue time to add searchable metadata:

```csharp
await scheduler.EnqueueAsync<SendInvoiceJob, SendInvoiceInput>(
    input,
    tags: ["tenant:acme", $"order:{orderId}", "region:us-east"]);
```

Filter by tag in the dashboard or query programmatically:

```csharp
var jobs = await scheduler.GetJobsByTagAsync("tenant:acme");
```

---

## Schema migrations

NexJob automatically migrates its storage schema on startup. No manual SQL scripts, no deployment steps. Each migration runs in a transaction protected by a distributed advisory lock — safe when multiple instances start simultaneously.

```csharp
// Nothing to call — migrations run automatically when your app starts
builder.Services.AddNexJob(builder.Configuration);
```

---

## Graceful shutdown

When your host receives SIGTERM (Kubernetes rolling deployments, scale-down), NexJob waits for active jobs to complete before stopping.

```json
{
  "NexJob": {
    "ShutdownTimeoutSeconds": 30
  }
}
```

Jobs still running after the timeout are requeued automatically by the orphan watcher.

---

## Observability

NexJob emits OpenTelemetry spans and metrics for every job — enqueue, execute, retry, fail. Zero extra configuration if you already have OTEL wired up.

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource("NexJob"))
    .WithMetrics(m => m.AddMeter("NexJob"));
```

Every execution span carries:

```
nexjob.job_id       = "3f2a8c1d-..."
nexjob.job_type     = "SendInvoiceJob"
nexjob.queue        = "default"
nexjob.attempt      = 1
nexjob.status       = "succeeded"
nexjob.duration_ms  = 142
```

Metrics: `nexjob.jobs.enqueued`, `nexjob.jobs.succeeded`, `nexjob.jobs.failed`, `nexjob.job.duration`.

---

## Payload versioning

Your job inputs will change. NexJob handles it gracefully — no data loss, no broken jobs stuck in the queue.

```csharp
[SchemaVersion(2)]
public class SendInvoiceJob : IJob<SendInvoiceInputV2> { ... }

// Migration — discovered and applied automatically before execution
public class SendInvoiceMigration : IJobMigration<SendInvoiceInputV1, SendInvoiceInputV2>
{
    public SendInvoiceInputV2 Migrate(SendInvoiceInputV1 old)
        => new(old.OrderId, old.Email, Language: "en-US");
}
```

Register the migration at startup:

```csharp
builder.Services.AddJobMigration<SendInvoiceInputV1, SendInvoiceInputV2, SendInvoiceMigration>();
```

---

## Health checks

```csharp
builder.Services.AddHealthChecks()
                .AddNexJob()                        // Healthy / Degraded / Unhealthy
                .AddNexJob(failureThreshold: 100);  // Degraded if > 100 dead-letter jobs
```

---

## Testing

The in-memory provider requires zero setup.

```csharp
services.AddNexJob(opt => opt.UseInMemory());
```

---

## Storage providers

All open-source. No license walls. Ever.

| Package | Storage | Notes |
|---|---|---|
| `NexJob` | In-memory | Dev and testing only |
| `NexJob.Postgres` | PostgreSQL 14+ | `SELECT FOR UPDATE SKIP LOCKED` + auto-migrations |
| `NexJob.SqlServer` | SQL Server 2019+ | `UPDLOCK READPAST` + auto-migrations |
| `NexJob.Redis` | Redis 7+ | Lua scripts for atomicity |
| `NexJob.MongoDB` | MongoDB 6+ | Atomic `findAndModify` |
| `NexJob.Oracle` | Oracle 19c+ | `SKIP LOCKED` |

Bring your own? Implement `IStorageProvider` — one interface, full XML docs on every method.

---

## Configuration reference

```csharp
builder.Services.AddNexJob(builder.Configuration, opt =>
{
    // Storage — pick one
    opt.UsePostgres(connectionString);
    opt.UseSqlServer(connectionString);
    opt.UseRedis(connectionString);
    opt.UseMongoDB(connectionString, databaseName: "nexjob");
    opt.UseOracle(connectionString);
    opt.UseInMemory();                                    // dev/tests

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

## Roadmap

```
v0.1.0-alpha  ◆ Core · all storage providers · dashboard · 55 tests
v0.2.0        ◆ Schema migrations · graceful shutdown · [Retry] · distributed lock · 130 tests
v0.3.0        ◆ IJobContext · progress tracking · job tags
v1.0.0        ○ Stable API · production-ready
```

---

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
