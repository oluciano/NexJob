---
title: "NexJob Introduction: Reliable Background Jobs for .NET"
sidebarTitle: "Introduction"
description: "NexJob is a free, open-source background job library for .NET 8 with predictable retries, deadline enforcement, and a built-in real-time dashboard."
---

NexJob is a reliable, open-source background job processing library for .NET 8. It was built for developers who need jobs that **must run, fail safely, and leave a trace** — without paying for storage providers, wrestling with plugin ecosystems, or accepting silent failures. NexJob gives you predictable execution, built-in retries, deadline enforcement, dead-letter handling, and a real-time operational dashboard as first-class features from day one.

## Why NexJob?

If you have used Hangfire, you know the pain: free storage only covers InMemory, deadlines require a plugin, and observability is an afterthought. NexJob fixes all of that.

| Feature | NexJob | Hangfire |
|---|---|---|
| Storage providers | 5 free (PostgreSQL, SQL Server, Redis, MongoDB, InMemory) | Free InMemory only; others require a paid license |
| Deadline enforcement | Built-in (`deadlineAfter`) | Plugin required |
| Dead-letter handling | Automatic after exhausted retries | Manual |
| Dispatch latency | Near-zero (wake-up channel) | Polling-based |
| Dashboard | Built-in, standalone UI | Built-in (Pro required for advanced features) |
| OpenTelemetry | Built-in traces and metrics | Plugin required |
| Concurrency throttling | `[Throttle]` attribute per resource | Queue-level limits only |
| Ecosystem | Young library, focused scope | Mature ecosystem, many plugins |
| Package size | ~50 KB | ~2 MB |

NexJob is **not** a drop-in replacement for Hangfire. If you need calendar-based scheduling, distributed execution across untrusted networks, or a mature enterprise plugin ecosystem, Hangfire is the better fit. NexJob focuses on a tighter scope and delivers it with exceptional performance.

### Benchmark numbers

All measurements are per individual enqueue operation on .NET 8 (BenchmarkDotNet v0.14, RyuJIT AVX2, in-memory storage baseline):

| Metric | NexJob | Hangfire |
|---|---|---|
| Latency (mean) | **13.35 µs** | 35.95 µs — 2.7× slower |
| Memory allocated | **2.10 KB** | 11.20 KB — 81% more |
| GC Gen0 (per 1 k ops) | **0.06** | 0.85 — 14× more collections |
| GC Gen1 (per 1 k ops) | **0.00** | 0.18 — non-zero Gen1 cost |

Hangfire's overhead comes from runtime LINQ expression-tree parsing and reflection during enqueue. NexJob avoids both.

## Packages

Install only what you need. The core package ships with InMemory storage and the full dispatcher — every other package is optional.

| Package | Description |
|---|---|
| `NexJob` | Core library: dispatcher, scheduler, InMemory storage, retries, deadlines |
| `NexJob.Postgres` | PostgreSQL storage provider |
| `NexJob.SqlServer` | SQL Server storage provider |
| `NexJob.Redis` | Redis storage provider |
| `NexJob.MongoDB` | MongoDB storage provider |
| `NexJob.Dashboard` | Embedded ASP.NET Core dashboard middleware |
| `NexJob.Dashboard.Standalone` | Embedded HTTP dashboard server for Worker Services |
| `NexJob.OpenTelemetry` | OpenTelemetry SDK instrumentation (traces and metrics) |
| `NexJob.Trigger.AzureServiceBus` | Azure Service Bus trigger |
| `NexJob.Trigger.AwsSqs` | AWS SQS trigger |
| `NexJob.RabbitMQ` | RabbitMQ trigger and resilient outbox producer |
| `NexJob.Kafka` | Apache Kafka trigger and resilient outbox producer |
| `NexJob.Trigger.GooglePubSub` | Google Cloud Pub/Sub trigger |
| `NexJob.Trigger.Salesforce` | Salesforce Pub/Sub API trigger (gRPC and Avro) |
| `NexJob.Trigger.SalesforceStreaming` | Salesforce Streaming API trigger (CometD and Bayeux) |

## Key features

- **`IJob` / `IJob<T>`** — clean interfaces for parameterless and typed jobs, with full dependency injection support
- **Predictable retries** — configurable global delay policy plus per-job `[Retry]` attribute with exponential backoff
- **Deadline enforcement** — jobs expire before execution if `deadlineAfter` has elapsed; no zombie jobs
- **Dead-letter handlers** — register an `IDeadLetterHandler<T>` to run automatically when all retries are exhausted
- **Multi-service safe deferral** — foreign jobs from other microservices are deferred without penalizing attempt counts
- **Concurrency throttling** — `[Throttle]` attribute caps per-resource concurrency locally; `AddNexJobDistributedThrottle()` enforces cluster-wide limits via Redis
- **Job continuations** — chain jobs with parent/child relationships using `ContinueWithAsync`
- **Idempotency** — `DuplicatePolicy` controls re-enqueue behavior for jobs with the same idempotency key
- **Recurring jobs** — register in code with `RecurringAsync` or declare entirely in `appsettings.json` with full timezone support
- **Job filters** — implement `IJobExecutionFilter` for cross-cutting middleware (logging, correlation IDs, etc.)
- **Job retention** — automatic cleanup of terminal jobs with configurable TTL via the `[Retention]` attribute
- **Job priority** — `JobPriority` controls execution order within a queue
- **Runtime job control** — pause, resume, and cancel jobs at runtime with `IJobControlService`
- **Dashboard authorization** — implement `IDashboardAuthorizationHandler` to control who can view or operate the dashboard
- **Configuration** — `NexJobOptions` centralises worker count, polling interval, retry policy, and more
- **Read replicas** — `UseDashboardReadReplica()` offloads dashboard queries to PostgreSQL or SQL Server read replicas
- **Resilient Outbox** — transaction-safe event producers for RabbitMQ and Apache Kafka
- **OpenTelemetry** — distributed traces and metrics emitted out of the box
- **Built-in dashboard** — standalone dark UI with live cluster topology, SSE log stream, and job catalog

!!! note
    NexJob is released under the **MIT License** and is free for commercial use with no storage-provider paywalls or enterprise tiers. See the [LICENSE](https://github.com/oluciano/NexJob/blob/develop/LICENSE) file for full terms.


## Where to go next

- [Quick Start](quickstart.md) — install NexJob and run your first job in under 5 minutes
- [Mental Model](mental-model.md) — understand storage-first design, the job state machine, and crash recovery before writing production jobs
