---
title: "NexJob Changelog: Release Notes and Breaking Changes"
sidebarTitle: "Changelog"
description: "A history of NexJob releases with key features, bug fixes, and breaking changes for each version from v0.4.0 through the current release."
---

NexJob follows [Semantic Versioning](https://semver.org/). Breaking changes increment the major version. This page summarizes every public release from newest to oldest.

## v5.9.0

**Added**
- `[ExecutionTimeout("00:05:00")]` per job type and an opt-in `NexJobOptions.DefaultExecutionTimeout`. At the limit the job's `CancellationToken` is cancelled and the run fails with a `TimeoutException` through the normal retry and dead-letter path. Cancellation is cooperative.
- A warning and the `nexjob.jobs.cancellation_ignored` counter for a job still running 10 seconds after its timeout cancelled its token.
- `EnqueueAsync` and `ScheduleAsync` overloads that take `maxAttempts`: call site, then `[Retry]`, then `NexJobOptions.MaxAttempts`.
- `NexJobOptions.IgnoreRetryAttemptExceptions` and `[Retry(IgnoreRetryAttemptExceptions = ...)]`: a listed exception sends the job straight to dead-letter.
- `ExecutionWindowSettings.DaysOfWeek`, and each queue's window shown on the dashboard Settings page.
- Dashboard playground scenarios (`EnablePlayground`, off by default) and a configurable `DefaultTheme`.

**Changed**
- For a class with `[Retry(n)]`, the stored `MaxAttempts` and `IJobContext.MaxAttempts` now report `n` instead of the global default. `[Retry(0)]` is stored as `1`.
- `IScheduler` has four new overloads. Code that only calls it is unaffected; a custom implementation must add them. See the [migration guide](migration.md).

**Fixed**
- Dashboard job detail shows the real retry budget, and the execution timeline reads correctly when attempts are exhausted.

---

## v5.8.0

**Added**
- `IDeadLetterForwarder`, called for every registered forwarder after the typed `IDeadLetterHandler<TJob>`, with built-in forwarding of exhausted jobs for Kafka (`ExhaustedJobsTopic`) and RabbitMQ (`ExhaustedJobsRoutingKey`).
- An alerts guide, and new pages for the circuit breaker, execution windows, runtime control and delivery guarantees. The documentation is organised in categories.
- Dashboard 24-hour hourly throughput chart with 24 evenly spaced bars, dynamic average threshold line, and anomaly drop warnings.
- Clean server node ID formatting: composite IDs (`{Host}:{PID}:{Guid}`) now display as `{Host}:{PID} #{shortGuid}` in the dashboard, with the full ID in tooltips.
- In-memory host CPU and RAM radial gauges on the Overview and Servers views (no database persistence).

**Changed**
- `deadlineAfter` is stored and enforced on every database provider: a job fetched after its deadline is marked `Expired` and does not run.
- A throttled job that cannot get its slot within about 5 seconds goes back to the queue without using an attempt.
- A job interrupted by shutdown, or deferred because its type is not available in the process, no longer consumes an attempt on database providers.
- A job whose attempts are used up because the node running it died now calls its dead-letter handler and forwarders with an `OrphanedJobException`.

**Fixed**
- Pub/Sub trigger: `EmulatorHost` works, and a message without a job type is no longer redelivered in a tight loop.
- PostgreSQL: `AddNexJobPostgres(NpgsqlDataSource)` now creates the schema.
- SQL Server: an already open `SqlConnection` is refused with a clear message.
- Throughput chart no longer renders a single full-width bar when only one hour has activity.
- Clipboard copy now writes plain text in all terminal windows and log modals, preventing HTML tag leakage on paste.

---

## v5.7.0

**Security**
- Standalone dashboard `LocalhostOnly` now defaults to `true` (loopback only). Previously the default was `false`, exposing job payloads and actions to anyone who could reach the port. A registered `IDashboardAuthorizationHandler` is now enforced in standalone mode (previously silently ignored).

**Added**
- Documentation for forwarding dead-lettered jobs to Kafka or RabbitMQ via an `IDeadLetterHandler<>`.

**Changed**
- PostgreSQL: `AddNexJobPostgres(connectionString)` now shares a single connection pool with the runtime settings store (was two pools). Pool size and worker count are logged at startup; a warning is shown when the pool is smaller than `Workers`.

**Fixed**
- Redis: the job index and Succeeded/Failed sets are now reconciled every hour under a distributed lock. Rolling upgrades no longer require manual cleanup steps.
- SQL Server: continuation-release statements now use an index hint on `idx_nexjob_jobs_parent`, eliminating deadlock victims (error 1205) under concurrent workers.
- PostgreSQL: `AddNexJobPostgres(NpgsqlDataSource)` no longer crashes on password-protected databases.
- Dashboard `Workers` control removed; `QueueSettings[].Workers` and a non-default `DefaultQueue` now log a startup warning instead of silently doing nothing.

---

## v5.6.2

**Fixed**
- Dashboard empty-state pages no longer log SVG path errors in the browser.
- Standalone dashboard now logs a startup warning when `LocalhostOnly = false` and no authorization handler is registered.

**Changed**
- Throttle documentation updated to reflect that a throttled job waits in `Processing` and holds its worker slot.

**Added**
- New FAQ page covering 12 common questions with source-verified answers.

---

## v5.6.1

**Fixed**
- Broker triggers (SQS, Service Bus, Pub/Sub, Kafka, RabbitMQ): message body now reaches `IJob<string>` verbatim. Previously, any body that was not a JSON string literal failed with `JsonException`; on PostgreSQL, non-JSON bodies were rejected at enqueue.

---

## v5.6.0

**Added**
- **Progress checkpoints** (`IJobContext.SaveCheckpointAsync`): long-running jobs can save intermediate state that survives retries and crashes. Checkpoint JSON is preserved across retry attempts and cleared automatically on success.
- **Queue circuit breaker** (`ConfigureQueue` + `EnableCircuitBreaker`): 4-state lifecycle (Closed → Open → HalfOpen → Recovering) with selective exception filtering (`BreakOnTransientHttpErrors()`) and exponential backoff.
- **`[Retention]` attribute**: `PurgeOnSuccess` deletes the job row immediately on success; `TrimPayloadOnSuccess` wipes the input payload while preserving the row.
- **Structured logging scope**: every log line emitted during job execution inherits `NexJob.JobId`, `NexJob.JobType`, `NexJob.Queue`, `NexJob.Attempt`, and `NexJob.TraceParent`.
- **Job catalog** (`/catalog` page): interactive table of all registered job types with aggregated execution telemetry, inline trigger modal for parameterized jobs, and on-demand job triggering.
- **Multi-cluster federation** (`DashboardOptions.Clusters`): manage multiple NexJob deployments from a single dashboard.
- **Ops-host mode** (`DisableWorkers = true`): run a standalone dashboard host that processes no jobs.
- **Queue scoping** (`DashboardOptions.Queues`, `StandaloneDashboardOptions.Queues`): scope a dashboard instance to a subset of queues.

**Changed**
- MongoDB: `DateTimeOffset` serializer is no longer registered globally — applied only to NexJob's own documents.
- Recurring jobs from `appsettings.json`: first run is now the next cron occurrence (no longer immediate on first registration).
- Batch acknowledgment (`EnableBatchAcknowledgment`) now correctly releases `ContinueWith` continuations.
- Graceful shutdown: dispatcher stops fetching immediately on shutdown; interrupted jobs are requeued without consuming an attempt and are never dead-lettered.

**Fixed**
- Redis: due scheduled and retry jobs are promoted atomically via Lua script (no more double-execution on concurrent nodes).
- Redis: distributed throttle slots use per-job holders with TTL; crashed-node slots reclaimed after `3 × HeartbeatInterval`.
- Redis: job index built as a sorted set; dashboard queries and retention no longer scan the full keyspace.
- Redis: idempotency key lifetime tied to the job's retention period (no longer fixed TTL).
- PostgreSQL, SQL Server, MongoDB: `DuplicatePolicy.AllowAfterFailed` now correctly allows re-enqueue after any terminal state.
- SQL Server, MongoDB: foreign-key/shared-queue job handling correctly defers without counting an attempt.

---

## v5.5.0

**Added**
- **Batch fetching**: the dispatcher fetches as many jobs as free worker slots allow in one roundtrip.
- **Batch acknowledgment** (`EnableBatchAcknowledgment`): implemented for all five storage providers. Verified 10–15× throughput increase on SQL Server (150,000-job load test with Kafka).
- SQL Server: non-blocking application lock on scheduled-job promotion prevents contention under parallel workers.

---

## v5.4.1

**Added**
- All external trigger packages (Salesforce, SalesforceStreaming, Azure Service Bus, AWS SQS, Google Pub/Sub) now report connection state (`Starting`, `Listening`, `Reconnecting`, `Faulted`, `Stopped`) to the dashboard `/listeners` page.

---

## v5.4.0

**Added**
- **Enterprise dashboard layout**: collapsible sidebar, `Ctrl+K` global search, live cluster health badge, and 5 themes persisted in browser storage.
- **Cluster Pipeline Topology Map**: interactive flowchart connecting triggers → queue buffers → workers with live activity pulses.
- **Real-time SSE log streaming** on the job detail page.
- **`/listeners` page**: real-time broker connection status for all registered triggers.
- Kafka custom `ConsumerConfig` and `ProducerConfig` delegates for SASL/SSL and advanced tuning.

---

## v5.3.0 — v5.2.0

**Added (v5.3.0)**
- Kafka custom security configuration: `ConfigureConsumer` and `ConfigureProducer` delegates for SASL/SSL credentials, raw PEM strings, and custom client tuning.

**Added (v5.2.0)**
- `RetentionDeadLetter` option (default 60 days) and `RetentionBatchSize` (default 1,000 rows) with batched deletion in all storage providers.
- Consumer-driven trigger job mapping: subscribe to a broker topic without requiring publishers to set `nexjob.job_type` headers. Use `.AddKafkaTrigger<TJob>()` and similar generic overloads.
- OpenTelemetry gauge instruments: `nexjob.queue.depth`, `nexjob.workers.active`, `nexjob.workers.total`.
- Comprehensive sample projects for all storage topologies, broker integrations, and cloud triggers.
- `AddNexJobPostgres(NpgsqlDataSource)` overload for reusing an application-managed connection pool.

---

## v5.1.0

**Added**
- `NexJob.Trigger.SalesforceStreaming`: CometD/Bayeux long-polling trigger for PushTopics, CDC events, Platform Events, and Generic Streaming channels. Includes Replay ID checkpointing, automatic session recovery, and OAuth 2.0 support.

---

## v5.0.0

**Added**
- `NexJob.Trigger.Salesforce`: Pub/Sub API trigger for CDC and Platform Events over gRPC. Apache Avro schema caching, Replay ID checkpointing, and OAuth2 client credentials flow.
- `NexJob.RabbitMQ` (unified package): resilient outbox producer with Publisher Confirms + consumer trigger. `EnqueueRabbitMqAsync` and `EnqueueRabbitMqRawAsync` scheduling extensions.
- `NexJob.Kafka` (unified package): resilient outbox producer with retries + consumer trigger. `EnqueueKafkaAsync` and `EnqueueKafkaRawAsync` scheduling extensions.

**Changed** *(breaking)*
- Packages renamed: `NexJob.Trigger.RabbitMQ` → `NexJob.RabbitMQ`; `NexJob.Trigger.Kafka` → `NexJob.Kafka`.

---

## v4.0.0 — v4.0.1

**Fixed**
- `MigrationPipeline` now throws `InvalidOperationException` on incomplete migration chains instead of silently returning partial results.
- Recurring job scheduler acquires a distributed lock before enqueuing due jobs — prevents duplicate firings on multi-instance deployments.
- Job dispatcher adds a back-off on error to prevent hot polling loops when storage is unavailable.

**Security (v4.0.1)**
- Resolved transitive vulnerabilities in `NexJob.Trigger.AzureServiceBus`, `NexJob.MongoDB`, and integration test packages.

---

## v3.0.0

**Breaking changes**
- `AddNexJob` returns `NexJobBuilder` instead of `IServiceCollection`. Use `.Services` to chain non-NexJob extensions.
- `IStorageProvider` split into `IJobStorage`, `IRecurringStorage`, and `IDashboardStorage`. Custom provider implementors must register all four DI types.

**Added**
- `IJobControlService`: programmatic requeue, delete, and pause from application code.
- `UseDashboardReadReplica(connectionString)`: route read-heavy dashboard queries to a read replica.
- `AddNexJobDistributedThrottle()` (`NexJob.Redis`): global Redis-backed throttle enforcement shared across nodes.
- `NexJobOptions.DistributedThrottleTtl`: configurable distributed throttle slot TTL.
- Major dashboard redesign with command-center overview, bulk operations, and high-density job views.

---

## v2.0.0

**Added**
- Broker trigger packages: `NexJob.Trigger.AzureServiceBus`, `NexJob.Trigger.AwsSqs`, `NexJob.Trigger.RabbitMQ`, `NexJob.Trigger.Kafka`, `NexJob.Trigger.GooglePubSub`.
- `NexJob.OpenTelemetry`: opt-in OTel SDK integration for tracing and metrics.
- `DashboardOptions.MetricsCacheTtl`: configurable dashboard metrics cache TTL (default 3 s).

**Fixed**
- Redis `EnqueueAsync` idempotency check is now atomic via Lua script.
- MongoDB `EnqueueAsync` uses a partial unique index for idempotency key uniqueness.

---

## v1.0.0

- Public API frozen. Breaking changes now require a major version bump.
- Complete documentation published.
- `DuplicatePolicy` concurrency tests added across all storage providers.

---

## v0.8.0

**Added**
- `IJobExecutionFilter`: middleware pipeline for cross-cutting job execution concerns (logging, audit, metrics).
- `IDashboardAuthorizationHandler`: pluggable dashboard authorization replacing the removed `RequireAuth` flag.
- `ContinueWithAsync<TJob>`: no-input overload for chaining `IJob` continuations.
- Persistent `IRuntimeSettingsStore` for all storage providers — dashboard overrides survive restarts.
- `JobRetentionService`: automatic purge of `Succeeded`, `Failed`, and `Expired` jobs on configurable TTLs.

**Changed** *(breaking)*
- `DashboardOptions.RequireAuth` removed. Replaced by `IDashboardAuthorizationHandler`.

---

## v0.7.0

**Added**
- `DuplicatePolicy` enum (`AllowAfterFailed`, `RejectIfFailed`, `RejectAlways`) on `EnqueueAsync`.
- Atomic `CommitJobResultAsync` on `IStorageProvider`: single transactional commit of all execution outcome mutations.

**Fixed**
- Transaction leak in `EnqueueAsync` (PostgreSQL, SQL Server): early-return paths now explicitly roll back.

---

## v0.6.0

**Added**
- Distributed reliability tests against real storage providers.

**Changed** *(breaking)*
- `AddNexJob()` now defaults to InMemory storage (previously required explicit provider configuration).
- `EnqueueAsync` returns `JobId` instead of `EnqueueResult`.
- `DuplicatePolicy` default changed to `AllowAfterFailed`.
- Recurring jobs configuration redesigned: use simple class name in `Job` field instead of assembly-qualified type string; `Id` is now optional.

---

## v0.4.x — v0.5.0

**Breaking changes (v0.5.0)**
- `IJob<T>.ExecuteAsync` signature changed: `input` is now the first parameter, before `cancellationToken`.
- `RecurringJobSettings` moved to a separate collection configured via `AddRecurringJob` methods.
- `IScheduler.ContinueWithAsync` returns `JobId` for the child job.
- `IJobContext.Progress` replaced by `ReportProgressAsync`.
