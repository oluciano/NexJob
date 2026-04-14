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

## What is NexJob?

NexJob is a reliable background job processing library for .NET 8.
It gives you predictable execution, built-in retries, deadline enforcement, and operational visibility — without the complexity of traditional schedulers.

If you need jobs that **must run, fail safely, and leave a trace**, NexJob handles it as a first-class concern.

---

## Why not Hangfire?

NexJob was built for developers who want Hangfire-like reliability without the paid storage providers or the hidden complexity.

| Feature | NexJob | Hangfire |
|---|---|---|
| Storage providers | 5 free (PostgreSQL, SQL Server, Redis, MongoDB, InMemory) | Free InMemory only; others require paid license |
| Deadline enforcement | Built-in (`deadlineAfter`) | Plugin required |
| Dead-letter handling | Automatic after exhausted retries | Manual |
| Dispatch latency | Near-zero (wake-up channel) | Polling-based |
| Dashboard | Built-in, standalone UI | Built-in (Pro required for advanced) |
| OpenTelemetry | Built-in traces and metrics | Plugin required |
| Concurrency throttling | `[Throttle]` attribute per resource | Queue-level limits |
| Ecosystem | Young library, focused scope | Mature ecosystem, many plugins |
| Package size | ~50 KB | ~2 MB |

NexJob is not a drop-in replacement for Hangfire. If you need calendar-based scheduling, distributed execution across untrusted networks, or enterprise plugin ecosystems, Hangfire is the better choice.

---

## Quick Start

```bash
dotnet add package NexJob
```

```csharp
// 1. Register NexJob and scan for jobs
builder.Services.AddNexJob();
builder.Services.AddNexJobJobs(typeof(Program).Assembly);
```

```csharp
// 2. Define a job
public sealed class SendInvoiceJob : IJob<SendInvoiceInput>
{
    private readonly IEmailService _email;

    public SendInvoiceJob(IEmailService email) => _email = email;

    public async Task ExecuteAsync(SendInvoiceInput input, CancellationToken ct)
        => await _email.SendAsync(input.Email, "Your invoice", ct);
}

public sealed record SendInvoiceInput(string Email);
```

```csharp
// 3. Enqueue from anywhere
var scheduler = app.Services.GetRequiredService<IScheduler>();
await scheduler.EnqueueAsync<SendInvoiceJob, SendInvoiceInput>(
    new SendInvoiceInput("customer@example.com"),
    deadlineAfter: TimeSpan.FromMinutes(5));
```

The job expires if not started within 5 minutes — no silent failures, no zombie jobs.

---

## Core Features

- **`IJob` / `IJob<T>`** — simple and structured job interfaces
- **Predictable retries** — exponential backoff with configurable policies, global + per-job `[Retry]`
- **Deadline enforcement** — jobs expire if not executed in time (`deadlineAfter`)
- **Dead-letter handlers** — automatic fallback when all retries are exhausted
- **Concurrency throttling** — `[Throttle]` attribute for per-resource limits
- **Job continuations** — chain jobs with parent/child relationships
- **Idempotency** — `DuplicatePolicy` controls re-enqueue behavior
- **Recurring jobs** — via code or `appsettings.json`
- **Job filters** — `IJobExecutionFilter` middleware for cross-cutting behaviour
- **Job retention** — automatic cleanup of terminal jobs with configurable TTL
- **OpenTelemetry** — traces and metrics built-in
- **Built-in dashboard** — standalone dark UI, zero configuration

---

## Storage Providers

| Provider | Package | Status |
|---|---|---|
| InMemory | `NexJob` (core) | Production ready |
| PostgreSQL | `NexJob.Postgres` | Production ready |
| SQL Server | `NexJob.SqlServer` | Production ready |
| Redis | `NexJob.Redis` | Production ready |
| MongoDB | `NexJob.MongoDB` | Production ready |

All providers implement `IRuntimeSettingsStore` — runtime configuration persists across restarts.

---

## Packages

| Package | NuGet | Description |
|---|---|---|
| `NexJob.OpenTelemetry` | [![NuGet](https://img.shields.io/badge/nuget-v2.0.0-blue)](https://www.nuget.org/packages/NexJob.OpenTelemetry) | OTel SDK instrumentation |
| `NexJob.Trigger.AzureServiceBus` | [![NuGet](https://img.shields.io/badge/nuget-v2.0.0-blue)](https://www.nuget.org/packages/NexJob.Trigger.AzureServiceBus) | Azure Service Bus trigger |
| `NexJob.Trigger.AwsSqs` | [![NuGet](https://img.shields.io/badge/nuget-v2.0.0-blue)](https://www.nuget.org/packages/NexJob.Trigger.AwsSqs) | AWS SQS trigger |
| `NexJob.Trigger.RabbitMQ` | [![NuGet](https://img.shields.io/badge/nuget-v2.0.0-blue)](https://www.nuget.org/packages/NexJob.Trigger.RabbitMQ) | RabbitMQ trigger |
| `NexJob.Trigger.Kafka` | [![NuGet](https://img.shields.io/badge/nuget-v2.0.0-blue)](https://www.nuget.org/packages/NexJob.Trigger.Kafka) | Apache Kafka trigger |
| `NexJob.Trigger.GooglePubSub` | [![NuGet](https://img.shields.io/badge/nuget-v2.0.0-blue)](https://www.nuget.org/packages/NexJob.Trigger.GooglePubSub) | Google Cloud Pub/Sub trigger |

---

## Dashboard

The dashboard provides a visual timeline of every job's lifecycle — no log reconstruction needed.
See failures, retries, expired jobs, and execution timing at a glance.

```csharp
app.UseNexJobDashboard("/dashboard");
```

One line. No configuration required.

---

## Documentation

Complete documentation is in the [wiki](docs/wiki/Home.md). Key pages:

- **[Mental Model](docs/wiki/00-Mental-Model.md)** — how NexJob works, read this first
- **[Getting Started](docs/wiki/01-Getting-Started.md)** — run your first job in 2 minutes
- **[Best Practices](docs/wiki/13-Best-Practices.md)** — production guidelines
- **[Troubleshooting](docs/wiki/16-Troubleshooting.md)** — debug common issues
- **[Common Scenarios](docs/wiki/15-Common-Scenarios.md)** — real-world use cases with code

---

## Benchmarks

Measured per individual enqueue operation:

| Metric | NexJob | Hangfire |
|---|---|---|
| Latency | 9.3 µs | 26.6 µs |
| Memory | 1.67 KB | 11.2 KB |

NexJob is **2.87× faster** and uses **85% less memory** per enqueue. Benchmarks run with BenchmarkDotNet against comparable configurations.

---

## Roadmap

```
v0.4.0  ✅ Deadlines, dead-letter handlers, wake-up signaling
v0.5.0  ✅ Wake-up channel, recurring jobs, dashboard timeline
v0.6.0  ✅ Distributed reliability tests, recurring config redesign
v0.7.0  ✅ DuplicatePolicy, atomic commits, AI execution system
v0.8.0  ✅ Filters, persistent settings, job retention, wiki
v1.0.0  ✅ API freeze, production hardened
v2.0.0  ✅ External triggers, OpenTelemetry, metrics cache
```

---

<div align="center">
<br/>

*Built with obsession over developer experience and production reliability.*

<br/>

[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE) &nbsp;&nbsp; © 2025 [Luciano Azevedo](https://github.com/oluciano)

</div>
