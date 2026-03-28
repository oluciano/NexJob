# Changelog

All notable changes to NexJob are documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.3.2] — March 2026

### Added
- **`NexJob.Dashboard.Standalone`** — embedded dashboard server for Worker Services
  and Console Applications. Install one package, call `AddNexJobStandaloneDashboard()`,
  and the full dashboard is available at `http://localhost:{Port}/dashboard` without
  any HTTP pipeline in the host project.
- **`StandaloneDashboardOptions`** — configurable via `NexJob:Dashboard` in
  appsettings.json: `Port` (default 5005), `Path` (default `/dashboard`),
  `Title`, `LocalhostOnly`.
- **`samples/NexJob.Sample.WorkerService`** — runnable Worker Service sample
  demonstrating standalone dashboard, `IJobContext`, progress reporting, and tags.

### Changed
- Default dashboard path changed from `/jobs` to `/dashboard` in both
  `DashboardSettings` and `UseNexJobDashboard()` to avoid conflicts with
  common REST API route conventions (`/jobs` is a frequent API route).

### Fixed
- `DashboardSettings.Path` default corrected to `/dashboard`.

## [0.3.1] — March 2026

### Changed
- **Dashboard complete visual redesign** — Linear-inspired dark UI replacing the
  generic 2021 aesthetic:
  - New design system: deep blue-black palette (`#080810`), indigo accent (`#6366f1`),
    proper typographic hierarchy, Inter font with optical variants
  - NexJob SVG hexagon logo replacing the `⚡` emoji
  - Overview: metric cards with status-colored top borders and animated pulse dot,
    120px throughput chart with hover tooltips, failure list as cards
  - Jobs: card-row layout replacing HTML table, status pill filters,
    inline progress bar for active jobs, tag badges with click-to-filter
  - Job detail: grouped property sections (Timeline / Configuration / Relationships),
    JSON payload with syntax highlighting (no external library),
    terminal-style execution log with level colors
  - Queues: utilization bar cards showing `processing / total` ratio,
    paused/window badges
  - Recurring: countdown display ("in 17h 43m"), cron in `<code>` element,
    concurrent/paused/deleted badges
  - Failed: warning banner, bulk Requeue All / Delete All actions in header
  - Settings: toggle switches for queue pause/resume, section cards
  - Responsive: mobile sidebar collapses to icon-only nav,
    2-column grid on tablet
  - `Helpers.StatusDot`, `Helpers.RelativeTime`, `Helpers.CountdownFriendly`,
    `Helpers.ColorizeJson` added
  - SSE stream extended to include active job progress for live progress bar updates

## [0.3.0] — March 2026

### Added
- **`IJobContext`** — injectable job runtime context providing `JobId`, `Attempt`, `Queue`,
  `Tags`, and `ReportProgressAsync`. Scoped to each job execution via `IJobContextAccessor`,
  mirroring the ASP.NET Core `IHttpContextAccessor` pattern.
- **`WithProgress` extensions** — `IEnumerable<T>.WithProgress(context)` and
  `IAsyncEnumerable<T>.WithProgress(context, ct)` report live progress percentages as items
  are yielded. The `IEnumerable` overload uses fire-and-forget to avoid blocking iterator
  threads; use the async overload when back-pressure or guaranteed delivery is required.
- **Job progress tracking** — live progress bar in dashboard job detail page
  (`.progress-container` / `.progress-bar` / `.progress-label`).
- **Job tags** — enqueue jobs with `string[]` tags; filter by tag in the dashboard jobs list;
  `GetJobsByTagAsync` added to `IStorageProvider` and all storage adapters
  (InMemory, Postgres, MongoDB, SQL Server, Redis).
- **`ReportProgressAsync`** added to `IStorageProvider` and all storage adapters.
- **Schema migration V5** — adds `progress_percent`, `progress_message`, and `tags` columns
  to Postgres and SQL Server schemas.
- **Benchmark results** — NexJob is 2.87× faster (9.3 µs vs 26.6 µs) and uses 85% less
  memory (1.67 KB vs 11.20 KB) than Hangfire per enqueue (Intel Xeon E5-2667 v4, .NET 8.0.25).

### Fixed
- `WithProgress<T>(IEnumerable<T>)` no longer calls `.GetAwaiter().GetResult()` inside the
  iterator, eliminating potential thread-pool deadlocks under I/O-backed storage providers.
- v0.2.0 release date corrected below.

## [0.2.0] — February 2026

### Added
- **Schema migrations** — versioned DDL with advisory locks (`pg_advisory_lock` for Postgres,
  `sp_getapplock` for SQL Server). Multiple instances starting simultaneously apply migrations
  exactly once. Schema version tracked in `nexjob_schema_version`.
- **Graceful shutdown** — `JobDispatcherService.StopAsync` waits up to `ShutdownTimeout`
  (default 30 s) for active jobs to complete before stopping. Configurable via
  `NexJobOptions.ShutdownTimeout` or `NexJob:ShutdownTimeoutSeconds` in `appsettings.json`.
  Jobs no longer receive a cancellation signal on SIGTERM — the orphan watcher handles stragglers.
- **`[Retry]` attribute** — per-job retry configuration overriding the global
  `RetryDelayFactory`. Supports `Attempts`, `InitialDelay`, `Multiplier`, `MaxDelay`,
  and ±10% jitter. `[Retry(0)]` dead-letters immediately on first failure.
- **Distributed recurring-job lock** — `TryAcquireRecurringJobLockAsync` /
  `ReleaseRecurringJobLockAsync` prevent duplicate recurring job firings when multiple server
  instances run simultaneously. Implemented for all five storage providers:
  InMemory (expiry-based), Postgres (`INSERT … ON CONFLICT DO NOTHING`),
  SQL Server (PK violation catch), MongoDB (duplicate key + TTL index), Redis (`SET NX + TTL`).
- **BenchmarkDotNet suite** — `benchmarks/NexJob.Benchmarks` measuring throughput
  (500 fire-and-forget jobs) and single enqueue latency vs Hangfire.
- **`dotnet new nexjob` template** — `NexJob.Templates` NuGet package for instant project
  scaffolding (`dotnet new install NexJob.Templates && dotnet new nexjob -n MyApp`).
- **Test coverage** — `RetryAttributeTests`, `GracefulShutdownTests`, `SchemaMigratorTests`,
  and `DistributedRecurringLockTests` added to `NexJob.Tests`.

### Changed
- `JobDispatcherService` no longer cancels the running job `CancellationToken` on host shutdown.
  Jobs complete naturally; only jobs exceeding `ShutdownTimeout` are abandoned and requeued.
- `NexJobSettings` gains `ShutdownTimeoutSeconds` (default `30`) for `appsettings.json` binding.
- `SchemaSQL` / `SqlServerSchemaSql` refactored into versioned constants (`V1CreateTables` …
  `V4CreateVersionTable`) replacing the single `CreateTables` blob.

### Fixed
- Multiple instances starting simultaneously no longer race on schema creation
  (Postgres + SQL Server).
- `publish.yml` now packs all seven NuGet packages:
  `NexJob`, `NexJob.Postgres`, `NexJob.MongoDB`, `NexJob.Dashboard`,
  `NexJob.SqlServer`, `NexJob.Redis`, `NexJob.Oracle`, and `NexJob.Templates`.

## [0.1.0-alpha] — 2025

### Added
- Core interfaces: `IJob<TInput>`, `IScheduler`, `IStorageProvider`
- Storage providers: InMemory, PostgreSQL, SQL Server (stub), Redis (stub), MongoDB, Oracle (stub)
- Dashboard (Blazor SSR) with live updates, settings page, dark mode
- `appsettings.json` configuration with live hot-reload support via `IRuntimeSettingsStore`
- Execution windows per queue (supports ranges crossing midnight)
- `IRuntimeSettingsStore` — pause queues and adjust workers at runtime without restart
- `[Throttle]` attribute — resource-based concurrency limits across all workers
- `IJobMigration<TOld, TNew>` and `MigrationPipeline` — automatic payload versioning
- OpenTelemetry `ActivitySource` + `System.Diagnostics.Metrics` (activity spans per lifecycle event)
- `IHealthCheck` integration (`builder.Services.AddHealthChecks().AddNexJob()`)
- `AddNexJobJobs(Assembly)` — auto-registration of all `IJob<>` implementations in an assembly
- `ContinueWithAsync` — job continuations (run job B only after job A succeeds)
- Priority queues — `Critical → High → Normal → Low` processed in order within each queue
- Idempotency keys — safe to enqueue the same logical job multiple times
- Recurring concurrency policy — `SkipIfRunning` (default) or `AllowConcurrent`
- CI/CD pipeline (`ci.yml` + `publish.yml`) publishing all packages to NuGet on `v*` tag push

[Unreleased]: https://github.com/oluciano/NexJob/compare/v0.3.1...HEAD
[0.3.1]: https://github.com/oluciano/NexJob/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/oluciano/NexJob/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/oluciano/NexJob/compare/v0.1.0-alpha...v0.2.0
[0.1.0-alpha]: https://github.com/oluciano/NexJob/releases/tag/v0.1.0-alpha
