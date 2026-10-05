<div align="center">

# NexJob

**Background jobs for .NET. Predictable. Observable. No magic.**

📖 **Documentation: [oluciano.github.io/NexJob](https://oluciano.github.io/NexJob/)** &nbsp;|&nbsp; 🎮 **Live Demo: [nexjob-playground.fly.dev](https://nexjob-playground.fly.dev/)**

[![NuGet](https://img.shields.io/nuget/v/NexJob.svg?style=flat-square&color=512bd4&label=nuget)](https://www.nuget.org/packages/NexJob)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NexJob?style=flat-square&color=512bd4)](https://www.nuget.org/packages/NexJob)
[![Build](https://img.shields.io/github/actions/workflow/status/oluciano/NexJob/ci.yml?style=flat-square)](https://github.com/oluciano/NexJob/actions)
[![Live Demo](https://img.shields.io/badge/live%20demo-fly.io-00cfd5?style=flat-square)](https://nexjob-playground.fly.dev/)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg?style=flat-square)](LICENSE)

<br/>

[![NexJob Enterprise Dashboard](docs/assets/dashboard-overview.png)](docs/assets/dashboard-overview.png)

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

**Write and run jobs**

- **[`IJob` / `IJob<T>`](https://oluciano.github.io/NexJob/concepts/job-types/)** — simple and structured job interfaces, with full dependency injection
- **[Job filters](https://oluciano.github.io/NexJob/guides/job-filters/)** — `IJobExecutionFilter` middleware for cross-cutting behaviour
- **[Progress and checkpoints](https://oluciano.github.io/NexJob/guides/job-context/#progress-checkpoints-for-long-running-jobs)** — report progress, and resume a retried job from its last checkpoint instead of starting over
- **[Job continuations](https://oluciano.github.io/NexJob/concepts/continuations/)** — chain jobs with parent/child relationships
- **[Job priority](https://oluciano.github.io/NexJob/concepts/scheduling/#priority)** — `JobPriority` controls the order within a queue

**Stay reliable**

- **[Predictable retries](https://oluciano.github.io/NexJob/concepts/retries-and-dead-letter/)** — a configurable global delay policy plus per-job `[Retry]` with exponential backoff
- **[Dead-letter handlers and forwarders](https://oluciano.github.io/NexJob/concepts/retries-and-dead-letter/)** — a fallback when all retries are exhausted, and a hook for [alerts](https://oluciano.github.io/NexJob/guides/alerts/)
- **[Failures that should not be retried](https://oluciano.github.io/NexJob/concepts/retries-and-dead-letter/#failures-that-should-not-be-retried)** — list exception types such as `ArgumentException` and the job goes straight to dead-letter instead of burning attempts
- **[Per-job attempt limit](https://oluciano.github.io/NexJob/concepts/retries-and-dead-letter/#per-job-attempt-limit)** — pass `maxAttempts` when enqueuing, so the same job type can fail fast for one caller and retry longer for another
- **[Execution timeout](https://oluciano.github.io/NexJob/concepts/retries-and-dead-letter/#execution-timeout)** — `[ExecutionTimeout]` cancels a job's token after a limit and treats it as a normal failure (opt-in, cooperative)
- **[Crash recovery](https://oluciano.github.io/NexJob/concepts/delivery-guarantees/)** — a job left behind by a node that died is found by its stale heartbeat and run again, or dead-lettered if it had no attempts left
- **[Deadline enforcement](https://oluciano.github.io/NexJob/concepts/scheduling/#deadlines)** — jobs expire if not executed in time (`deadlineAfter`)
- **[Idempotency](https://oluciano.github.io/NexJob/concepts/idempotency/)** — `DuplicatePolicy` controls re-enqueue behavior
- **[Queue circuit breaker](https://oluciano.github.io/NexJob/guides/circuit-breaker/)** — pauses a queue when a dependency is down, probes it with one job and ramps back up gradually
- **[Concurrency throttling](https://oluciano.github.io/NexJob/guides/throttling/)** — `[Throttle]` for per-resource limits, and `AddNexJobDistributedThrottle()` for global cluster-wide limits via Redis
- **[Execution windows](https://oluciano.github.io/NexJob/guides/execution-windows/)** — restrict a queue to certain hours and days, such as nights only or business days
- **[Delivery guarantees](https://oluciano.github.io/NexJob/concepts/delivery-guarantees/)** — one table of what each failure costs a job

**Schedule**

- **[Recurring jobs](https://oluciano.github.io/NexJob/concepts/recurring-jobs/)** — via code or [`appsettings.json`](https://oluciano.github.io/NexJob/concepts/recurring-jobs/#configuration-via-appsettingsjson), with time zones
- **[Delayed and scheduled jobs](https://oluciano.github.io/NexJob/concepts/scheduling/#scheduled-execution)** — run after a delay or at a specific time

**Operate**

- **[Built-in dashboard](https://oluciano.github.io/NexJob/integrations/dashboard/)** — standalone dark UI, zero configuration
- **[Runtime control](https://oluciano.github.io/NexJob/guides/runtime-control/)** — pause and resume queues, requeue failed jobs, delete jobs and reset circuit breakers with `IJobControlService`
- **[Alerts](https://oluciano.github.io/NexJob/guides/alerts/)** — know when a job fails for good, with a Slack recipe and the metrics to alert on
- **[Health checks](https://oluciano.github.io/NexJob/guides/best-practices/#monitoring-and-alerting)** and **[OpenTelemetry](https://oluciano.github.io/NexJob/integrations/opentelemetry/)** — traces and metrics built-in
- **[Job retention](https://oluciano.github.io/NexJob/guides/best-practices/#control-storage-growth-with-retention-policies)** — automatic cleanup of terminal jobs with configurable TTL

**Storage and integrations**

- **[Five storage providers](https://oluciano.github.io/NexJob/storage/overview/)** — PostgreSQL, SQL Server, Redis, MongoDB and in-memory
- **[Read replicas](https://oluciano.github.io/NexJob/storage/postgresql/#dashboard-read-replica)** — `UseDashboardReadReplica()` offloads dashboard queries (PostgreSQL, SQL Server)
- **[Several services on one database](https://oluciano.github.io/NexJob/guides/multi-service/)** — queue isolation, and foreign jobs from other microservices are deferred without penalizing attempts or dead-lettering
- **[External triggers](https://oluciano.github.io/NexJob/integrations/triggers/)** — turn Kafka, RabbitMQ, AWS SQS, Azure Service Bus, Google Pub/Sub and Salesforce messages into jobs
- **[Resilient Outbox](https://oluciano.github.io/NexJob/integrations/rabbitmq/)** — transaction-safe event producers for RabbitMQ and Apache Kafka

**Trust**

- **[Tested on real databases](https://oluciano.github.io/NexJob/reference/how-we-test/)** — scenarios with several nodes, a killed process and upgrades from the previous version, and an honest list of what is not covered

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
| `NexJob.Dashboard` | [![NuGet](https://img.shields.io/badge/nuget-v5.9.0-blue)](https://www.nuget.org/packages/NexJob.Dashboard) | Embedded ASP.NET Core dashboard middleware |
| `NexJob.Dashboard.Standalone` | [![NuGet](https://img.shields.io/badge/nuget-v5.9.0-blue)](https://www.nuget.org/packages/NexJob.Dashboard.Standalone) | Embedded HTTP dashboard server for Worker Services |
| `NexJob.OpenTelemetry` | [![NuGet](https://img.shields.io/badge/nuget-v5.9.0-blue)](https://www.nuget.org/packages/NexJob.OpenTelemetry) | OTel SDK instrumentation |
| `NexJob.Trigger.AzureServiceBus` | [![NuGet](https://img.shields.io/badge/nuget-v5.9.0-blue)](https://www.nuget.org/packages/NexJob.Trigger.AzureServiceBus) | Azure Service Bus trigger |
| `NexJob.Trigger.AwsSqs` | [![NuGet](https://img.shields.io/badge/nuget-v5.9.0-blue)](https://www.nuget.org/packages/NexJob.Trigger.AwsSqs) | AWS SQS trigger |
| `NexJob.RabbitMQ` | [![NuGet](https://img.shields.io/badge/nuget-v5.9.0-blue)](https://www.nuget.org/packages/NexJob.RabbitMQ) | RabbitMQ trigger & resilient outbox producer |
| `NexJob.Kafka` | [![NuGet](https://img.shields.io/badge/nuget-v5.9.0-blue)](https://www.nuget.org/packages/NexJob.Kafka) | Apache Kafka trigger & resilient outbox producer |
| `NexJob.Trigger.GooglePubSub` | [![NuGet](https://img.shields.io/badge/nuget-v5.9.0-blue)](https://www.nuget.org/packages/NexJob.Trigger.GooglePubSub) | Google Cloud Pub/Sub trigger |
| `NexJob.Trigger.Salesforce` | [![NuGet](https://img.shields.io/badge/nuget-v5.9.0-blue)](https://www.nuget.org/packages/NexJob.Trigger.Salesforce) | Salesforce Pub/Sub API trigger (gRPC & Avro) |
| `NexJob.Trigger.SalesforceStreaming` | [![NuGet](https://img.shields.io/badge/nuget-v5.9.0-blue)](https://www.nuget.org/packages/NexJob.Trigger.SalesforceStreaming) | Salesforce Streaming API trigger (CometD & Bayeux) |

---

## Dashboard

The dashboard provides real-time operational visibility into your background jobs, worker nodes, and message broker listeners — with zero external dependencies.

🎮 **Try it live:** [nexjob-playground.fly.dev](https://nexjob-playground.fly.dev/) (interactive scenario simulator: burst jobs, outages, retries, and dead-letters).

[![NexJob Live Broker Listeners & Event Triggers](docs/assets/dashboard-listeners.png)](docs/assets/dashboard-listeners.png)

- **Maxton Design System:** Modern 64px header, live cluster status indicator, and `Ctrl + K` instant job search.
- **Cluster Pipeline Topology Map:** Native SVG & CSS flow diagram showing Ingress Triggers ➔ Queue Buffers ➔ Worker Nodes.
- **Live Event Listeners:** Dedicated `/listeners` page monitoring connected message brokers (RabbitMQ, Kafka, AWS SQS, Azure Service Bus, Salesforce).
- **Job Catalog & Definitions (`/catalog`):** Aggregated job types, queue distribution, run counts, error rates, deep links to history, and on-demand ad-hoc triggering for parameterless jobs.
- **Server-Sent Events (SSE):** Streaming logs and real-time execution progress bars without page reloads.
- **Interactive Scenarios Drawer:** Opt-in simulator (`EnablePlayground = true`, disabled by default) to test burst loads and failure scenarios without writing custom scripts.
- **5 Built-In Themes:** Configurable default theme (`DefaultTheme = "semi-dark"`, `"blue-theme"`, `"dark"`, `"light"`, `"bordered"`).

### ASP.NET Core Web App

Install package:
```bash
dotnet add package NexJob.Dashboard
```

Configure in `Program.cs`:
```csharp
using NexJob;
using NexJob.Dashboard;

var builder = WebApplication.CreateBuilder(args);

// Register NexJob (InMemory storage by default, or your chosen provider)
builder.Services.AddNexJob();

var app = builder.Build();

// Mount dashboard at /dashboard
app.UseNexJobDashboard("/dashboard");

app.Run();
```

### Worker Service (Standalone)

For Worker Services or console applications without ASP.NET Core:
```bash
dotnet add package NexJob.Dashboard.Standalone
```

Configure in `Program.cs`:
```csharp
using NexJob.Dashboard.Standalone;

builder.Services.AddNexJob();

// Starts an embedded HTTP server at http://localhost:5005/dashboard
builder.Services.AddNexJobStandaloneDashboard(options =>
{
    options.Port = 5005;
    // options.DefaultTheme = "semi-dark";
    // options.EnablePlayground = true; // enable scenario drawer in dev/staging
});
```

---

## Documentation

Complete documentation is on the [Documentation Site](https://oluciano.github.io/NexJob/). Key pages:

- **[Mental Model](https://oluciano.github.io/NexJob/mental-model/)** — how NexJob works, read this first
- **[Quickstart](https://oluciano.github.io/NexJob/quickstart/)** — run your first job in 2 minutes
- **[Delivery Guarantees](https://oluciano.github.io/NexJob/concepts/delivery-guarantees/)** — what each failure costs a job: crashes, shutdown, throttling, pauses, deadlines
- **[Circuit Breaker](https://oluciano.github.io/NexJob/guides/circuit-breaker/)** and **[Throttling](https://oluciano.github.io/NexJob/guides/throttling/)** — protect a queue from a failing dependency, and limit concurrency
- **[Alerts](https://oluciano.github.io/NexJob/guides/alerts/)** — get notified when a job fails for good
- **[Best Practices](https://oluciano.github.io/NexJob/guides/best-practices/)** — production and Kubernetes guidelines
- **[Common Scenarios](https://oluciano.github.io/NexJob/guides/common-scenarios/)** — real-world use cases with code
- **[How We Test](https://oluciano.github.io/NexJob/reference/how-we-test/)** — what the test suite proves, and what it does not
- **[Troubleshooting](https://oluciano.github.io/NexJob/reference/troubleshooting/)** — debug common issues

---

## Samples & Reference Architecture

The [`samples/`](samples/) directory provides comprehensive, runnable reference architectures for all NexJob capabilities:

| Sample | Stack / Focus | Port | Highlights |
|---|---|---|---|
| [`NexJob.Sample.MinimalApi`](samples/NexJob.Sample.MinimalApi) | ASP.NET Core Minimal API | `5001` | Segregated `IDashboardStorage`, dead-letter handler, deadline enforcement |
| [`NexJob.Sample.WebApi`](samples/NexJob.Sample.WebApi) | ASP.NET Core Web API | `5002` | Dual-storage (InMemory / PostgreSQL), REST endpoints, `.http` file |
| [`NexJob.Sample.WorkerService`](samples/NexJob.Sample.WorkerService) | Headless Console Worker | `5005` (dashboard) | Standalone embedded HTTP dashboard server, graceful shutdown |
| [`NexJob.Sample.ConfiguredRecurring`](samples/NexJob.Sample.ConfiguredRecurring) | Declarative Recurring | `5004` | Zero-code recurring job registration via `appsettings.json` with timezones |
| [`NexJob.Sample.RabbitMQ`](samples/NexJob.Sample.RabbitMQ) | Broker Integration | `5009` | Outbox producer + trigger consumer with 5 trigger guarantees |
| [`NexJob.Sample.Kafka`](samples/NexJob.Sample.Kafka) | Streaming Broker | `5010` | Partitioned Outbox event publishing + consumer trigger with offset tracking |
| [`NexJob.Sample.Storage`](samples/NexJob.Sample.Storage) | Enterprise Topology | `5007` | PostgreSQL primary + read replica (`UseDashboardReadReplica`), Redis throttle (`AddNexJobDistributedThrottle`), OTel |
| [`NexJob.Sample.CloudTriggers`](samples/NexJob.Sample.CloudTriggers) | Unified Cloud Consumers | `5008` | AWS SQS, Azure Service Bus, GCP Pub/Sub, Salesforce gRPC & CometD with `/simulate/*` endpoints |
| [`NexJob.Sample.Reliability`](samples/NexJob.Sample.Reliability) | Reliability behaviors | `5011` | `[Retry]`, checkpoint resume, deadline, dead-letter, queue circuit breaker, `IJobControlService`, health checks |
| [`NexJob.Sample.Providers`](samples/NexJob.Sample.Providers) | One app, any storage | `5012` | InMemory / PostgreSQL / SQL Server / Redis / MongoDB chosen by `Sample:Provider` |

A full local test stack (PostgreSQL 16, Redis 7, RabbitMQ 3.13, and Kafka KRaft) is provided in [`samples/docker-compose.yml`](samples/docker-compose.yml).

---

## Benchmarks

Measured per individual enqueue operation on .NET 8 (BenchmarkDotNet v0.14, RyuJIT AVX2, in-memory storage baseline):

| Metric | NexJob | Hangfire | Comparison |
|---|---|---|---|
| Latency (Mean) | **13.35 µs** | 35.95 µs | **2.7× faster** |
| Memory (Allocated) | **2.10 KB** | 11.20 KB | **81% less memory** |
| GC Gen0 (per 1k ops) | **0.06** | 0.85 | **14× fewer Gen0 collections** |
| GC Gen1 (per 1k ops) | **0.00** | 0.18 | **Zero Gen1 collections** |

NexJob is **2.7× faster**, allocates **81% less memory**, and produces zero Gen1 garbage collections during enqueue bursts. Hangfire incurs additional CPU and allocation overhead due to runtime LINQ expression tree parsing and reflection.

Benchmarks can be parameterized by payload size (`PayloadBytes: 0, 1024, 10240`) and concurrency levels (`ConcurrencyLevel: 10, 50`) in [`benchmarks/NexJob.Benchmarks`](benchmarks/NexJob.Benchmarks).

---

## Ecosystem & Companion Projects

- **[qKafka](https://github.com/oluciano/QKafka)**: If you need event-driven choreographies, distributed sagas with compensating transactions, and native Kafka stream state machines, check out qKafka. NexJob focuses on background job scheduling and resilient outbox dispatch, seamlessly bridging events to and from Kafka.

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
v3.0.0  ✅ Storage segregation (IJobStorage / IRecurringStorage / IDashboardStorage),
           JobExecutor pipeline, IJobExecutionFilter, IJobControlService,
           UseDashboardReadReplica(), AddNexJobDistributedThrottle()
v4.0.0  ✅ Reliability hardening, crash recovery, orphaned job watcher, fault injection
v5.0.0  ✅ Resilient Outbox producers & triggers for RabbitMQ and Apache Kafka
v5.1.0  ✅ Salesforce triggers (gRPC Pub/Sub API + CometD Bayeux Streaming API)
v5.2.0  ✅ Dead-letter retention & chunked purging, consumer-driven triggers, OTel HPA gauges
v5.3.0  ✅ Kafka security delegates (ConfigureConsumer, ConfigureProducer, SASL/SSL PEM)
v5.4.0  ✅ Dashboard Maxton layout (5 themes), Cluster Topology, SSE log stream & Event Listeners
v5.4.1  ✅ Full IListenerRegistry integration across all external triggers (Salesforce, ASB, SQS, Pub/Sub)
v5.5.0  ✅ High-throughput batch fetching & batch acknowledgment across all storage providers
v5.6.0  ✅ Queue circuit breaker, job progress checkpoints, anti-bloat retention, structured log scope,
           dashboard job catalog / multi-cluster federation / ops-host mode, graceful-shutdown and
           storage-error hardening, Redis job index & crash-safe distributed throttle, trigger idempotency and
           transient-failure fixes
v5.6.1  ✅ Broker triggers deliver the message body to `IJob<string>` verbatim as text (fixes non-JSON bodies on every
           trigger and the PostgreSQL `jsonb` enqueue failure)
v5.6.2  ✅ Standalone dashboard warns when it is reachable from the network, valid empty-state icons, throttling and
           sample documentation aligned with the code, documentation site on GitHub Pages
v5.7.0  ✅ Standalone dashboard is loopback-only by default and enforces `IDashboardAuthorizationHandler`, the Workers control
           is gone from the dashboard, Postgres `NpgsqlDataSource` registration no longer crashes the host, SQL Server
           deadlocks under concurrent workers fixed, Redis job index reconciled hourly under a lock, database connection
           pool guidance and startup advisory
v5.8.0  ✅ `deadlineAfter` enforced on every database provider (new `expires_at`, migration V11), a saturated `[Throttle]`
           resource no longer starves a node and an interrupted job keeps its attempt, jobs exhausted by crashes reach
           dead-letter handlers, `IDeadLetterForwarder` with built-in Kafka/RabbitMQ forwarding, Redis delete ghost and
           MongoDB rolling-upgrade fixes, 24h throughput chart and CPU/RAM gauges in the dashboard, new guides (alerts,
           circuit breaker, execution windows, runtime control, delivery guarantees, queues)
v5.9.0  ✅ `[ExecutionTimeout]` and `DefaultExecutionTimeout` (cooperative cancellation, with a warning and the
           `nexjob.jobs.cancellation_ignored` counter for jobs that ignore it), per-job `maxAttempts` on
           `EnqueueAsync`/`ScheduleAsync`, `IgnoreRetryAttemptExceptions` to dead-letter without retrying,
           `DaysOfWeek` on execution windows, dashboard playground and default theme
```

---

<div align="center">
<br/>

*Built with obsession over developer experience and production reliability.*

<br/>

[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE) &nbsp;&nbsp; © 2025 [Luciano Azevedo](https://github.com/oluciano)

</div>
