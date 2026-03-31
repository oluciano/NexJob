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

## What is NexJob?

**NexJob** is a modern, open-source background job framework for .NET built for predictable execution, time-sensitive workflows, and explicit failure handling.

Unlike Hangfire — which relies on serialization magic and delayed async — NexJob is **async/await native**, **fully type-safe**, and **stateless**. Jobs run in isolated DI scopes with zero hidden state. Storage is the source of truth. Failures are explicit.

---

## ⚡ 30-Second Quick Start

### 1. Define a job

```csharp
public class SendInvoiceJob : IJob<SendInvoiceInput>
{
    private readonly IEmailService _email;
    public SendInvoiceJob(IEmailService email) => _email = email;

    public async Task ExecuteAsync(SendInvoiceInput input, CancellationToken ct)
        => await _email.SendAsync(input.Email, "Invoice attached", ct);
}
```

### 2. Register

```csharp
builder.Services.AddNexJob(builder.Configuration)
                .AddNexJobJobs(typeof(Program).Assembly);
```

### 3. Enqueue

```csharp
await scheduler.EnqueueAsync<SendInvoiceJob, SendInvoiceInput>(
    new(orderId, email));
```

Done. The job executes immediately on an available worker.

---

## 🔥 Why NexJob?

| Feature | Benefit |
|---------|---------|
| **Predictable execution** | No serialization magic, no hidden state. Jobs run in isolated DI scopes. |
| **Deadline support** (`deadlineAfter`) | Mark jobs expired if not started in time — essential for time-sensitive operations. |
| **Automatic dead-letter handling** | Failed jobs trigger explicit handlers for alerts, compensation, or cleanup. |
| **Low-latency dispatch** | Wake-up signaling eliminates polling delay on local enqueue. 2.87× faster than Hangfire. |
| **All storage free** | PostgreSQL, SQL Server, Redis, MongoDB — no paid adapters or license walls. |
| **Async/await native** | True async from the ground up. No serialization hacks. |
| **Live config** | Pause queues, adjust workers, change polling — all at runtime without restart. |
| **Schema migrations** | Auto-migrations on startup. No manual SQL. No deployment breakage. |

---

## ⚔️ NexJob vs Hangfire

| | NexJob | Hangfire |
|---|:---:|:---:|
| **License** | MIT | LGPL / paid Pro |
| **Async/await native** | ✅ | ❌ serialization-based |
| **Deadline support** | ✅ `deadlineAfter` | ❌ |
| **Dead-letter handlers** | ✅ DI-based | ❌ |
| **Priority queues** | ✅ | ❌ |
| **Resource throttling** | ✅ `[Throttle]` | ❌ |
| **Per-job retry config** | ✅ `[Retry]` | ❌ |
| **Execution windows** | ✅ | ❌ |
| **Live config** | ✅ | ❌ |
| **Schema migrations** | ✅ auto | ❌ |
| **Graceful shutdown** | ✅ | ❌ |
| **OpenTelemetry** | ✅ built-in | ❌ |
| **Payload versioning** | ✅ `IJobMigration` | ❌ |
| **All adapters free** | ✅ | ❌ Redis/MongoDB paid |
| **Storage providers** | 5 (all open-source) | 3 |
| **Enqueue latency** | 9.28 μs | 26.63 μs |

---

## 📦 Installation

```bash
# Core
dotnet add package NexJob

# Storage (pick one — all free)
dotnet add package NexJob.Postgres
dotnet add package NexJob.SqlServer
dotnet add package NexJob.Redis
dotnet add package NexJob.MongoDB

# Dashboard
dotnet add package NexJob.Dashboard
```

---

# Features in Depth

## Scheduling

### Fire and forget
```csharp
await scheduler.EnqueueAsync<SendInvoiceJob, SendInvoiceInput>(new(orderId, email));
```

### Delayed
```csharp
await scheduler.ScheduleAsync<SendInvoiceJob, SendInvoiceInput>(
    new(orderId, email), 
    delay: TimeSpan.FromMinutes(5));
```

### Recurring (cron)
```csharp
await scheduler.RecurringAsync<ReportJob, ReportInput>(
    id: "monthly",
    input: new(DateTime.UtcNow.Month),
    cron: "0 9 1 * *");
```

### Continuations (chaining)
```csharp
var paymentId = await scheduler.EnqueueAsync<ProcessPaymentJob, PaymentInput>(input);
await scheduler.ContinueWithAsync<SendReceiptJob, ReceiptInput>(paymentId, receiptInput);
```

---

## Reliability

### Global retry policy
By default, jobs retry with exponential backoff. Configure via `appsettings.json`:
```json
{ "NexJob": { "MaxAttempts": 5 } }
```

### Per-job retry
```csharp
[Retry(5, InitialDelay = "00:00:30", Multiplier = 2.0, MaxDelay = "01:00:00")]
public class PaymentJob : IJob<PaymentInput> { ... }
```

### Dead-letter handler
Automatically handle jobs that exhaust retries:
```csharp
public class PaymentDeadLetterHandler : IDeadLetterHandler<PaymentJob>
{
    public async Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken ct)
        => await _alerts.SendAsync($"Payment failed", ct);
}

builder.Services.AddTransient<IDeadLetterHandler<PaymentJob>, PaymentDeadLetterHandler>();
```

### Graceful shutdown
Jobs complete naturally on SIGTERM. Strays are requeued by orphan watcher.

---

## Deadlines

Jobs not executed within the deadline are marked `Expired` and skipped:

```csharp
await scheduler.EnqueueAsync<PaymentJob, PaymentInput>(
    input,
    deadlineAfter: TimeSpan.FromMinutes(5));
```

---

## Observability

### OpenTelemetry (zero config)
```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource("NexJob"))
    .WithMetrics(m => m.AddMeter("NexJob"));
```

Metrics: `nexjob.jobs.enqueued`, `nexjob.jobs.succeeded`, `nexjob.jobs.failed`, `nexjob.jobs.expired`, `nexjob.job.duration`.

### Job context
```csharp
public class ImportJob : IJob<ImportInput>
{
    private readonly IJobContext _ctx;
    public ImportJob(IJobContext ctx) => _ctx = ctx;

    public async Task ExecuteAsync(ImportInput input, CancellationToken ct)
    {
        await _ctx.ReportProgressAsync(0, "Starting...", ct);
        // ... do work ...
        await _ctx.ReportProgressAsync(100, "Done.", ct);
    }
}
```

### Job tags
```csharp
await scheduler.EnqueueAsync<SendInvoiceJob, SendInvoiceInput>(
    input,
    tags: ["tenant:acme", $"invoice:{invoiceId}"]);

var jobs = await scheduler.GetJobsByTagAsync("tenant:acme");
```

---

## Concurrency & Throttling

### Resource throttling
```csharp
[Throttle(resource: "stripe", maxConcurrent: 3)]
public class ChargeCardJob : IJob<ChargeInput> { ... }
```

### Execution windows
Restrict queues to specific times:
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

---

## Payload Versioning

Handle evolving job inputs gracefully:

```csharp
[SchemaVersion(2)]
public class SendInvoiceJob : IJob<SendInvoiceInputV2> { ... }

public class Migration : IJobMigration<SendInvoiceInputV1, SendInvoiceInputV2>
{
    public SendInvoiceInputV2 Migrate(SendInvoiceInputV1 old)
        => new(old.OrderId, old.Email, Language: "en-US");
}

builder.Services.AddJobMigration<SendInvoiceInputV1, SendInvoiceInputV2, Migration>();
```

---

## Dashboard

**Web API:**
```csharp
app.UseNexJobDashboard("/dashboard");
```

**Worker Service / Console App:**
```csharp
services.AddNexJobStandaloneDashboard(configuration);
// Dashboard at http://localhost:5005/dashboard
```

---

# Configuration

## appsettings.json

```json
{
  "NexJob": {
    "Workers": 10,
    "MaxAttempts": 5,
    "PollingInterval": "00:00:05",
    "ShutdownTimeoutSeconds": 30,
    "DefaultQueue": "default",
    "Queues": [
      { "Name": "critical", "Workers": 3 },
      { "Name": "default",  "Workers": 5 }
    ]
  }
}
```

## Code configuration

```csharp
builder.Services.AddNexJob(builder.Configuration, opt =>
{
    opt.UsePostgres(connectionString);
    opt.Workers = 10;
    opt.MaxAttempts = 5;
});
```

---

## Storage Providers

All open-source. No license walls.

| Package | Storage | Status |
|---|---|---|
| `NexJob` | In-memory | Production ready |
| `NexJob.Postgres` | PostgreSQL 14+ | Production ready |
| `NexJob.SqlServer` | SQL Server 2019+ | Production ready |
| `NexJob.Redis` | Redis 7+ | Production ready |
| `NexJob.MongoDB` | MongoDB 6+ | Production ready |
| `NexJob.Oracle` | Oracle 19c+ | Planned |

---

## Testing

The in-memory provider is built-in:

```csharp
services.AddNexJob();  // InMemory by default
```

---

# Project

## Roadmap

```
v0.4.0  ✅ Wake-up channel · Deadlines · Dead-letter handlers · README updated
v1.0.0  ○ Stable API · production-ready
```

## Contributing

NexJob is open-source. Issues, PRs, and ideas welcome.

```bash
git clone git@github.com:oluciano/NexJob.git
cd NexJob && dotnet test
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for details.

## License

MIT © 2025 [Luciano Azevedo](https://github.com/oluciano)

---

<div align="center">
<br/>

*Built with obsession over developer experience.*

**If Hangfire is the past, NexJob is what comes next.**

<br/>
</div>
