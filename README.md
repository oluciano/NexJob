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
[![Coverage](https://img.shields.io/badge/coverage-87%25-brightgreen?style=flat-square)](https://github.com/oluciano/NexJob/actions)
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
- The license is LGPL for the core and paid for anything production-worthy.

NexJob was built to solve all of that. MIT license, end to end. Every storage adapter open-source. Native `async/await` from the ground up. Priority queues and idempotency built in. A dark-mode dashboard to inspect every queue, job state, and retry — without touching the code. OpenTelemetry, `appsettings.json` support, and live runtime config are on the roadmap.

---

## At a glance

> ✅ = implemented &nbsp;·&nbsp; 🔜 = on the roadmap

| | NexJob | Hangfire |
|---|:---:|:---:|
| License | MIT | LGPL / paid Pro |
| `async/await` native | ✅ | ❌ |
| Priority queues | ✅ | ❌ |
| Idempotency keys | ✅ | ❌ |
| In-memory for testing | ✅ | ✅ |
| Cron / recurring jobs | ✅ | ✅ |
| PostgreSQL + MongoDB adapters free | ✅ | ❌ |
| Dashboard (dark mode) | ✅ | ❌ |
| All storage adapters free | ✅ | ❌ |
| Resource throttling | ✅ | ❌ |
| Job continuations (chaining) | ✅ | ❌ |
| OpenTelemetry built-in | 🔜 | ❌ |
| Payload versioning | 🔜 | ❌ |
| `appsettings.json` support | 🔜 | ❌ |
| Execution windows per queue | 🔜 | ❌ |
| Live config without restart | 🔜 | ❌ |

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
builder.Services.AddNexJob(opt =>
{
    opt.Workers = 10;
    opt.PollingInterval = TimeSpan.FromSeconds(1);
});

// Register your jobs
builder.Services.AddTransient<SendInvoiceJob>();
```

### 3 — Schedule

```csharp
// Fire and forget
await scheduler.EnqueueAsync<SendInvoiceJob, SendInvoiceInput>(
    new(orderId, email));

// Delayed
await scheduler.ScheduleAsync<SendInvoiceJob, SendInvoiceInput>(
    new(orderId, email),
    delay: TimeSpan.FromMinutes(5));

// Recurring (cron) — default: skip if already running
await scheduler.RecurringAsync<MonthlyReportJob, MonthlyReportInput>(
    id: "monthly-report",
    input: new(DateTime.UtcNow.Month),
    cron: "0 9 1 * *");

// Recurring — allow multiple instances in parallel (range-based sharding etc.)
await scheduler.RecurringAsync<ImportChunkJob, ImportChunkInput>(
    id: "import-chunk",
    input: new(ShardId: 0),
    cron: "*/5 * * * *",
    concurrencyPolicy: RecurringConcurrencyPolicy.AllowConcurrent);

// Continuation — runs only after parent succeeds
var jobId = await scheduler.EnqueueAsync<ProcessPaymentJob, PaymentInput>(paymentInput);
await scheduler.ContinueWithAsync<SendReceiptJob, ReceiptInput>(jobId, receiptInput);

// With idempotency key — safe to call multiple times
await scheduler.EnqueueAsync<SendInvoiceJob, SendInvoiceInput>(
    new(orderId, email),
    idempotencyKey: $"invoice-{orderId}");
```

### 4 — Dashboard

```csharp
app.UseNexJobDashboard("/dashboard");
```

Open `/dashboard` and see every queue, every job state, every retry — live.

---

## Priority queues

Jobs with `Critical` priority jump the queue. No workarounds, no separate deployments.

```csharp
await scheduler.EnqueueAsync<AlertJob, AlertInput>(
    input,
    priority: JobPriority.Critical);   // Critical → High → Normal → Low
```

---

## Resource throttling

Limit how many instances of a job run concurrently across all workers — no extra infrastructure required.

```csharp
[Throttle(resource: "stripe", maxConcurrent: 3)]
public class ChargeCardJob : IJob<ChargeInput> { ... }
```

---

## Recurring concurrency policy

By default, NexJob prevents a recurring job from having more than one active instance at a time.
If the previous execution is still running when the cron fires again, the new firing is silently
skipped — no duplicates, no queued pile-up.

```csharp
// SkipIfRunning (default) — safe for jobs that must not overlap
await scheduler.RecurringAsync<SyncInventoryJob, Unit>(
    id:    "sync-inventory",
    input: Unit.Value,
    cron:  "*/5 * * * *");
    // concurrencyPolicy defaults to RecurringConcurrencyPolicy.SkipIfRunning
```

Some jobs are designed to run in parallel — for example, range-based imports that each
process a different shard of data. Use `AllowConcurrent` to opt out of the overlap guard:

```csharp
// AllowConcurrent — each firing spawns a new instance regardless of running ones
await scheduler.RecurringAsync<ImportShardJob, ShardInput>(
    id:                "import-shard",
    input:             new(ShardId: myShardId),
    cron:              "*/10 * * * *",
    concurrencyPolicy: RecurringConcurrencyPolicy.AllowConcurrent);
```

The dashboard shows a **⟳ concurrent** badge on any recurring job registered with
`AllowConcurrent`, so the behaviour is always visible at a glance.

---

## Observability

> 🔜 Coming in v0.6 — OpenTelemetry spans for every job lifecycle event.

---

## Payload versioning

> 🔜 Coming in v0.6 — migrate job inputs across schema versions without losing queued jobs.

---

## Testing

The in-memory provider requires zero setup:

```csharp
builder.Services.AddNexJob(opt => opt.Workers = 1);
// InMemoryStorageProvider is the default — no extra config needed
```

> 🔜 `TestScheduler` with `ShouldHaveEnqueued` assertions coming in v0.6.

---

## Retry policy

Failed jobs retry with exponential backoff and jitter. Dead-lettered jobs are preserved — never silently dropped.

| Attempt | Delay |
|:---:|---|
| 1 | ~16 seconds |
| 2 | ~1 minute |
| 3 | ~5 minutes |
| 4 | ~17 minutes |
| 5 | ~42 minutes |

Configure globally:

```csharp
builder.Services.AddNexJob(opt =>
{
    opt.MaxAttempts = 3;   // default: 10
});
```

> 🔜 Per-job `[Retry(attempts: 3)]` attribute coming in v0.6.

---

## Storage providers

All open-source. No license walls. Ever.

| Package | Storage | Status |
|---|---|---|
| `NexJob` | In-memory | ✅ Dev and testing |
| `NexJob.Postgres` | PostgreSQL 14+ | ✅ `SELECT FOR UPDATE SKIP LOCKED` |
| `NexJob.MongoDB` | MongoDB 6+ | ✅ Atomic `findAndModify` |
| `NexJob.SqlServer` | SQL Server 2019+ | 🔜 Coming soon |
| `NexJob.Redis` | Redis 7+ | 🔜 Coming soon |
| `NexJob.Oracle` | Oracle 19c+ | 🔜 Coming soon |

Bring your own? Implement `IStorageProvider` — one interface, ~15 methods.

---

## Configuration reference

```csharp
// Storage — pick one and register BEFORE AddNexJob (InMemory is the default)
builder.Services.AddNexJobPostgres(connectionString);
builder.Services.AddNexJobMongoDB(connectionString, databaseName: "nexjob");

builder.Services.AddNexJob(opt =>
{
    // Workers & queues
    opt.Workers = 10;
    opt.Queues  = ["default", "critical"];   // polled in order

    // Timing
    opt.PollingInterval   = TimeSpan.FromSeconds(5);
    opt.HeartbeatInterval = TimeSpan.FromSeconds(30);
    opt.HeartbeatTimeout  = TimeSpan.FromMinutes(5);

    // Retries
    opt.MaxAttempts = 5;
});
```

---

## Roadmap

```
v0.1  ✅ Core interfaces · in-memory provider · fire-and-forget
v0.2  ✅ PostgreSQL + MongoDB providers · delayed jobs · cron · dashboard (Blazor SSR)
v0.3  ✅ Priority queues · resource throttling ([Throttle]) · job continuations
v0.4  ✅ Recurring job execution status · unit + integration tests · CI pipeline
v0.5  ○ SQL Server · Redis · Oracle providers
v0.6  ○ OpenTelemetry · payload versioning (IJobMigration) · [Retry] per-job
v1.0  ○ Stable API · production-ready · published to NuGet
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
