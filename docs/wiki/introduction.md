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

### Write and run jobs

- [**`IJob` / `IJob<T>`**](concepts/job-types.md) — clean interfaces for parameterless and typed jobs, with full dependency injection support
- [**Job filters**](guides/job-filters.md) — implement `IJobExecutionFilter` for cross-cutting middleware (logging, correlation IDs, etc.)
- [**Job context, progress and checkpoints**](guides/job-context.md#progress-checkpoints-for-long-running-jobs) — report progress, and save a checkpoint so a retried job resumes where it stopped instead of starting over
- [**Job continuations**](concepts/continuations.md) — chain jobs with parent/child relationships using `ContinueWithAsync`
- [**Job priority**](concepts/scheduling.md#priority) — `JobPriority` controls execution order within a queue
- [**Tags and idempotency keys**](concepts/scheduling.md#idempotency-keys-and-tags) — label jobs and find them again

### Stay reliable

- [**Predictable retries**](concepts/retries-and-dead-letter.md) — configurable global delay policy plus per-job `[Retry]` attribute with exponential backoff
- [**Failures that should not be retried**](concepts/retries-and-dead-letter.md#failures-that-should-not-be-retried) — list exception types such as `ArgumentException` and the job goes straight to dead-letter instead of burning attempts
- [**Per-job attempt limit**](concepts/retries-and-dead-letter.md#per-job-attempt-limit) — pass `maxAttempts` when enqueuing, so the same job type can fail fast for one caller and retry longer for another
- [**Execution timeout**](concepts/retries-and-dead-letter.md#execution-timeout) — `[ExecutionTimeout]` cancels a job's token after a limit and treats it as a normal failure (opt-in, cooperative)
- [**Dead-letter handlers and forwarders**](concepts/retries-and-dead-letter.md) — an `IDeadLetterHandler<T>` runs when all retries are exhausted, and an `IDeadLetterForwarder` sees every such job, which is the hook for [alerts](guides/alerts.md)
- [**Crash recovery**](concepts/delivery-guarantees.md) — a job left behind by a node that died is found by its stale heartbeat and run again, or dead-lettered if it had no attempts left
- [**Deadline enforcement**](concepts/scheduling.md#deadlines) — jobs expire before execution if `deadlineAfter` has elapsed; no zombie jobs
- [**Idempotency**](concepts/idempotency.md) — `DuplicatePolicy` controls re-enqueue behavior for jobs with the same idempotency key
- [**Queue circuit breaker**](guides/circuit-breaker.md) — pauses a queue automatically when a dependency is down, probes it with one job, and ramps back up gradually
- [**Concurrency throttling**](guides/throttling.md) — `[Throttle]` caps per-resource concurrency locally; `AddNexJobDistributedThrottle()` enforces cluster-wide limits via [Redis](guides/throttling.md#distributed-throttling-with-redis)
- [**Execution windows**](guides/execution-windows.md) — restrict a queue to certain hours and days, such as nights only or business days
- [**Graceful shutdown**](guides/best-practices.md#graceful-shutdown) — running jobs are given time to finish, and the ones interrupted go back to the queue without using an attempt
- [**Delivery guarantees**](concepts/delivery-guarantees.md) — one table of what each failure costs a job

### Schedule

- [**Recurring jobs**](concepts/recurring-jobs.md) — register in code with `RecurringAsync` or [declare them in `appsettings.json`](concepts/recurring-jobs.md#configuration-via-appsettingsjson) with full timezone support
- [**Delayed and scheduled jobs**](concepts/scheduling.md#scheduled-execution) — run after a delay or at a specific time

### Operate

- [**Built-in dashboard**](integrations/dashboard.md) — standalone dark UI with live cluster topology, SSE log stream, and job catalog
- [**Dashboard authorization**](integrations/dashboard.md#dashboard-authorization) — implement `IDashboardAuthorizationHandler` to control who can view or operate the dashboard
- [**Runtime control**](guides/runtime-control.md) — pause and resume queues, requeue failed jobs, delete jobs and reset circuit breakers at runtime with `IJobControlService`
- [**Alerts**](guides/alerts.md) — know when a job fails for good, with a Slack recipe and the metrics to alert on
- [**Health checks**](guides/best-practices.md#monitoring-and-alerting) — `AddNexJob()` on the ASP.NET Core health checks builder reports whether storage is reachable
- [**OpenTelemetry**](integrations/opentelemetry.md) — distributed traces and metrics emitted out of the box
- [**Job retention**](guides/best-practices.md#control-storage-growth-with-retention-policies) — automatic cleanup of terminal jobs with configurable TTL via the `[Retention]` attribute
- [**Configuration**](reference/configuration.md) — `NexJobOptions` centralises worker count, polling interval, retry policy, and more

### Storage and integrations

- [**Five storage providers**](storage/overview.md) — PostgreSQL, SQL Server, Redis, MongoDB and in-memory, with no paid tier
- [**Read replicas**](storage/postgresql.md#dashboard-read-replica) — `UseDashboardReadReplica()` offloads dashboard queries to PostgreSQL or SQL Server read replicas
- [**Several services on one database**](guides/multi-service.md) — queue isolation, and foreign jobs from other microservices are deferred without penalizing attempt counts
- [**External triggers**](integrations/triggers.md) — turn messages from Kafka, RabbitMQ, AWS SQS, Azure Service Bus, Google Pub/Sub and Salesforce into jobs
- [**Resilient Outbox**](integrations/rabbitmq.md) — transaction-safe event producers for [RabbitMQ](integrations/rabbitmq.md) and [Apache Kafka](integrations/kafka.md)

### Trust

- [**Tested on real databases**](reference/how-we-test.md) — reliability scenarios with several nodes, a killed process, upgrades from the previous version, and an honest list of what is not covered

!!! note
    NexJob is released under the **MIT License** and is free for commercial use with no storage-provider paywalls or enterprise tiers. See the [LICENSE](https://github.com/oluciano/NexJob/blob/develop/LICENSE) file for full terms.


## Where to go next

- [Quick Start](quickstart.md) — install NexJob and run your first job in under 5 minutes
- [Mental Model](mental-model.md) — understand storage-first design, the job state machine, and crash recovery before writing production jobs
