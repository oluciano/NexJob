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

**Background jobs for .NET. Predictable. Observable. No magic.**

[![NuGet](https://img.shields.io/nuget/v/NexJob.svg?style=flat-square&color=512bd4&label=nuget)](https://www.nuget.org/packages/NexJob)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NexJob?style=flat-square&color=512bd4)](https://www.nuget.org/packages/NexJob)
[![Build](https://img.shields.io/github/actions/workflow/status/oluciano/NexJob/ci.yml?style=flat-square)](https://github.com/oluciano/NexJob/actions)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg?style=flat-square)](LICENSE)

<br/>

</div>

---

## NexJob

**Background jobs that execute reliably and enforce deadlines. No hidden state. No surprises in production.**

Jobs expire if not started in time. Storage is authoritative. State transitions are persisted. This is how background processing should work.

---

## Why NexJob

- **Deadlines are enforced**: Jobs expiring before execution are marked `Expired` and skipped. No silent failures.
- **Storage owns state**: All state persisted. Multi-instance safe from day one. Dispatcher is stateless.
- **Predictable execution**: No serialization. Jobs run natively with async/await. One job instance per execution.
- **Explicit failure handling**: Retries are configured. Dead-letter handlers trigger for permanent failures.
- **Built for visibility**: Live timeline, execution history, failure tracking. OpenTelemetry integration.
- **No hidden behavior**: What you see is what happens. Clear, deterministic execution model.

---

## Key Features

- **Deadline enforcement** — jobs expire if not started in time
- **Dead-letter handlers** — handle permanent failures automatically
- **Retry policies** — global and per-job control
- **Resource throttling** — limit concurrency per resource
- **Live dashboard** — execution timeline, history, observability
- **Graceful shutdown** — jobs complete naturally; strays are requeued
- **All storage providers free** — PostgreSQL, SQL Server, Redis, MongoDB
- **Live config** — pause/resume, adjust workers at runtime

---

## Quick Example

Define a job:

```csharp
public class SendInvoiceJob : IJob<SendInvoiceInput>
{
    private readonly IEmailService _email;
    public SendInvoiceJob(IEmailService email) => _email = email;

    public async Task ExecuteAsync(SendInvoiceInput input, CancellationToken ct)
        => await _email.SendAsync(input.Email, "Invoice attached", ct);
}
```

Register and enqueue with deadline:

```csharp
builder.Services.AddNexJob(builder.Configuration)
                .AddNexJobJobs(typeof(Program).Assembly);

var scheduler = app.Services.GetRequiredService<IScheduler>();

// Enqueue with 5-minute deadline. Job expires if not started in time.
await scheduler.EnqueueAsync<SendInvoiceJob, SendInvoiceInput>(
    new(orderId, email),
    deadlineAfter: TimeSpan.FromMinutes(5));
```

Job executes immediately on an available worker. If it misses the deadline, it's marked `Expired` and skipped.

---

## Dashboard

![Dashboard Timeline](./assets/dashboard-timeline.png)

**Visual timeline, not logs.**

Logs force you to reconstruct what happened. The dashboard shows it directly: every job's exact state and when it transitioned. See failures, retries, expired jobs, and execution timing at a glance.

**Live progress reporting** for long-running jobs:

```csharp
public async Task ExecuteAsync(ImportInput input, CancellationToken ct)
{
    await _ctx.ReportProgressAsync(0, "Starting...", ct);
    // ... do work ...
    await _ctx.ReportProgressAsync(100, "Done.", ct);
}
```

**Enable it**:

```csharp
app.UseNexJobDashboard("/dashboard");
```

One dashboard view. Full system visibility. No investigation required.

---

## Core Concepts

### Job Types

- **`IJob`** — jobs with no input
- **`IJob<T>`** — jobs with structured input

### Lifecycle

Jobs move through states: **Enqueued** → **Processing** → **Succeeded** (or **Failed** → retry).

Terminal states: **Dead-letter** (exhausted retries), **Expired** (deadline missed).

### Deadlines

Set at enqueue. Checked before execution. Expired jobs are marked `Expired` and skipped:

```csharp
await scheduler.EnqueueAsync<PaymentJob, PaymentInput>(
    input,
    deadlineAfter: TimeSpan.FromMinutes(5));
```

### Retries

Global policy. Per-job override:

```csharp
[Retry(5, InitialDelay = "00:00:30", Multiplier = 2.0, MaxDelay = "01:00:00")]
public class PaymentJob : IJob<PaymentInput> { ... }
```

### Dead-Letter Handling

Triggered when retries exhausted. Handler runs in isolated scope:

```csharp
public class PaymentDeadLetterHandler : IDeadLetterHandler<PaymentJob>
{
    public async Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken ct)
        => await _alerts.SendAsync("Payment failed", ct);
}

builder.Services.AddTransient<IDeadLetterHandler<PaymentJob>, PaymentDeadLetterHandler>();
```

---

## Installation

```bash
# Core
dotnet add package NexJob

# Storage (pick one)
dotnet add package NexJob.Postgres
dotnet add package NexJob.SqlServer
dotnet add package NexJob.Redis
dotnet add package NexJob.MongoDB

# Dashboard (optional)
dotnet add package NexJob.Dashboard
```

---

## Philosophy

Explicit behavior over magic. Jobs execute natively. Storage owns state. Deadlines are enforced. Failures are handled, not hidden. If it's not obvious from the code, it's not in NexJob.

---

## Roadmap

```
v0.4.0  ✅ Deadlines, dead-letter handlers, wake-up signaling
v1.0.0  ○ Stable API, production hardened, all providers tested
v2.0.0  ○ Distributed coordination, multi-node consistency
```

---

<div align="center">
<br/>

*Built with obsession over developer experience and production reliability.*

<br/>

[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE) &nbsp;&nbsp; © 2025 [Luciano Azevedo](https://github.com/oluciano)

</div>
