# Changelog

All notable changes to NexJob are documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

### Changed

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
- **Storage providers table** — clarified implementation status: four providers marked "Production ready" (In-memory, Postgres, SQL Server, Redis, MongoDB); Oracle marked "Planned".

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
- Storage: InMemory, PostgreSQL, SQL Server, Redis, MongoDB, Oracle (stub)
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

[Unreleased]: https://github.com/oluciano/NexJob/compare/v0.6.0...HEAD
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
