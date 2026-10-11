---
title: "NexJob Introduction: Reliable Background Jobs for .NET"
sidebarTitle: "Introduction"
description: "NexJob is a free, open-source background job library for .NET 8 with predictable retries, deadline enforcement, and a built-in real-time dashboard."
---

NexJob is a reliable, open-source background job processing library for .NET 8. It was built for developers who need jobs that **must run, fail safely, and leave a trace** — without a paid tier, hidden complexity, or silent failures. NexJob gives you predictable execution, built-in retries, deadline enforcement, dead-letter handling, and a real-time operational dashboard as first-class features from day one.

## Why NexJob?

NexJob is for jobs that **must run, fail safely and leave a trace**.

- **Five storage providers, none behind a paid tier:** PostgreSQL, SQL Server, Redis, MongoDB and in-memory, with the same test contract on all of them ([how we test](reference/how-we-test.md)).
- **Failure is a first-class case.** [Deadlines](concepts/scheduling.md#deadlines), [dead-letter handling](concepts/dead-letter.md), crash recovery and a table of [what each failure costs a job](concepts/delivery-guarantees.md) are part of the core library.
- **Built-in dashboard and OpenTelemetry**, with no extra package to find or license.
- **Near-zero dispatch latency** on the node that enqueues, through an in-process wake-up channel.

NexJob is a young library with a focused scope.

### Benchmark numbers

Measured per individual enqueue operation on .NET 8 (BenchmarkDotNet v0.14, RyuJIT AVX2, in-memory storage baseline):

| Metric | NexJob |
|---|---|
| Latency (mean) | **13.35 µs** |
| Memory allocated | **2.10 KB** |
| GC Gen0 (per 1 k ops) | **0.06** |
| GC Gen1 (per 1 k ops) | **0.00** |

These are enqueue-only numbers on in-memory storage, not end-to-end throughput. The benchmark project is in the repository (`benchmarks/NexJob.Benchmarks`), so you can run it against your own setup.

## Packages

Install only what you need. The core package ships with in-memory storage and the full dispatcher; every other package is optional and adds one capability.

### Core

| Package | What it is |
|---|---|
| `NexJob` | The library: dispatcher, scheduler, retries, deadlines, in-memory storage and the [Roslyn analyzers](guides/analyzers.md). Every application needs it. |

### Storage providers

Pick one. Without any of these, jobs live in memory and are lost when the process stops. See [Storage Providers](storage/overview.md).

| Package | Backend |
|---|---|
| `NexJob.Postgres` | PostgreSQL |
| `NexJob.SqlServer` | SQL Server |
| `NexJob.Redis` | Redis (also offers opt-in [distributed throttle](guides/throttling.md#distributed-throttling-with-redis)) |
| `NexJob.MongoDB` | MongoDB |

### Dashboard

Pick the one that matches your host. See [Dashboard](integrations/dashboard.md).

| Package | Host |
|---|---|
| `NexJob.Dashboard` | ASP.NET Core: mounts the dashboard as middleware |
| `NexJob.Dashboard.Standalone` | Worker Service or console app: runs an embedded HTTP server |

### Observability

| Package | What it adds |
|---|---|
| `NexJob.OpenTelemetry` | Traces and metrics through the OpenTelemetry SDK. See [OpenTelemetry](integrations/opentelemetry.md). |

### Triggers and outbox

A trigger turns messages from a broker into jobs. The two outbox packages also publish events from your application in a transaction-safe way. See [External Triggers](integrations/triggers.md).

| Package | Broker | Trigger | Outbox |
|---|---|---|---|
| `NexJob.RabbitMQ` | RabbitMQ | yes | yes |
| `NexJob.Kafka` | Apache Kafka | yes | yes |
| `NexJob.Trigger.AzureServiceBus` | Azure Service Bus | yes | |
| `NexJob.Trigger.AwsSqs` | AWS SQS | yes | |
| `NexJob.Trigger.GooglePubSub` | Google Cloud Pub/Sub | yes | |
| `NexJob.Trigger.Salesforce` | Salesforce Pub/Sub API (gRPC and Avro) | yes | |
| `NexJob.Trigger.SalesforceStreaming` | Salesforce Streaming API (CometD and Bayeux) | yes | |

## Key features

### Write and run jobs

- [**`IJob` / `IJob<T>`**](concepts/job-types.md) — clean interfaces for parameterless and typed jobs, with full dependency injection support
- [**Job filters**](guides/job-filters.md) — implement `IJobExecutionFilter` for cross-cutting middleware (logging, correlation IDs, etc.)
- [**Job context, progress and checkpoints**](guides/job-context.md#progress-checkpoints-for-long-running-jobs) — report progress, and save a checkpoint so a retried job resumes where it stopped instead of starting over
- [**Job continuations**](concepts/continuations.md) — chain jobs with parent/child relationships using `ContinueWithAsync`
- [**Job priority**](concepts/scheduling.md#priority) — `JobPriority` controls execution order within a queue
- [**Tags and idempotency keys**](concepts/scheduling.md#idempotency-keys-and-tags) — label jobs and find them again

### Stay reliable

- [**Predictable retries**](concepts/retries.md) — configurable global delay policy plus per-job `[Retry]` attribute with exponential backoff
- [**Failures that should not be retried**](concepts/retries.md#failures-that-should-not-be-retried) — list exception types such as `ArgumentException` and the job goes straight to dead-letter instead of burning attempts
- [**Per-job attempt limit**](concepts/retries.md#per-job-attempt-limit) — pass `maxAttempts` when enqueuing, so the same job type can fail fast for one caller and retry longer for another
- [**Execution timeout**](concepts/retries.md#execution-timeout) — `[ExecutionTimeout]` cancels a job's token after a limit and treats it as a normal failure (opt-in, cooperative)
- [**Dead-letter handlers and forwarders**](concepts/dead-letter.md) — an `IDeadLetterHandler<T>` runs when all retries are exhausted, and an `IDeadLetterForwarder` sees every such job, which is the hook for [alerts](guides/alerts.md)
- [**Crash recovery**](concepts/delivery-guarantees.md) — a job left behind by a node that died is found by its stale heartbeat and run again, or dead-lettered if it had no attempts left
- [**Deadline enforcement**](concepts/scheduling.md#deadlines) — jobs expire before execution if `deadlineAfter` has elapsed; no zombie jobs
- [**Idempotency**](concepts/idempotency.md) — `DuplicatePolicy` controls re-enqueue behavior for jobs with the same idempotency key
- [**Queue isolation by default**](concepts/queues.md#the-default-queue) — the default queue is `{prefix}.default` per application, so services that share a database do not share a queue; the legacy `default` is still drained
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
- [Migration guide](reference/migration.md) — breaking changes and what to update when you upgrade between NexJob versions
