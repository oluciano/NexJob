# FAQ

Frequently asked questions, from the basics to the tricky ones.

Every answer is taken from the source code, not from memory. Each one ends with a **Verified in** line that points to the file (and a test when one exists) so you can check it yourself.

> File and line references describe **v5.6.1**. Lines move as the code evolves; the file and method names are the stable part.

**Architecture, database and performance**
- [How does NexJob handle several nodes consuming the same database without contention or deadlocks?](#how-does-nexjob-handle-several-nodes-consuming-the-same-database-without-contention-or-deadlocks)
- [If my application restarts or a node dies in the middle of a job, what happens?](#if-my-application-restarts-or-a-node-dies-in-the-middle-of-a-job-what-happens)
- [Won't a high job volume bloat my database?](#wont-a-high-job-volume-bloat-my-database)

**Reliability, failures and operation**
- [What happens when a job fails? Does the error stop the system?](#what-happens-when-a-job-fails-does-the-error-stop-the-system)
- [What is the per-queue circuit breaker and when should I use it?](#what-is-the-per-queue-circuit-breaker-and-when-should-i-use-it)
- [Retry, dead-letter, throttle or circuit breaker: which one do I need?](#retry-dead-letter-throttle-or-circuit-breaker-which-one-do-i-need)
- [Can a job run more than once?](#can-a-job-run-more-than-once)
- [Why was my job marked Expired instead of running?](#why-was-my-job-marked-expired-instead-of-running)
- [Can several microservices share the same NexJob database?](#can-several-microservices-share-the-same-nexjob-database)

**Integration and observability**
- [Do I need a dedicated ASP.NET Core server just for the dashboard?](#do-i-need-a-dedicated-aspnet-core-server-just-for-the-dashboard)
- [How do OpenTelemetry tracing and logging work?](#how-do-opentelemetry-tracing-and-logging-work)
- [What does a broker trigger do with a message body that is not JSON?](#what-does-a-broker-trigger-do-with-a-message-body-that-is-not-json)

---

## Architecture, database and performance

### How does NexJob handle several nodes consuming the same database without contention or deadlocks?

Claiming a job is a **single atomic statement in the database**, so two nodes can never take the same job. Each provider uses its own primitive:

| Provider | How a job is claimed |
|---|---|
| PostgreSQL | `UPDATE ... WHERE id = (SELECT ... FOR UPDATE SKIP LOCKED) RETURNING *`. Locked rows are skipped, not waited for. |
| SQL Server | `UPDLOCK, READPAST` hints on the select and `ROWLOCK, READPAST` on the update. |
| MongoDB | `FindOneAndUpdate`, atomic per document. |
| Redis | A Lua script (`FetchNextScript`), executed atomically on the server. |

In PostgreSQL, the same statement that claims the job also moves it to `Processing`, stamps the heartbeat and increments `attempts`.

**About deadlocks, honestly:** the design avoids lock waits (`SKIP LOCKED` / `READPAST`), but SQL Server is not proven deadlock-free under load. Issue [#279](https://github.com/oluciano/NexJob/issues/279) is open: in the distributed reliability suite, several SQL Server tests failed with error 1205 (deadlock victim) under many concurrent workers. It is not yet established whether this also happens in production. If you run SQL Server with many workers, watch for that error.

**Verified in:** `PostgresStorageProvider.cs` (`FetchNextAsync`, `FOR UPDATE SKIP LOCKED`), `SqlServerStorageProvider.cs` (`UPDLOCK, READPAST`), `MongoStorageProvider.cs` (`FindOneAndUpdateAsync`), `RedisStorageProvider.cs` (`FetchNextScript`). Open issue #279 for the SQL Server caveat.

---

### If my application restarts or a node dies in the middle of a job, what happens?

It depends on *how* the process stops.

**Graceful stop (deploy, `SIGTERM`):** the host stops fetching new jobs and waits up to `ShutdownTimeout` (default 30 s) for running jobs. A job interrupted by the shutdown is **requeued immediately and the attempt is not consumed**. It is never dead-lettered because of a shutdown.

**Crash (`kill -9`, node lost):** nothing gets to run, so the job stays `Processing`.
1. While a job runs, its worker refreshes a heartbeat every `HeartbeatInterval` (default 30 s).
2. A background watcher looks for `Processing` jobs whose heartbeat is older than `HeartbeatTimeout` (default 5 min) and requeues them. It checks once per `HeartbeatTimeout`.
3. A requeued job goes back to `Enqueued`, **unless it already used all its attempts**: then it becomes `Failed` with the message `Orphaned execution exceeded maximum attempts.` (this is the PostgreSQL statement; the storage contract tests in `StorageProviderTestsBase` cover requeueing for every provider).

Things to know:
- **Detection is not instant.** With the defaults, a crashed job is picked up between 5 and about 10 minutes later (the heartbeat must go stale, and the watcher only runs every 5 minutes). Lower `HeartbeatTimeout` for faster recovery, but keep it **higher than your longest job**, or healthy long jobs may be requeued.
- **A crash consumes an attempt**, because `attempts` is incremented when the job is claimed (shown for PostgreSQL).
- **The job may have partly run before the crash**, so make it idempotent. See [Can a job run more than once?](#can-a-job-run-more-than-once).

**Verified in:** `JobExecutor.cs` (shutdown branch that decrements `Attempts`; `RunHeartbeatAsync`), `OrphanedJobWatcherService.cs`, `PostgresStorageProvider.cs` (`RequeueOrphanedJobsAsync`), `NexJobOptions.cs` (`HeartbeatInterval`, `HeartbeatTimeout`, `ShutdownTimeout`). Tests: `JobCancelledByShutdown_AttemptNotConsumed_NoDeadLetter`, `OrphanedJob_ExpiredHeartbeat_IsReenqueued`.

---

### Won't a high job volume bloat my database?

Not if retention is on, and it is on by default. A background service purges finished jobs on a schedule:

| Status | Default retention |
|---|---|
| Succeeded | 7 days |
| Failed | 30 days |
| Expired | 7 days |
| Dead-letter | 60 days |

It runs every `RetentionInterval` (1 h), deleting in batches of up to `RetentionBatchSize` (1000). Setting a retention to `TimeSpan.Zero` disables purging for that status.

Sizing rule of thumb: the table holds roughly *(jobs per day) × (days of retention)* rows. If that is too much, lower the retention or use the per-job attribute:

```csharp
[Retention(PurgeOnSuccess = true)]        // delete the job as soon as it succeeds
public sealed class NoisyJob : IJob { /* ... */ }

[Retention(TrimPayloadOnSuccess = true)]  // keep the row, wipe the input payload
public sealed class BigPayloadJob : IJob<BigInput> { /* ... */ }
```

Also capped: execution logs are limited to `MaxJobLogLines` (200) per job.

**Verified in:** `NexJobOptions.cs` (`RetentionSucceeded`, `RetentionFailed`, `RetentionExpired`, `RetentionDeadLetter`, `RetentionInterval`, `RetentionBatchSize`, `MaxJobLogLines`), `JobRetentionService.cs`, `RetentionAttribute.cs` (`PurgeOnSuccess`, `TrimPayloadOnSuccess`). Tests: `JobRetentionServiceTests`, `JobRetentionDeadLetterAndBatchingTests`.

---

## Reliability, failures and operation

### What happens when a job fails? Does the error stop the system?

No. The exception is caught inside the executor for that one job. The worker slot is released and the next job runs normally.

What happens to the failed job:
1. The retry policy decides. The job is retried **while `Attempts < MaxAttempts`**. The global default is `MaxAttempts = 10`. **It counts total executions, not retries**: `MaxAttempts = 3` means the job runs at most 3 times.
2. The default delay before the next try is `attempt⁴ + 15 + random(0..29) × (attempt + 1)` seconds. That is roughly 16 to 74 s before the 2nd try, 31 to 118 s before the 3rd, and about 1.8 h before the 10th. You can replace it with `RetryDelayFactory` (code only, not `appsettings.json`).
3. `[Retry(n)]` on the job class overrides the attempt limit (and, with `InitialDelay`, the delay). It uses the same comparison, so `[Retry(3)]` also means 3 executions in total.
4. When attempts run out, the job becomes `Failed` and its `IDeadLetterHandler<TJob>` is called, if you registered one.

Guarantees worth knowing:
- **The dead-letter handler cannot crash the dispatcher.** If it throws, the error is logged and swallowed.
- **A committed success is never turned into a failure.** The result is stored outside the job's `try/catch`; if storing it fails, the job stays `Processing` and the orphan watcher re-runs it (see [Can a job run more than once?](#can-a-job-run-more-than-once)).

**Verified in:** `JobExecutor.cs` (catch blocks, `HandleFailureAsync`), `DefaultJobRetryPolicy.cs` (`ComputeRetryAt`), `NexJobOptions.cs` (`MaxAttempts`, `RetryDelayFactory`), `RetryAttribute.cs`, `DefaultDeadLetterDispatcher.cs` (handler errors swallowed). Tests: `FailedJob_SchedulesRetry_WhenAttemptsRemaining`, `FailedJob_NoRetry_WhenMaxAttemptsExhausted`, `ComputeRetryAt_MaxAttemptsReached_ReturnsNull`.

---

### What is the per-queue circuit breaker and when should I use it?

It protects a **queue** whose jobs all depend on the same external service (payment gateway, ERP, carrier). When that service is down, every job fails and burns its retries, and you hammer a service that is trying to recover. The breaker **stops fetching from that queue** after consecutive failures, then lets jobs back in gradually.

The states:

```
Closed ──(N consecutive failures)──► Open ──(OpenDuration)──► HalfOpen
   ▲                                                              │
   │                                        one canary job runs ──┤
   │                                                              ▼
   └────────────(RecoveryDuration)──── Recovering ◄──(canary ok)──┘
                                   (limited concurrency)
```

- **Open:** the dispatcher skips the queue entirely. Jobs stay `Enqueued`. They are **not failed and lose no attempts**.
- **HalfOpen:** exactly one job (a canary) may run.
- **Recovering:** only `RecoveryConcurrency` jobs run at once, so the recovered service is not flooded.
- If the canary fails, the queue opens again with a longer wait (`BackoffMultiplier`, capped by `MaxOpenDuration`).

Defaults: `ConsecutiveFailuresThreshold = 5`, `OpenDuration = 1 min`, `BackoffMultiplier = 2.0`, `MaxOpenDuration = 15 min`, `RecoveryConcurrency = 2`, `RecoveryDuration = 2 min`.

```csharp
options.ConfigureQueue("payments", queue =>
{
    queue.EnableCircuitBreaker(cb =>
    {
        cb.ConsecutiveFailuresThreshold = 5;
        cb.BreakOnTransientHttpErrors();   // trips on 5xx, timeouts, 429 and (by default) 401; ignores 400/403/404/422
    });
});
```

Two things that surprise people:
- **With no `BreakOn...` configured, every exception counts** toward the threshold, including your own bugs. Configure what should trip it.
- **The circuit state is kept in memory, per process.** With several nodes, each node counts its own failures and opens and closes on its own. It is not shared through storage.

**Use it** for queues that call one fragile external dependency. **Skip it** for queues of independent work, where one failing job says nothing about the next.

**Verified in:** `DefaultQueueCircuitBreakerManager.cs` (state machine; `ConcurrentDictionary` of circuits), `JobDispatcherService.cs` (queue skipped when `Open`), `QueueCircuitBreakerOptions.cs` (defaults; `IsEligible` returns `true` when no predicate is set), `QueueCircuitBreakerFilter.cs`. Tests: `QueueCircuitBreakerTests`.

---

### Retry, dead-letter, throttle or circuit breaker: which one do I need?

They answer different questions, and they are meant to be combined.

| Mechanism | Question it answers | Scope | When it acts |
|---|---|---|---|
| **Retry** | This run failed. Should I try again? | one job | after a failure |
| **Dead-letter** | I am out of attempts. What now? | one job | after the last failure |
| **Throttle** | How many jobs may use this resource at once? | one resource, any queue | before execution |
| **Circuit breaker** | Is the destination down? Should I stop sending? | a whole queue | after repeated failures |

Quick rules:
- One job fails, others are fine → **retry**.
- Every job in the queue fails at once → **circuit breaker**.
- The destination accepts only N calls at a time → **throttle**.
- Retries are exhausted and the job still matters → **dead-letter handler** (alert, compensate, park for review).

A payment gateway, end to end: `[Throttle("payment-gateway", maxConcurrent: 5)]` keeps you from saturating it, retry absorbs the isolated error, the breaker on the `payments` queue stops the flood if the gateway goes down, and the dead-letter handler deals with what is left.

**Throttle details that matter:**
- A throttled job **waits while already `Processing`**, holding its worker slot (and its heartbeat keeps running). It does not go back to the queue. If many jobs wait for one resource, they can occupy all your workers and starve other queues, so keep `Workers` comfortably above the sum of the relevant `maxConcurrent` values or isolate throttled jobs in their own queue (see [Throttling](07-Throttling.md#waiting-for-a-slot)).
- By default the limit is **per process** (an in-memory semaphore per resource). For a limit shared by all nodes, register the Redis store with `AddNexJobDistributedThrottle()`. If that store fails, NexJob degrades to the local throttle and logs a warning.

**Verified in:** `JobExecutor.cs` (`ExecuteWithThrottlingAndFiltersAsync`: waits for slots after the heartbeat has started), `ThrottleRegistry.cs` (`SemaphoreSlim` per resource; degradation to local), `NexJob.Redis/NexJobRedisExtensions.cs` (`AddNexJobDistributedThrottle`), plus the sources cited in the two answers above.

---

### Can a job run more than once?

**Yes. NexJob gives you at-least-once execution, not exactly-once.** A job can run again when:
- its node crashed mid-run and the orphan watcher requeued it (the first run may have done part of the work);
- it succeeded but the result could not be stored after several tries. The executor logs that the job *stays Processing and will be re-run by the orphan watcher*;
- a retry follows a failure that happened after a side effect (for example, the email was sent, then the save failed).

So write jobs to be **idempotent**: use a natural key, check before you write, or use the idempotency key when you enqueue. See [Idempotency](17-Idempotency.md).

**Verified in:** `JobExecutor.cs` (`CommitSuccessAsync` log message), `OrphanedJobWatcherService.cs`, `PostgresStorageProvider.cs` (`RequeueOrphanedJobsAsync`).

---

### Why was my job marked Expired instead of running?

You enqueued it with a deadline (`deadlineAfter`) and the deadline had passed by the time a worker picked it up. The deadline is checked **before** the job starts. If `UtcNow` is past `ExpiresAt`, the job is marked `Expired`, the counter `nexjob.jobs.expired` is incremented, and the job is **never executed**. A deadline does not interrupt a job that is already running.

Common causes: the queue was paused, the circuit breaker was open, all workers were busy, or the node was down past the deadline. Jobs wait in `Enqueued` while that happens, and the clock keeps running.

**Verified in:** `JobExecutor.cs` (`TryHandleExpirationAsync`), `NexJobMetrics.cs` (`nexjob.jobs.expired`).

---

### Can several microservices share the same NexJob database?

Yes, as long as each service only takes the jobs it can run. Two mechanisms make that safe:

1. **Queues per service.** A host polls only the queues in `NexJobOptions.Queues` (default `["default"]`). Give each service its own queue names and they never fetch each other's jobs. The fetch statement filters by `queue = ANY(@queues)`.
2. **Foreign jobs are deferred, not failed.** If a service fetches a job whose job type or input type it cannot load, it does **not** count the attempt and does **not** dead-letter the job. It releases the job back with a delay (`ForeignJobRetryDelay`, default 5 s) so the owning service can take it.

The catch: if two services poll the **same queue**, they keep bouncing each other's jobs. Nothing breaks, but it wastes fetches and can delay the real owner. Separate queues avoid it.

**Verified in:** `NexJobOptions.cs` (`Queues`, `ForeignJobRetryDelay`), `JobExecutor.cs` (`ForeignJobTypeException` branch: attempt decremented, `RetryAt` set, no dead-letter), `PostgresStorageProvider.cs` (`queue = ANY(@queues)`). Tests: `PrepareAsync_InvalidJobType_ThrowsForeignJobTypeException`, `PrepareAsync_InvalidInputType_ThrowsForeignJobTypeException`.

---

## Integration and observability

### Do I need a dedicated ASP.NET Core server just for the dashboard?

No. There are two ways to host it:

| Your app | Package | Setup |
|---|---|---|
| Already an ASP.NET Core app | `NexJob.Dashboard` | `app.UseNexJobDashboard()` |
| Worker Service or console app | `NexJob.Dashboard.Standalone` | `services.AddNexJobStandaloneDashboard(...)` |

The standalone package starts a small embedded web server **inside your worker process** (port `5005` by default, path `/dashboard`). You do not deploy anything extra.

If you want a dashboard-only process with no job execution, set `DisableWorkers` in the standalone options. It sets `Workers = 0` for that host, so it serves the UI and runs no jobs.

**Security, read this before exposing it:**
- In standalone mode the dashboard listens on **loopback only by default** (`LocalhostOnly = true`). In a container that makes it unreachable through a published port; there, set `LocalhostOnly = false` and register an `IDashboardAuthorizationHandler` (or restrict the port at the network level). With `LocalhostOnly = false` and no handler, NexJob logs a warning at startup.
- A registered `IDashboardAuthorizationHandler` **is enforced in standalone mode**, but the embedded server has no authentication middleware, so `context.User` is never authenticated: the handler must authenticate from the request itself (see [Dashboard](10-Dashboard.md#authorization-in-the-standalone-dashboard)).

**Verified in:** `StandaloneDashboardHostedService.cs` (builds a `WebApplication`; `UseUrls` with `localhost` or `0.0.0.0`; `DisableWorkers` sets `Workers = 0`; forwards the parent host's `IDashboardAuthorizationHandler` through `RootScopedDashboardAuthorizationHandler`), `StandaloneDashboardOptions.cs` (`Port = 5005`, `Path`, `LocalhostOnly = true`).

---

### How do OpenTelemetry tracing and logging work?

**Tracing.** The trace context travels with the job:
1. When you enqueue, NexJob stores the current W3C `traceparent` (`Activity.Current?.Id`) in the job record. Broker triggers store the one from the message headers.
2. When the job runs, the executor starts an activity named after the job and **continues that trace** from the stored `traceparent`. The enqueue and the execution end up in the same trace even across processes.
3. The activity carries tags such as `nexjob.attempt`, and failures add an `exception` event with type, message and stack trace.

Register the source and the meter with the OpenTelemetry SDK. Both are named `NexJob` (`NexJobActivitySource.Name`, `NexJobMetrics.MeterName`). The `NexJob.OpenTelemetry` package wires this up; see [OpenTelemetry](12-OpenTelemetry.md).

**Metrics** (meter `NexJob`): counters `nexjob.jobs.enqueued`, `nexjob.jobs.succeeded`, `nexjob.jobs.failed`, `nexjob.jobs.expired`; histogram `nexjob.job.duration` (ms); gauges `nexjob.queue.depth`, `nexjob.workers.active`, `nexjob.workers.total`.

**Logging.** Every log line written while a job runs, yours and NexJob's, inherits a structured scope with `NexJob.JobId`, `NexJob.JobType`, `NexJob.Queue`, `NexJob.Attempt` and `NexJob.TraceParent` (empty string if there was none). You can filter and correlate in Loki, Splunk, Datadog or Elastic without adding anything.

**Verified in:** `JobExecutor.cs` (`BeginScope` keys; `NexJobActivitySource.StartExecute(..., job.TraceParent)`; exception event), `JobRecordFactory.cs` (`Activity.Current?.Id`), `NexJobActivitySource.cs`, `NexJobMetrics.cs`.

---

### What does a broker trigger do with a message body that is not JSON?

Since v5.6.1, the job receives the body **exactly as text**. All five broker triggers (SQS, Service Bus, Pub/Sub, Kafka, RabbitMQ) bind the message to an `IJob<string>`; the body can be JSON, XML, CSV or plain text, and **you** parse it inside the job.

Before v5.6.1 this was broken: the body was stored raw but read back as a JSON string, so any body that was not itself a JSON string literal (plain text, XML, even a JSON object) failed the job, and on PostgreSQL a non-JSON body was rejected by the `jsonb` column at enqueue. See the [Migration](18-Migration.md) note if you worked around it by publishing quoted JSON string literals.

Note this is about what the **trigger** receives. The Kafka *producer* (`EnqueueKafkaAsync` / `EnqueueKafkaRawAsync`) can publish any string or bytes; it does not require JSON either.

**Verified in:** the five `*TriggerHandler.cs` files (body passed through `JsonSerializer.Serialize`), `DefaultJobInvokerFactory.cs` (input deserialized as `string`). Tests: `*TriggerRawBodyTests` in each trigger test project.
