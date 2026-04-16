# Changelog

All notable changes to NexJob are documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

### Changed

### Fixed

## [3.0.0] - 2026-04-15

### ⚠️ Reliability Hardening Phase (In Progress)
The project has entered an official **Reliability Lock**. Development is focused exclusively on achieving 100% unit test coverage (line/branch) for all core components.
- **Project Status:** Locked for new features.
- **Guarantee:** No v3.0.0 final release until every core component is verified with an exhaustive Testing Matrix.
- **Verification:** Mandatory 80% coverage floor enforced via CI, targeting 100% for critical paths.

### Breaking Changes

- `AddNexJob` now returns `NexJobBuilder` instead of `IServiceCollection`.
  Use `.Services` to chain non-NexJob extensions.
  See docs/wiki/migration-v2-to-v3.md.

- `IStorageProvider` split into `IJobStorage`, `IRecurringStorage`, and
  `IDashboardStorage`. `IStorageProvider` is the composed interface.
  No change required for standard (built-in provider) usage.
  Custom storage provider implementors must register all 4 DI types.

### Added

- `IJobInvokerFactory` / `DefaultJobInvokerFactory` — encapsulates type resolution, payload migration, DI scope creation, and compiled invoker cache. Extracted from `JobExecutor` for testability.
- `IJobRetryPolicy` / `DefaultJobRetryPolicy` — encapsulates retry delay calculation. Extracted from `JobExecutor.HandleFailureAsync`. Pure function: testable in isolation.
- `IDeadLetterDispatcher` / `DefaultDeadLetterDispatcher` — encapsulates dead-letter handler resolution and invocation. Extracted from `JobExecutor`. Removes reflection from the hot path.
- `IJobStorage` — hot-path storage contract for execution and worker coordination
- `IRecurringStorage` — recurring job scheduling contract
- `IDashboardStorage` — read-heavy dashboard query contract (safe for read replicas)
- `NexJobBuilder` — fluent builder returned by `AddNexJob`
- `IJobControlService` — programmatic requeue, delete, and pause from application code
- `UseDashboardReadReplica(connectionString)` — read replica routing for PostgreSQL and SQL Server
- `IDistributedThrottleStore` — opt-in interface for global throttle enforcement
- `RedisDistributedThrottleStore` — Redis-backed global `[ThrottleAttribute]` limits
- `UseDistributedThrottle()` — opt-in extension to enable Redis-backed throttling
- `NexJobOptions.DistributedThrottleTtl` — configurable slot TTL for distributed throttle (default: 1h)
- `JobExecutor` — extracted execution pipeline from `JobDispatcherService`
- `NexJob.Dashboard` — massive UI/UX refactor featuring:
    - **Premium Ki-ADMIN Aesthetic**: High-fidelity charcoal navy theme with glowing status indicators and emerald teal accents. Adaptive Light mode with off-white backgrounds to reduce eye strain.
    - **Command Center (NOC) Overview**: Completely remodeled dashboard landing page with real-time throughput charts, recent job activity across all statuses, server fleet health, and queue distribution visualizations.
    - **High-Density Data Views**: Optimized Jobs and Recurring pages with single-line horizontal layouts, allowing monitoring of large job volumes without excessive scrolling.
    - **Bulk Operations**: Support for multi-job selection with Requeue and Delete actions via a new floating bulk toolbar and JSON API.
    - **Enhanced Discovery**: Integrated Breadcrumbs for navigation context and optimized native storage filters for Queue and Status, significantly improving performance on large datasets.
    - **Zero-Dependency Engineering**: All visual enhancements implemented using pure C#, modern CSS, and lightweight JS (SSE/Polling), maintaining a minimal footprint.

### Fixed

- `CommitJobResultAsync` dead-letter path now explicitly clears `RetryAt` across all 5 storage providers (InMemory, PostgreSQL, SQL Server, Redis, MongoDB). Previously, jobs transitioned to `Failed` but retained the last `RetryAt` value.
- `ThrottleRegistry` now wraps `IDistributedThrottleStore` calls in try-catch. When the distributed store throws, the registry degrades gracefully to local `SemaphoreSlim` throttling instead of propagating the exception.
- Throttle wait replaced busy-loop (`Task.Delay(50)`) with `SemaphoreSlim.WaitAsync`
- Redis throttle TTL now reads from `NexJobOptions.DistributedThrottleTtl` (was hardcoded to 3600s)
- `NexJob.Dashboard` — wired dashboard services for samples to ensure correct UI rendering.

### Infrastructure

- `ci: publish` — updated workflow to include v2 triggers and OpenTelemetry packages in the release pipeline.

### Testing

- Contract test `CommitJobResultAsync_Failure_NoRetry_SetsFailed` now asserts `RetryAt == null` on dead-letter transition across all providers.
- Added `MissingJobType` negative test scenario to RabbitMQ, SQS, Kafka, and AzureServiceBus trigger unit test suites.
- Added `JobControlServiceIntegrationTests` — verifies Pause/Resume/Requeue/Delete against real dispatcher and InMemory storage.
- Added `DistributedThrottleDegradationTests` — verifies graceful fallback to local throttle when distributed store is unavailable.

### Documentation

- wiki/07-Throttling: added distributed throttle section, removed outdated Redis semaphore example
- wiki/09-Storage-Providers: added interface segregation, read replica, and IJobControlService sections
- wiki/11-Configuration-Reference: added DistributedThrottleTtl option
- wiki/18-Migration: added v2→v3 section
- wiki/19-Triggers: added producer examples for all 5 brokers and error handling section
- wiki/migration-v2-to-v3.md: new full migration guide

## [2.0.0] - 2026-04-14

### Added
- `NexJob.Trigger.AzureServiceBus` — trigger package for Azure Service Bus queues and topics
- `NexJob.Trigger.AwsSqs` — trigger package for AWS SQS queues
- `NexJob.Trigger.RabbitMQ` — trigger package for RabbitMQ queues
- `NexJob.Trigger.Kafka` — trigger package for Apache Kafka topics
- `NexJob.Trigger.GooglePubSub` — trigger package for Google Cloud Pub/Sub subscriptions
- `NexJob.OpenTelemetry` — opt-in package exposing NexJob tracing and metrics to the OTel SDK
- `IScheduler.EnqueueAsync(JobRecord, DuplicatePolicy, CancellationToken)` — non-generic overload for broker triggers
- `JobRecordFactory` — internal factory enabling trigger packages to build `JobRecord` instances
- `DashboardOptions.MetricsCacheTtl` — configurable TTL for dashboard metrics cache (default: 3 seconds)
- Testcontainers integration tests for RabbitMQ, AWS SQS, Kafka, and Azure Service Bus triggers

### Fixed
- Redis `EnqueueAsync` idempotency check is now atomic via Lua script — prevents duplicate jobs under concurrent load
- MongoDB `EnqueueAsync` uses a partial unique index (`partialFilterExpression`) to enforce idempotency key uniqueness while allowing multiple jobs without keys (null keys)
- MongoDB `EnqueueAsync` catch block now correctly guards against `DuplicateKey` errors on jobs with idempotency keys during race conditions
- AWS SQS trigger `ServiceCollectionExtensions` uses `TryAddTransient` to respect user-registered `ISqsClient` implementations

### Performance
- Dashboard metrics are now cached with a configurable TTL (default: 3s) to prevent database overload when multiple users have the dashboard open

## [1.0.0] — April 2026

### Added
- **NexJob wiki** — complete documentation in `docs/wiki/` covering all features, best practices, troubleshooting, common scenarios, and migration guides.
- **Concurrency tests for `DuplicatePolicy`** — integration tests validating concurrent enqueue behaviour under `AllowAfterFailed` and `RejectAlways` policies across all storage providers.

### Changed
- **API freeze** — public API is now stable. Breaking changes require a major version bump.
- **Roadmap updated** — v1.0.0 marks production-hardened status.

### Fixed

## [0.8.0] — April 2026

### Added

- **`IJobExecutionFilter`** — middleware pipeline for job execution. Implement and register in DI to add cross-cutting behaviour: logging, tenant injection, audit trails, metrics, circuit breakers. Filters wrap the job execution in registration order. A filter that throws is treated as a job failure — the normal retry and dead-letter flow applies. Filters are resolved from the job's DI scope.
- **`JobExecutingContext`** — context passed to each filter containing the `JobRecord`, `IServiceProvider` (job scope), and the execution outcome (`Succeeded`, `Exception`) set after the pipeline runs.
- **`JobExecutionDelegate`** — delegate type representing the next component in the job execution filter pipeline. Returned from `IJobExecutionFilter.OnExecutingAsync` and invoked by the filter to pass control.
- **`IDashboardAuthorizationHandler`** — pluggable authorization interface for the NexJob dashboard. Implement and register in DI to control access with any strategy: role-based, claims, API key, IP whitelist, or custom logic. No handler registered = open access (suitable for development and internal networks).
- **`ContinueWithAsync<TJob>`** — new no-input overload for chaining `IJob` continuations after a parent job completes.
- **`ThrottleAttribute` documentation** — clarified that concurrency limits are enforced per worker process (local), not cluster-wide.
- **Persistent `IRuntimeSettingsStore`** — all four storage providers (PostgreSQL, SQL Server, Redis, MongoDB) now implement `IRuntimeSettingsStore`, persisting runtime configuration (worker count, polling interval, paused queues, recurring jobs paused) across application restarts. Dashboard overrides no longer require reapplication after each deploy. The in-memory store remains as fallback when no persistent provider is configured.
- **Job Retention — automatic cleanup of terminal jobs** — new `JobRetentionService` periodically purges `Succeeded`, `Failed`, and `Expired` jobs older than configurable retention thresholds. Defaults: Succeeded 7 days, Failed 30 days, Expired 7 days. Thresholds are configurable via `NexJobOptions` (code/appsettings) and overridable at runtime through the dashboard Settings page without restart. Setting a threshold to zero disables purging for that status. `RetentionPolicy` type and `IStorageProvider.PurgeJobsAsync` implemented in all five storage providers.

### Changed

- **`DashboardOptions.RequireAuth` removed** — replaced by `IDashboardAuthorizationHandler`. The boolean flag only supported ASP.NET Core authentication; the new interface supports any authorization strategy.

### Fixed

## [0.7.0] — April 2026

### Added
- **NexJob.Oracle removed** — stub project with no implementation removed from the solution. Oracle support may be contributed as a community provider in the future.
- **`DuplicatePolicy` — idempotency key duplicate control** — new enum (`AllowAfterFailed`, `RejectIfFailed`, `RejectAlways`) controls what happens when a job with the same `idempotencyKey` already exists in a terminal failure state. Default is `AllowAfterFailed` (at-least-once semantics). `RejectAlways` guarantees exactly-once across the full job lifetime.
- **`EnqueueResult`** — rich return type from `IStorageProvider.EnqueueAsync` containing `JobId` and `WasRejected` flag.
- **`DuplicateJobException`** — thrown by `IScheduler.EnqueueAsync` when enqueue is rejected by `DuplicatePolicy`. Contains `IdempotencyKey`, `ExistingJobId`, and `Policy`.
- **`duplicatePolicy` parameter on `IScheduler.EnqueueAsync`** — optional parameter (default `AllowAfterFailed`) on both overloads, positioned after `idempotencyKey`. `DuplicatePolicy` implemented in all 5 storage providers (InMemory, PostgreSQL, SQL Server, Redis, MongoDB).
- **`CommitJobResultAsync` on `IStorageProvider`** — new atomic commit method that persists all execution outcome mutations (status, logs, continuations, recurring result) as a single transactional unit. Eliminates partial-state risk on process crash during finalisation. Idempotent by contract — safe to call twice on the same terminal job.
- **`JobExecutionResult`** — value type carrying the complete execution outcome passed to `CommitJobResultAsync`. Fields: `Succeeded`, `Logs`, `Exception`, `RetryAt`, `RecurringJobId`.
- **xunit.analyzers v1.16.0** — added to all test projects via `Directory.Build.props`. Catches xUnit-specific mistakes (wrong assertion patterns, `async void` tests, incorrect fixture usage) that generic analyzers miss.

### Changed
- **AI execution system migrated to `ai-method/`** — modular, token-efficient framework replaces monolithic `prompts/` folder. Load only what each task needs (200–3000 tokens) instead of all-or-nothing (5000–8000 tokens). See `ai-method/README.md` for full documentation.
- **`JobDispatcherService` refactored into named stages** — `ExecuteJobAsync` is now a thin orchestrator delegating to `TryHandleExpirationAsync`, `PrepareInvocationAsync`, `ExecuteWithThrottlingAsync`, `HandleFailureAsync`, and `RecordSuccessMetrics`. Behaviour unchanged; cognitive load and MTTR reduced significantly.
- **Decision logging added to dispatcher** — queues skipped due to pause or execution window, throttle waits, retry scheduling with exact delay and time, and dead-letter transitions are now logged explicitly. Dispatcher decisions are fully reconstructable from `Information`-level logs.
- **NexJob.Oracle removed** — stub project with no implementation removed from the solution. Oracle support may be contributed as a community provider in the future.

### Fixed
- **Transaction leak in `EnqueueAsync` (PostgreSQL, SQL Server)** — early-return paths inside the idempotency transaction block now call `RollbackAsync` explicitly before returning. Previously, the transaction was abandoned on dispose without an explicit rollback, which is not guaranteed to roll back in all driver versions.
- **CI double-trigger removed** — `ci.yml` no longer fires on `push` to `develop`. CI now runs only on `pull_request`, eliminating duplicate runs on every PR push.
- **`release.yml` PAT fix** — tag creation now uses `RELEASE_PAT` secret instead of `GITHUB_TOKEN`. Pushes made with `GITHUB_TOKEN` do not trigger other workflows by GitHub design; switching to PAT ensures `publish.yml` (NuGet) fires automatically on every release tag.

## [0.6.0] — April 2026

### Added
- **Distributed Reliability Tests** — New `NexJob.ReliabilityTests.Distributed` project validates all scenarios against **real storage providers** via Testcontainers (PostgreSQL, SQL Server, Redis, MongoDB). Tests ensure production readiness across all backends.
  - **Retry & Dead-Letter**: Retry execution, handler invocation, exception resilience across providers.
  - **Concurrency**: Duplicate prevention, concurrent enqueue, stress testing.
  - **Crash Recovery**: Job persistence, state consistency after node restart.
  - **Deadline Enforcement**: Expiration handling, deadline evaluated before execution.
  - **Wake-Up Latency**: Signaling efficiency, queue-specific dispatch behavior.

### Changed (Breaking)
- **RecurringJobs configuration redesigned** — simpler, refactor-safe API replaces assembly-qualified type strings:
  - `Job` replaces `JobType` — use simple class name ("CleanupJob"), not assembly-qualified string. Types resolved via DI registry.
  - `Id` is now optional — omit it and NexJob derives it from the job name. Use explicit Id when scheduling the same job multiple times with different inputs/schedules.
  - `Input` replaces `InputJson` + `InputType` — plain JSON object, input type inferred automatically from `IJob<T>` interface.
  - Ambiguous job names (same class name in multiple namespaces) produce a clear startup error listing both types.
  - `JobType`, `InputType`, `InputJson` config fields removed entirely.

  **Before:**
  ```json
  {
    "Id": "my-job",
    "JobType": "MyApp.Jobs.CleanupJob, MyApp",
    "InputType": "MyApp.Jobs.CleanupInput, MyApp",
    "InputJson": "{ \"Target\": \"old-jobs\" }",
    "Cron": "0 2 * * *"
  }
  ```
  **After:**
  ```json
  {
    "Job": "CleanupJob",
    "Input": { "Target": "old-jobs" },
    "Cron": "0 2 * * *"
  }
  ```

### Fixed
- **IJob (no-input) recurring jobs from appsettings.json** — Fixed critical regression where configuration-driven recurring jobs implementing `IJob` (without input parameter) failed with `"Cannot load input type: "` error. The `InputType` is now correctly set to the `NoInput` sentinel type when no input is specified, matching the behavior of code-registered recurring jobs.

### Internal
- Added distributed test filtering commands to `CONTRIBUTING.md` for running individual provider or scenario tests.
- **NexJobJobRegistry** — internal DI singleton tracking all registered job types for configuration-based resolution.
- Added regression test `RecurringJob_AppsettingsNoInput_ExecutesEndToEnd` verifying end-to-end execution of `IJob` recurring jobs loaded from configuration.
- **NexJob.Sample.ConfiguredRecurring** — New WebAPI sample demonstrating configuration-driven recurring jobs with automatic binding from `appsettings.json`.

## [0.5.2] — April 2026 [NOT PUBLISHED]

### Fixed
- **IJob (no-input) recurring jobs from appsettings.json** — Fixed critical regression where configuration-driven recurring jobs implementing `IJob` (without input parameter) failed with `"Cannot load input type: "` error. The `InputType` is now correctly set to the `NoInput` sentinel type when no input is specified, matching the behavior of code-registered recurring jobs.

### Internal
- Added regression test `RecurringJob_AppsettingsNoInput_ExecutesEndToEnd` verifying end-to-end execution of `IJob` recurring jobs loaded from configuration.
- **NexJob.Sample.ConfiguredRecurring** — New WebAPI sample demonstrating configuration-driven recurring jobs with automatic binding from `appsettings.json`.

## [0.5.1] — April 2026

### Added
- **Automatic RecurringJob binding from `appsettings.json`** — Define recurring jobs directly in configuration without code:
  ```json
  {
    "NexJob": {
      "RecurringJobs": [
        {
          "Id": "daily-email",
          "JobType": "MyApp.Jobs.EmailJob, MyApp",
          "InputType": "MyApp.Jobs.EmailInput, MyApp",
          "InputJson": "{ \"to\": \"admin@example.com\" }",
          "Cron": "0 9 * * *",
          "TimeZoneId": "America/New_York",
          "Queue": "email",
          "Enabled": true
        }
      ]
    }
  }
  ```
  Full validation at startup: type resolution, cron syntax, input deserialization. Invalid jobs are skipped with a logged error; valid jobs register immediately.
- **Dashboard visual timeline** — Execution timeline showing every job's lifecycle: Enqueued, Processing, Succeeded, Failed, Dead-letter, Expired.

### Changed
- **Dashboard rendering refactored** — Separated presentation logic from business logic. Reduced render cycles.
- **Dashboard live updates** — Universal vanilla JS polling engine replaces DOM using `data-refresh` annotations. Updates every 5 seconds without full page reloads.
- **Dashboard authorization** — Added read-only mode. JSON endpoints for programmatic queries.

### Internal
- Refactored integration tests to use `IClassFixture<T>` for Testcontainers — containers reused across test runs.
- Improved database isolation: Postgres and SQL Server now provision separate databases per test.
- Stabilized `HeartbeatServerAsync` test for flaky timing in CI.

## [0.5.0] — March 2026

### Added
- **Wake-up channel** — Local job enqueues trigger immediate dispatcher wake-up instead of waiting for the next polling interval. Non-blocking signal with capacity=1 prevents latency spikes. Polling fallback preserved for distributed scenarios. Integrated into `JobDispatcherService` and `DefaultScheduler`.
- **`deadlineAfter` — jobs expire if not executed in time** — Add deadline constraint to immediate enqueues:
  ```csharp
  await scheduler.EnqueueAsync<PaymentJob, PaymentInput>(
      input,
      deadlineAfter: TimeSpan.FromMinutes(5));
  ```
  Jobs not started within the deadline are marked `Expired` and skipped. New `JobStatus.Expired` terminal state. Deadline checked immediately after fetch, before execution begins. Deadline only applies to immediate enqueues; scheduled/delayed jobs cannot have deadlines.
- **`IDeadLetterHandler<TJob>` — automatic fallback on permanent failure** — Handle jobs that exhaust all retry attempts:
  ```csharp
  public class PaymentDeadLetterHandler : IDeadLetterHandler<PaymentJob>
  {
      public async Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken ct)
          => await _alerts.SendAsync($"Payment failed: {lastException.Message}", ct);
  }

  // Register
  builder.Services.AddTransient<IDeadLetterHandler<PaymentJob>, PaymentDeadLetterHandler>();
  ```
  Handler invoked only after all retries exhausted. Resolved via DI. Handler exceptions are logged and swallowed — never crash the dispatcher. Works for both `IJob` and `IJob<T>`.
- **`nexjob.jobs.expired` metric** — OpenTelemetry counter for jobs expired due to deadline. Added to existing metrics: `nexjob.jobs.enqueued`, `nexjob.jobs.succeeded`, `nexjob.jobs.failed`, `nexjob.job.duration`.
- **`JobRecord.ExpiresAt` property** — stores calculated deadline timestamp. Null if no deadline specified.
- **`IStorageProvider.SetExpiredAsync(JobId, CancellationToken)`** — implemented in all five storage providers (InMemory, Postgres, SQL Server, Redis, MongoDB).
- **Internal `JobTypeResolver` helper** — centralizes runtime job type resolution for handler invocation and execution pipeline. Returns null on failure instead of throwing.

### Changed
- **README reorganized** — Quick Start and Features sections separated. Registration step now shows only service configuration (no dashboard middleware mixed in). Dashboard setup moved to dedicated step. Example code updated to use correct recurring job API (`RecurringAsync<TJob, TInput>` with input parameter). No-input job example now self-contained with constructor.
- **Storage providers table** — clarified implementation status: all five providers marked "Production ready" (In-memory, Postgres, SQL Server, Redis, MongoDB).

### Fixed
- Recurring job examples in README now match actual `IScheduler` API (requires `TInput` parameter).
- No-input job example in README now shows complete, copy-paste-friendly code with DI dependencies declared.

## [0.4.0] — March 2026

### Added
- **Wake-up channel** — Local enqueues trigger immediate dispatcher wake-up. Non-blocking, capacity=1 signal collapses rapid enqueues. Polling fallback preserved for distributed scenarios.
- **`deadlineAfter: TimeSpan?`** — Jobs not started within the deadline are marked `Expired` and skipped. New `JobStatus.Expired` terminal state. Deadline checked after fetch, before execution.
  ```csharp
  await scheduler.EnqueueAsync<PaymentJob, PaymentInput>(
      input,
      deadlineAfter: TimeSpan.FromMinutes(5));
  ```
- **`IDeadLetterHandler<TJob>`** — Automatic fallback when a job exhausts all retries. Resolved via DI. Handler exceptions are logged and swallowed — never crash the dispatcher.
  ```csharp
  public class PaymentDeadLetterHandler : IDeadLetterHandler<PaymentJob>
  {
      public async Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken ct)
          => await _alerts.SendAsync($"Payment failed: {lastException.Message}", ct);
  }
  builder.Services.AddTransient<IDeadLetterHandler<PaymentJob>, PaymentDeadLetterHandler>();
  ```
- **`nexjob.jobs.expired` metric** — OpenTelemetry counter for jobs expired due to deadline.
- **`JobRecord.ExpiresAt`** — stores calculated deadline timestamp. Null if no deadline.
- **`IStorageProvider.SetExpiredAsync`** — implemented in all five storage providers.
- **`JobTypeResolver`** — internal helper centralizing runtime type resolution.

### Changed
- README reorganized — Quick Start and Features sections separated.

### Fixed
- Recurring job examples in README now match actual `IScheduler` API.

## [0.3.3] — March 2026

### Added
- **`IJob` (no-input interface)** — Jobs without input no longer require a dummy DTO.
  ```csharp
  public sealed class CleanupJob : IJob
  {
      public async Task ExecuteAsync(CancellationToken ct) => await _db.Cleanup(ct);
  }
  await scheduler.EnqueueAsync<CleanupJob>();
  ```
  `AddNexJobJobs()` discovers both `IJob` and `IJob<TInput>` automatically.
- **`NexJobOptions.UseInMemory()`** — Explicit opt-in for in-memory storage. Was already the default; now matches documented API.

### Fixed
- `services.AddNexJob(opt => opt.UseInMemory())` now compiles.
- `AddNexJobJobs()` discovers `IJob` (no-input) implementations.
- `JobDispatcherService` handles `IJob` single-parameter `ExecuteAsync` without `MethodNotFoundException`.

## [0.3.2] — March 2026

### Added
- **Active server tracking** — Cluster-wide visibility into running instances. Each node registers ID, queues, and worker count. Heartbeat monitoring. New Servers tab in dashboard.
- **`NexJob.Dashboard.Standalone`** — Embedded dashboard for Worker Services and Console Apps. One package, one line: `AddNexJobStandaloneDashboard()`. Dashboard at `http://localhost:5005/dashboard`.
- **`StandaloneDashboardOptions`** — Configurable via `NexJob:Dashboard`: Port (default 5005), Path (default `/dashboard`), Title, LocalhostOnly.
- **`samples/NexJob.Sample.WorkerService`** — Runnable Worker Service sample.

### Changed
- Default dashboard path changed from `/jobs` to `/dashboard`.

### Fixed
- `DashboardSettings.Path` default corrected to `/dashboard`.

## [0.3.1] — March 2026

### Changed
- **Dashboard complete visual redesign** — Linear-inspired dark UI. Deep blue-black palette (`#080810`), indigo accent (`#6366f1`), Inter typography. Card-row job list, status pills, progress bars, tag badges, terminal-style logs, toggle switches in settings. Responsive mobile layout.

## [0.3.0] — March 2026

### Added
- **`IJobContext`** — Injectable runtime context: `JobId`, `Attempt`, `Queue`, `Tags`, `ReportProgressAsync`. Scoped per execution via `IJobContextAccessor`.
- **`WithProgress` extensions** — `IEnumerable<T>` and `IAsyncEnumerable<T>` report live progress as items are yielded.
- **Job progress tracking** — Live progress bar in dashboard job detail.
- **Job tags** — Enqueue with tags, filter in dashboard, `GetJobsByTagAsync`.
- **`ReportProgressAsync`** on `IStorageProvider` and all adapters.
- **Schema migration V5** — Adds `progress_percent`, `progress_message`, `tags` columns.
- **Benchmark results** — NexJob 2.87× faster (9.3 µs vs 26.6 µs), 85% less memory (1.67 KB vs 11.2 KB) than Hangfire per enqueue.

### Fixed
- `WithProgress<T>(IEnumerable<T>)` no longer calls `.GetAwaiter().GetResult()` inside the iterator.

## [0.2.0] — February 2026

### Added
- **Schema migrations** — Versioned DDL with advisory locks (`pg_advisory_lock` / `sp_getapplock`). Multiple instances apply migrations exactly once.
- **Graceful shutdown** — `StopAsync` waits up to `ShutdownTimeout` (default 30s) for active jobs. Configurable via `NexJob:ShutdownTimeoutSeconds`.
- **`[Retry]` attribute** — Per-job retry: `Attempts`, `InitialDelay`, `Multiplier`, `MaxDelay`, ±10% jitter. `[Retry(0)]` dead-letters immediately.
- **Distributed recurring lock** — Prevents duplicate recurring firings across instances. All five providers.
- **BenchmarkDotNet suite** — Throughput and enqueue latency vs Hangfire.
- **`dotnet new nexjob` template** — `NexJob.Templates` for instant scaffolding.

### Changed
- `JobDispatcherService` no longer cancels running job `CancellationToken` on shutdown.
- `NexJobSettings` gains `ShutdownTimeoutSeconds`.

### Fixed
- Multi-instance schema creation race condition (Postgres + SQL Server).
- `publish.yml` now packs all packages.

## [0.1.0-alpha] — 2025

### Added
- Core: `IJob<TInput>`, `IScheduler`, `IStorageProvider`
- Storage: InMemory, PostgreSQL, SQL Server, Redis, MongoDB
- Dashboard (Blazor SSR) with live updates, dark mode, settings page
- `appsettings.json` configuration with `IRuntimeSettingsStore` hot-reload
- Execution windows per queue (supports midnight-crossing ranges)
- `[Throttle]` attribute — resource-based concurrency limits
- `IJobMigration<TOld, TNew>` + `MigrationPipeline` — payload versioning
- OpenTelemetry `ActivitySource` + `System.Diagnostics.Metrics`
- `IHealthCheck` integration
- `AddNexJobJobs(Assembly)` — auto-registration of all `IJob<>` implementations
- `ContinueWithAsync` — job continuations
- Priority queues: Critical → High → Normal → Low
- Idempotency keys
- Recurring concurrency policy: `SkipIfRunning` / `AllowConcurrent`
- CI/CD pipeline publishing all packages on `v*` tag push

[Unreleased]: https://github.com/oluciano/NexJob/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/oluciano/NexJob/compare/v1.0.0...v2.0.0
[1.0.0]: https://github.com/oluciano/NexJob/compare/v0.8.0...v1.0.0
[0.8.0]: https://github.com/oluciano/NexJob/compare/v0.7.0...v0.8.0
[0.7.0]: https://github.com/oluciano/NexJob/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/oluciano/NexJob/compare/v0.5.1...v0.6.0
[0.5.2]: https://github.com/oluciano/NexJob/compare/v0.5.1...v0.5.2
[0.5.1]: https://github.com/oluciano/NexJob/compare/v0.5.0...v0.5.1
[0.5.0]: https://github.com/oluciano/NexJob/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/oluciano/NexJob/compare/v0.3.3...v0.4.0
[0.3.3]: https://github.com/oluciano/NexJob/compare/v0.3.2...v0.3.3
[0.3.2]: https://github.com/oluciano/NexJob/compare/v0.3.1...v0.3.2
[0.3.1]: https://github.com/oluciano/NexJob/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/oluciano/NexJob/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/oluciano/NexJob/compare/v0.1.0-alpha...v0.2.0
[0.1.0-alpha]: https://github.com/oluciano/NexJob/releases/tag/v0.1.0-alpha
