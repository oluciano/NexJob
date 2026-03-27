# Changelog

All notable changes to NexJob are documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.0] — TBD

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

[Unreleased]: https://github.com/oluciano/NexJob/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/oluciano/NexJob/compare/v0.1.0-alpha...v0.2.0
[0.1.0-alpha]: https://github.com/oluciano/NexJob/releases/tag/v0.1.0-alpha
