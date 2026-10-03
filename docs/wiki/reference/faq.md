---
title: "NexJob Frequently Asked Questions: Complete Answers"
sidebarTitle: "FAQ"
description: "Answers to common questions about NexJob: storage choices, multi-instance deployments, job ordering, performance, and Hangfire migration."
---

These answers describe verified behavior as of v5.6.1 and later.

## General

???+ "If my app restarts or a node dies mid-job, what happens?"
    It depends on how the process stops.

    **Graceful stop (`SIGTERM`, deploy restart):** the host stops fetching new jobs and waits up to `ShutdownTimeout` (default 30 s) for running jobs to finish. A job interrupted by shutdown is **requeued immediately without consuming an attempt** and is never dead-lettered because of a shutdown.

    **Crash (`kill -9`, node lost):** the job stays `Processing`. NexJob detects the stale heartbeat and requeues the job:
    1. Workers refresh their heartbeat every `HeartbeatInterval` (default 30 s).
    2. A background watcher looks for `Processing` jobs with a heartbeat older than `HeartbeatTimeout` (default 5 min) and requeues them.
    3. If the job already consumed all its attempts, it becomes `Failed` instead of `Enqueued`.

    Detection is not instant. With defaults, a crashed job is picked up again in 5–10 minutes. Lower `HeartbeatTimeout` for faster recovery, but keep it higher than your longest job's expected duration. A crash **does** consume an attempt, because the attempt counter is incremented when the job is claimed.


???+ "Can a job run more than once?"
    Yes. NexJob gives you **at-least-once execution**, not exactly-once. A job can run again when:

    - Its node crashed mid-run and the job was requeued (the first run may have done part of the work).
    - The job succeeded but the result could not be stored after several retries. The executor requeues the job via the orphan detection path.
    - A retry follows a failure that happened after a side effect (e.g., the email was sent, then the database save failed).

    Write your jobs to be **idempotent**: use a natural key, check before you write, or pass an `idempotencyKey` when enqueuing.


???+ "Can several microservices share the same NexJob database?"
    Yes, as long as each service only fetches the jobs it can run. Two mechanisms keep this safe:

    1. **Queue isolation.** A host polls only queues listed in `NexJobOptions.Queues`. Give each service its own queue names and they never fetch each other's jobs.
    2. **Foreign job deferral.** If a service fetches a job whose type it cannot load, it does *not* count the attempt and does *not* dead-letter the job. It releases the job back after `ForeignJobRetryDelay` (default 5 s) so the correct service can claim it.

    The catch: if two services poll the **same** queue they keep bouncing each other's jobs. Nothing breaks, but it wastes fetches and delays execution. Separate queues are the recommended pattern. See the [Multi-Service guide](../guides/multi-service.md).


## Storage & Performance

???+ "How does NexJob prevent contention and deadlocks across multiple nodes?"
    Claiming a job is a **single atomic database statement**, so two nodes can never take the same job. Each storage provider uses its own mechanism:

    | Provider | How a job is claimed |
    |---|---|
    | PostgreSQL | `UPDATE … WHERE id = (SELECT … FOR UPDATE SKIP LOCKED) RETURNING *` — locked rows are skipped, not waited for |
    | SQL Server | `UPDLOCK, READPAST` on the select and `ROWLOCK, READPAST` on the update |
    | MongoDB | `FindOneAndUpdate`, atomic per document |
    | Redis | A Lua script executed atomically on the server |

    **Honest note on SQL Server:** the design avoids lock waits, but SQL Server is not proven deadlock-free under very high concurrency (issue [#279](https://github.com/oluciano/NexJob/issues/279)). v5.7.0 adds index hints that eliminate the most common deadlock path. If you run SQL Server with many workers, monitor logs for error 1205.


???+ "Won't a large job volume bloat my database?"
    Not with the default retention settings. NexJob automatically purges finished jobs on a schedule:

    | Status | Default retention |
    |---|---|
    | Succeeded | 7 days |
    | Failed | 30 days |
    | Expired | 7 days |
    | Dead-letter | 60 days |

    The retention service runs every `RetentionInterval` (default 1 hour) and deletes in batches of up to `RetentionBatchSize` (default 1,000 rows) to avoid lock escalation. Set any retention to `TimeSpan.Zero` to disable purging for that status.

    For high-frequency jobs, use per-job attributes to reduce storage further:

    ```csharp
    [Retention(PurgeOnSuccess = true)]       // Delete the row immediately on success
    public sealed class NoisyJob : IJob { ... }

    [Retention(TrimPayloadOnSuccess = true)] // Keep the row but wipe the input payload
    public sealed class BigPayloadJob : IJob<LargeInput> { ... }
    ```

    Job execution logs are capped at `MaxJobLogLines` (default 200) per execution.


???+ "How do I get the best throughput from NexJob?"
    Enable batch acknowledgment. By default, every job completion is a synchronous database write. With `EnableBatchAcknowledgment = true`, completions are aggregated and written in batches, cutting DB write roundtrips by over 90%:

    ```csharp
    options.EnableBatchAcknowledgment = true;
    ```

    In a load test with Apache Kafka and SQL Server (150,000 jobs), this raised throughput from ~30 jobs/s to ~320–470 jobs/s. NexJob also fetches jobs in dynamic batches based on available worker slots, eliminating the one-roundtrip-per-job bottleneck during backlog drain.


## Reliability

???+ "What happens when a job fails? Does it stop the whole system?"
    No. Exceptions are caught inside the executor for that one job. The worker slot is released and other jobs keep running.

    What happens to the failed job:

    1. The retry policy checks `Attempts < MaxAttempts`. The default global limit is 10 total executions.
    2. The job is rescheduled after a delay. The default is `attempt^4 + 15 + random(30) × (attempt+1)` seconds — roughly 16–74 s before the 2nd attempt and about 1.8 h before the 10th.
    3. When attempts run out, the job becomes `Failed` and its `IDeadLetterHandler<TJob>` is called, if one is registered.

    The dead-letter handler cannot crash the dispatcher — exceptions from it are logged and swallowed.


???+ "What is the queue circuit breaker and when should I use it?"
    The circuit breaker protects a queue whose jobs all depend on the same external service. When that service is down, every job burns its retries and hammers a system trying to recover. The breaker **stops fetching from the queue** after consecutive failures, then gradually lets jobs back in.

    States:

    ```
    Closed ──(N consecutive failures)──► Open ──(OpenDuration)──► HalfOpen
      ▲                                                               │
      │                                         one canary job runs ─┤
      │                                                               ▼
      └──────────────(RecoveryDuration)──── Recovering ◄──(canary ok)┘
                                        (limited concurrency)
    ```

    - **Open:** the dispatcher skips the queue entirely. Jobs stay `Enqueued` and lose no attempts.
    - **HalfOpen:** one canary job runs. If it fails, the queue reopens with a longer wait (`BackoffMultiplier`).
    - **Recovering:** only `RecoveryConcurrency` jobs run at once to avoid flooding the recovering service.

    Enable it per queue:

    ```csharp
    options.ConfigureQueue("payments", queue =>
    {
        queue.EnableCircuitBreaker(cb =>
        {
            cb.ConsecutiveFailuresThreshold = 5;
            cb.BreakOnTransientHttpErrors(); // 5xx, timeouts, 429; ignores 400/403/404/422
        });
    });
    ```

    Without a `BreakOn...` predicate, **every exception counts** toward the threshold, including your own bugs. Configure what should trip it. The circuit state is **per process** — each node tracks its own failures independently.

    **Use it** for queues that call one fragile external dependency. **Skip it** for queues of independent work.


???+ "Retry, dead-letter, throttle, or circuit breaker — which one do I need?"
    They answer different questions and are meant to be combined:

    | Mechanism | Question it answers | Scope | When it acts |
    |---|---|---|---|
    | **Retry** | This run failed. Should I try again? | One job | After a failure |
    | **Dead-letter** | I'm out of attempts. What now? | One job | After the last failure |
    | **Throttle** | How many jobs may use this resource at once? | One resource, any queue | Before execution |
    | **Circuit breaker** | Is the destination down? Should I stop sending? | A whole queue | After repeated failures |

    Quick rules:
    - One job fails, others are fine → **retry**.
    - Every job in the queue fails at once → **circuit breaker**.
    - The destination accepts only N concurrent calls → **throttle**.
    - Retries are exhausted and the job still matters → **dead-letter handler** (alert, compensate, park for human review).

    A throttled job that cannot get its slot waits briefly in `Processing`, holding its worker slot, and after about 5 seconds goes back to the queue without using an attempt. Jobs on a saturated resource can be delayed and can run in a different order. Keep `Workers` above the sum of your `maxConcurrent` values, or isolate throttled jobs in their own queue.


???+ "Why was my job marked Expired instead of running?"
    You enqueued it with a `deadlineAfter` parameter and the deadline passed before a worker picked it up. The expiry check happens **before** the job starts. A deadline does not interrupt a job already running.

    Common causes: the queue was paused, the circuit breaker was open, all workers were busy, or the node was down. Jobs wait in `Enqueued` while any of these conditions hold, and the clock keeps running.

    `deadlineAfter` only works with `EnqueueAsync`. Scheduled jobs have no built-in deadline — `ScheduledAt` is the *earliest* start time, not a hard cutoff.


## Dashboard

???+ "Do I need a separate ASP.NET Core server for the dashboard?"
    No. There are two hosting options:

    | Host type | Package | Registration |
    |---|---|---|
    | ASP.NET Core app | `NexJob.Dashboard` | `app.UseNexJobDashboard()` |
    | Worker Service / console | `NexJob.Dashboard.Standalone` | `services.AddNexJobStandaloneDashboard()` |

    The standalone package starts a small embedded web server inside your worker process (port 5005 by default). You deploy nothing extra.

    To run a dashboard-only host with no job execution, set `DisableWorkers = true` in the standalone options. It sets `Workers = 0` for that process.

    **Security note:** the standalone dashboard binds to loopback only by default. Set `LocalhostOnly = false` in containers and register an `IDashboardAuthorizationHandler`. Without a handler, NexJob logs a startup warning.


???+ "How do OpenTelemetry tracing and logging work?"
    **Tracing.** When you enqueue a job, NexJob stores the current W3C `traceparent` in the job record. When the job runs, the executor starts an activity named after the job type and continues that trace, so the enqueue and execution appear in the same trace even across processes. Failures add an `exception` event with the full type, message, and stack.

    Register the `NexJob` activity source and meter with the OTel SDK, or use the `NexJob.OpenTelemetry` package.

    **Metrics.** All instruments are under the `NexJob` meter: `nexjob.jobs.enqueued`, `nexjob.jobs.succeeded`, `nexjob.jobs.failed`, `nexjob.jobs.expired` (counters); `nexjob.job.duration` (histogram, ms); `nexjob.queue.depth`, `nexjob.workers.active`, `nexjob.workers.total` (gauges).

    **Structured logging.** Every log line emitted while a job runs inherits a scope with `NexJob.JobId`, `NexJob.JobType`, `NexJob.Queue`, `NexJob.Attempt`, and `NexJob.TraceParent`. This lets you filter and correlate in Loki, Splunk, Datadog, or Elastic without any extra configuration.


## Comparison with Hangfire

???+ "How does NexJob differ from Hangfire?"
    NexJob is designed for high-throughput, cloud-native .NET workloads. Key differences:

    - **Performance.** NexJob's enqueue latency benchmarks at ~13 µs with ~2 KB allocation vs Hangfire's ~36 µs and ~11 KB (measured on .NET 8 RyuJIT). Batch fetching and batch acknowledgment further reduce DB roundtrips.
    - **At-least-once semantics are explicit.** NexJob documents that jobs may run more than once and guides you toward idempotency rather than attempting false exactly-once guarantees.
    - **Built-in broker triggers.** NexJob includes native trigger packages for Kafka, RabbitMQ, Azure Service Bus, AWS SQS, Google Pub/Sub, and Salesforce — no third-party glue required.
    - **Queue circuit breaker.** A per-queue circuit breaker with progressive backoff is built into the core, not an add-on.
    - **OpenTelemetry first.** Distributed trace propagation, structured log scopes, and OTEL-compatible metrics are built into the core, not added through community middleware.
    - **Storage choice.** NexJob supports PostgreSQL, SQL Server, MongoDB, Redis, and InMemory out of the box, with a clean `IStorageProvider` interface for custom backends.

    For a step-by-step guide to migrating a Hangfire application, see the [Migration guide](../reference/migration.md).


???+ "What does a broker trigger do with a non-JSON message body?"
    Since v5.6.1, all five broker triggers (SQS, Service Bus, Pub/Sub, Kafka, RabbitMQ) pass the message body verbatim to `IJob<string>`. The body can be JSON, XML, CSV, or plain text — you parse it inside the job.

    Before v5.6.1, only a body that was itself a JSON string literal would execute without error. Any other format failed with `JsonException`, and on PostgreSQL a non-JSON body was rejected at enqueue by the storage layer.

    If you worked around the old behavior by publishing a body wrapped in JSON string quotes (e.g., `"\"hello\""` with explicit double quotes), your job now receives those quotes as part of the string. Remove the extra quoting from your publisher.

