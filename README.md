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
- The license is LGPL for the core and paid for anything production-worthy.

NexJob was built to solve all of that. MIT license, end to end. Every storage adapter open-source. Native `async/await` from the ground up. OpenTelemetry built in, not bolted on. A dashboard you'd actually want to show someone.

---

## At a glance

| | NexJob | Hangfire |
|---|:---:|:---:|
| License | MIT | LGPL / paid Pro |
| `async/await` native | ✅ | ❌ |
| Priority queues | ✅ | ❌ |
| Resource throttling | ✅ | ❌ |
| OpenTelemetry built-in | ✅ | ❌ |
| Payload versioning | ✅ | ❌ |
| Idempotency keys | ✅ | ❌ |
| All storage adapters free | ✅ | ❌ |
| In-memory for testing | ✅ | ✅ |
| Cron / recurring jobs | ✅ | ✅ |

---

## Installation

```bash
# Core (includes in-memory provider for dev/tests)
dotnet add package NexJob

# Pick your storage
dotnet add package NexJob.Postgres
dotnet add package NexJob.SqlServer
dotnet add package NexJob.Redis

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
    opt.UsePostgres(connectionString);
    opt.Workers = 10;
});
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
```

### 4 — Dashboard

```csharp
app.UseNexJobDashboard("/jobs");
```

Open `/jobs` and see every queue, every job state, every retry — live.

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

Don't overwhelm external APIs. Declare a limit once, NexJob enforces it across all workers.

```csharp
[Throttle(resource: "stripe", maxConcurrent: 3)]
public class ChargeCardJob : IJob<ChargeInput> { ... }

[Throttle(resource: "sendgrid", maxConcurrent: 5)]
public class BulkEmailJob : IJob<EmailInput> { ... }
```

Both `ChargeCardJob` and any other job with `resource: "stripe"` share the same concurrency slot — globally, across all server instances.

---

## Observability

NexJob emits OpenTelemetry spans for every job — enqueue, execute, retry, fail. Zero configuration if you already have OTEL wired up.

```csharp
builder.Services.AddNexJob(opt =>
{
    opt.UsePostgres(connectionString);
    opt.UseOpenTelemetry();
});
```

Every span carries:

```
nexjob.job_id       = "3f2a8c1d-..."
nexjob.job_type     = "SendInvoiceJob"
nexjob.queue        = "default"
nexjob.attempt      = 1
nexjob.status       = "succeeded"
nexjob.duration_ms  = 142
```

---

## Payload versioning

Your job inputs will change. NexJob handles it gracefully — no data loss, no broken jobs stuck in the queue.

```csharp
// Old input (v1)
public record SendInvoiceInputV1(Guid OrderId, string Email);

// New input (v2)
public record SendInvoiceInputV2(Guid OrderId, string Email, string Language);

// Migration — discovered and applied automatically before execution
public class SendInvoiceMigrationV1ToV2 : IJobMigration<SendInvoiceInputV1, SendInvoiceInputV2>
{
    public SendInvoiceInputV2 Migrate(SendInvoiceInputV1 old)
        => new(old.OrderId, old.Email, Language: "en-US");
}
```

---

## Testing

The in-memory provider requires zero setup. Use `TestScheduler` to assert without executing.

```csharp
services.AddNexJob(opt => opt.UseInMemory());

var scheduler = new TestScheduler();
await sut.PlaceOrderAsync(order);

scheduler.ShouldHaveEnqueued<SendInvoiceJob, SendInvoiceInput>(
    job => job.OrderId == order.Id);
```

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

Override per job:

```csharp
[Retry(attempts: 3)]
public class SendSmsJob : IJob<SmsInput> { ... }

[Retry(attempts: 0)]
public class IdempotentWebhookJob : IJob<WebhookInput> { ... }
```

---

## Storage providers

All open-source. No license walls.

| Package | Storage | Notes |
|---|---|---|
| `NexJob` | In-memory | Dev and testing only |
| `NexJob.Postgres` | PostgreSQL 14+ | `SELECT FOR UPDATE SKIP LOCKED` |
| `NexJob.SqlServer` | SQL Server 2019+ | Atomic dequeue guaranteed |
| `NexJob.Redis` | Redis 7+ | Lua scripts for atomicity |

Bring your own? Implement `IStorageProvider` — one interface, ten methods.

---

## Configuration reference

```csharp
builder.Services.AddNexJob(opt =>
{
    opt.UsePostgres(connectionString);

    opt.Workers = 10;
    opt.Queues = ["critical", "default", "low"];
    opt.DefaultQueue = "default";
    opt.PollingInterval = TimeSpan.FromSeconds(5);
    opt.HeartbeatInterval = TimeSpan.FromSeconds(30);
    opt.HeartbeatTimeout = TimeSpan.FromMinutes(5);
    opt.DefaultRetryAttempts = 5;

    opt.UseOpenTelemetry();
});
```

---

## Roadmap

```
v0.1  ◆ Core interfaces · in-memory provider · fire-and-forget     ← here
v0.2  ○ PostgreSQL provider · delayed jobs · recurring (cron)
v0.3  ○ Priority queues · resource throttling · continuations
v0.4  ○ Dashboard (Blazor SSR) · real-time streaming
v0.5  ○ SQL Server · Redis providers
v0.6  ○ OpenTelemetry · payload versioning
v1.0  ○ Stable API · production-ready
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
