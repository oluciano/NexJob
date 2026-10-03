# Changelog

All notable changes to NexJob are documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Triggers — built-in forwarding of exhausted jobs for Kafka and RabbitMQ (issue #312)**: set `ExhaustedJobsTopic` on `AddKafkaTrigger(...)`, or `ExhaustedJobsRoutingKey` (and optionally `ExhaustedJobsExchange`) on `AddRabbitMqTrigger(...)`, and a job created by that trigger that exhausts its retries is copied to that destination through the Outbox. The original message body is published verbatim and the job stays `Failed` in NexJob; an optional `ExhaustedJobsIncludeErrorHeader` adds the last error as header `nexjob.error` (off by default). Only that trigger's jobs are forwarded, the Outbox publisher jobs are never forwarded, and enabling it without the matching producer fails at startup. Only the body is forwarded; the original key, headers and message properties are not stored.
- **Core — `IDeadLetterForwarder`**: a new interface the dispatcher calls, after the typed `IDeadLetterHandler<TJob>`, for every registered forwarder whose `AppliesTo` is true, each isolated so none can stop another or the dispatcher. A handler written for a job type no longer prevents forwarding. New counters `nexjob.dead_letter.forwarded` and `nexjob.dead_letter.forward_failed`. Additive: nothing existing changes.
- **Dashboard — Clean 24h Hourly Trend chart (issue #318)**:
  - Implemented normalized 24h Hourly Throughput card with 24 evenly spaced bars, responsive tooltips, dynamic average threshold line, and anomaly drop warnings.
  - Added quick-copy buttons (`📋 Copy JSON`, `📋 Copy Logs`) with visual confirmation across Job Detail and Recurring Execution modals.
- **Dashboard — Clean Server Node ID formatting (issue #318)**:
  - Composite server node IDs (`{Host}:{PID}:{Guid}`) are now formatted cleanly across Overview, Topology Map, and Servers views as `{Host}:{PID} #{shortGuid}`, preserving the full 32-character unique ID in tooltips for inspection while keeping tables and cards compact and readable.
- **Dashboard — In-memory Host CPU and RAM radial gauges (issue #318)**:
  - Added lightweight, volatile in-memory process CPU % and physical RAM working set tracking rendered as modern circular SVG ring gauges in the Overview and Servers views, with zero database persistence or storage schema overhead.
- **Documentation — Modernized documentation site and structured categorization**:
  - Reorganized documentation into modular categories (`concepts/`, `guides/`, `storage/`, `integrations/`, `reference/`), splitting storage providers and cross-cutting topics into dedicated standalone guides.
  - Added dedicated sections for Queue Circuit Breaker resilience and clarified the architectural distinction between Message Broker Transport Queues (RabbitMQ/Kafka) and NexJob Storage Queues.
  - Upgraded MkDocs Material configuration with responsive category tabs, expandable sidebars, admonitions, and deep purple brand theme.

### Changed

- **Tests — crash recovery, several nodes and a real process kill (issues #337, #338, #339)**: the reliability suite now covers orphan recovery end to end on the four database providers, several NexJob nodes sharing one database (including recurring jobs on every provider, which was PostgreSQL only), and a job running in a process that is killed with `Process.Kill` on SQL Server. Test-only change; the one product gap it found (a job exhausted by crashes does not call dead-letter handlers, #340) is tracked separately.
- **SQL Server and PostgreSQL — fewer values in SQL text (issue #324)**: the SQL Server fetch built its queue priority list by escaping quotes into the SQL text; queue names are now bound as parameters (`@q0`, `@q1`, ...) and `TOP` uses `@maxBatchSize`. The PostgreSQL fetch uses `@maxBatchSize` and the migrator binds its advisory lock key. Queue names come from the worker configuration, not from user input, so this is hardening, not a fix for an exploitable bug; behaviour is unchanged.
- **Core and dashboard — cancellation tokens passed explicitly (issue #324)**: releasing a distributed throttle slot after a cancelled or timed-out wait now says `CancellationToken.None` (the release must complete even though the wait was cancelled), the batch-acknowledgement flusher starts with its own token, and the dashboard page write observes `RequestAborted`.
- **Docs — what pausing a queue and deleting a job guarantee (issue #140)**: pausing takes effect on each node's next polling cycle (a job fetched by a cycle already in flight can still run, and running jobs are never interrupted); deleting a running job does not stop it and its result is discarded. Written in the `IJobControlService` documentation, the storage guide and the troubleshooting page.
- **Core — `JobDispatcherService` depends on an internal `IJobExecutor` (issue #167)**: the dispatcher no longer depends on the concrete `JobExecutor`, so its polling loop and worker pool can be tested with a fake executor. Internal refactor; no behaviour change.
- **Tests — Reliability suites merged into `NexJob.ReliabilityTests` (issue #297, absorbs #278)**: the InMemory and Distributed suites are one project with shared scenario classes and a thin subclass per provider, with real assertions (execution counts, condition waits) instead of fixed delays. The suite went from 236 tests with 28 failures (about 2 minutes) to 98 tests with none (about 14 seconds), 16 of them skipped until #321. CI now runs it in three tiers: the InMemory scenarios on every pull request, a concurrency and restart subset per database provider on pull requests that touch storage or the dispatcher, and every scenario nightly and on demand. Test and CI only; no library behaviour changed.

### Fixed

- **Core and storage — a job whose attempts are used up by crashes now reaches the dead-letter handler and forwarders (issue #340)**: when the node running a job died and the job had no attempts left, the orphan watcher moved it to `Failed` without calling `IDeadLetterHandler<TJob>` or any `IDeadLetterForwarder` (so the Kafka/RabbitMQ forwarding of #312 was skipped too), and a job that kills its process every time reached `Failed` silently. A new optional interface, `IOrphanedJobReporter`, lets a storage return the jobs its orphan scan failed; the five built-in providers implement it, and the watcher then runs dead-letter handling for each one, exactly once even with several nodes scanning. The handler receives a new `OrphanedJobException` (the job never reported an error, so one stands in for it). A custom `IJobStorage` that does not implement the interface keeps the previous behaviour. Behaviour change: handlers now run for jobs that used to fail silently.
- **PostgreSQL — `AddNexJobPostgres(NpgsqlDataSource)` creates the schema (issue #326)**: unlike the connection-string overload, it never ran the migrations, so on a new database the host failed with `relation "nexjob_settings" does not exist` and enqueuing failed too. The registration now migrates when the host starts, before any other hosted service (idempotent and advisory-locked). The provider constructor still does not, because the dashboard read replica is built with it and must never run DDL.
- **SQL Server — an already open `SqlConnection` is refused with a clear message (issue #326)**: `SqlServerStorageProvider(SqlConnection, NexJobOptions)` keeps the connection string and opens its own connections with it, but SqlClient removes the password from it as soon as the connection is opened, so the first new connection failed with `Login failed` long after the cause. The constructor now rejects that case (and `null`) at once with an `ArgumentException` that says to pass a connection that is not open or to use `Persist Security Info=True`. A closed connection (what `UseDashboardReadReplica` passes) and integrated security are unaffected.
- **MongoDB — safer rolling upgrades (issue #335)**: documents now ignore elements they do not know (the driver throws on them by default and v5.7 did not ignore them), and `ExpiresAt` is no longer written for jobs without a deadline, so a v5.7 node can still read every job that has none. A job that has a deadline still carries the new element and cannot be read by a v5.7 node: do not use `deadlineAfter` until every node is on v5.8. See the migration guide.
- **Redis — deleting an enqueued job no longer leaves a ghost behind (issue #140)**: `DeleteJobAsync` removed the job hash but kept its id in the queue. When the dispatcher then fetched that id, the fetch script wrote `status` and `attempts` to the missing key, which recreated a hash without an `id`: the fetch and `GetJobByIdAsync` threw `KeyNotFoundException` and a ghost job stayed in `Processing`. Deleting now removes the id from its queue, and both fetch scripts drop queue entries whose job no longer exists. The same ghost appeared when a job was deleted **while it ran**: its late heartbeat, progress, checkpoint, execution logs, payload trim and result were written with a plain `HSET`, and could even be written for an id that never existed. Those writes now apply only while the job exists (one small Lua script), the heartbeat no longer re-adds a deleted job to the processing index, and the batch acknowledgement skips jobs that are gone. Found by the new real-database control-service scenarios; the other four providers were not affected.
- **Core — an interrupted or foreign job no longer consumes an attempt on database providers (issue #327)**: when a job was interrupted by host shutdown, or deferred because its job type is not available in this process, the executor said it did not consume the attempt but only edited its local copy of the job, which PostgreSQL, SQL Server, MongoDB and Redis never persisted. A job restarted by repeated deploys could therefore reach dead-letter without ever failing. Both paths now commit with `RefundAttempt`, which every provider honours (introduced with #299).
- **Core — a saturated `[Throttle]` resource no longer starves the rest of a node (issue #299)**: a throttled job that could not get its slot kept its worker slot indefinitely, so enough waiters could occupy every worker and block other queues and resources. The wait is now bounded (about 5 s); after that the job is returned to the queue and its worker is released. Returning a job this way does not consume an attempt, never reaches dead-letter and is not counted as a failure. New counter `nexjob.jobs.throttle_deferred`. To make the attempt refund real on every provider, `JobExecutionResult` gets an optional `RefundAttempt` flag honoured by the PostgreSQL, SQL Server, MongoDB, Redis and InMemory providers (additive; existing custom providers ignore it). Jobs that wait on a saturated resource may now run in a different order than they were enqueued.
- **Storage — `deadlineAfter` is now enforced on PostgreSQL, SQL Server, MongoDB and Redis (issue #321)**: the job deadline (`JobRecord.ExpiresAt`) was never written by the database providers, so it came back `null` on fetch and expired jobs ran normally. It is now stored and returned (PostgreSQL and SQL Server: new nullable `expires_at` column, migration V11, applied at startup; MongoDB and Redis: new field). Jobs enqueued before the upgrade keep no deadline. **Behaviour change to be aware of:** applications that pass `deadlineAfter` will now see jobs marked `Expired` instead of executed once the deadline passes.
- **Dashboard — Throughput 24h single-bar blowout (issue #318)**:
  - The hourly throughput calculation now always normalizes and pads all 24 hours (from `now.AddHours(-23)` to `now`) with zero-counts for inactive intervals, preventing single recorded hours from stretching into full-width solid blocks.
  - Constrained histogram `.bar` to `max-width: 24px` with centered alignment.
- **Dashboard — Plaintext clipboard copy & style leakage prevention (issue #318)**:
  - Added global copy event sanitization interceptor across all `.terminal-window`, `pre`, and modal log viewports, ensuring manual `Ctrl+C` / `Cmd+C` writes pure plaintext to the clipboard instead of leaking `<span class="jk">` syntax-highlighting tags or inline CSS styles into rich-text paste targets.
  - Enhanced all code and payload terminal headers with prominent `📋 Copy JSON` and `📋 Copy Logs` buttons.
  - Fixed modal backdrop click handling in `logModal` so mouse drag-selection of log text never prematurely closes the dialog on mouse release.

## [5.7.0] - 2026-10-01

### Security

- **Standalone dashboard is secure by default (issue #295)**: `StandaloneDashboardOptions.LocalhostOnly` now defaults to `true` (loopback only; it was `false`, all interfaces), and a registered `IDashboardAuthorizationHandler` is now enforced in standalone mode (it was silently ignored because the embedded server has its own container). The handler is resolved per request in a scope of the host container, so any lifetime works, and a throwing handler never grants access. The startup warning from #294 now appears only when the dashboard is exposed **and** no handler is registered. The standalone server runs no authentication middleware, so a handler must authenticate from `context.Request`; the wiki has a Basic-auth example that is covered by tests. `DashboardSettings.LocalhostOnly` in core is aligned to `true` (it is not read by anything). **Behaviour change:** see `docs/wiki/18-Migration.md` (containers need `LocalhostOnly = false` plus a handler).

### Added

- **Documentation: forwarding a dead-lettered job to Kafka or RabbitMQ (issue #311)**: explains what is broker-native (messages that can never become a job) and what stays only inside NexJob (a job that exhausted its retries), and why. Shows a pattern with an open-generic `IDeadLetterHandler<>` that forwards the original message body of one trigger queue through the Outbox, with the caveats (polled queues, a specific handler wins, key and headers are not kept, the forward is not transactional, requeue twice, PII). The code is compiled and run by new tests in `NexJob.Kafka.Tests` and `NexJob.RabbitMQ.Tests`. A built-in forwarder is tracked in #312.

### Changed

- **Database connection usage (issue #307)**: NexJob usually shares its database with other applications, so its footprint is now visible and bounded by design.
  - `AddNexJobPostgres(connectionString)` builds the runtime settings store from the provider's data source, so a node keeps **one** pool instead of two (the connection limit applied twice before). A host that resolved only the settings store also gets the schema migrated now, instead of failing with `relation "nexjob_settings" does not exist`.
  - PostgreSQL and SQL Server log one line at startup with the `Maximum Pool Size` found and the worker count, and a warning when the pool is smaller than `Workers`. A host with `Workers = 0` logs nothing. No behaviour change.
  - New wiki section "Database connections and pool sizing" (rule of about `Workers` + 10 per node times the number of nodes, the option name in each driver, measured numbers, how to check on the database). Measured on PostgreSQL: 5 workers use 10 connections, 30 use 35, 60 use 60.
- **CI runners pinned to `ubuntu-24.04` (issue #302)**: all workflows used `ubuntu-latest`, which GitHub migrates to Ubuntu 26 on 2026-10-19. Pinning keeps CI and the release pipeline on a known image until the move is tested on purpose. No change to the packages.

### Fixed

- **Redis — job index and status sets are reconciled periodically and under a lock (issue #286)**: the index (`nexjob:index:all`) and the Succeeded/Failed sets were built once and then guarded by a marker. A node on an older version that kept writing after the marker was set left jobs out of them for good (missing from the dashboard and tag and catalog queries, never purged by retention, undercounted in the totals), and the first backfill had no lock, so several nodes could scan a large Redis at once. The marker now holds the time of the last reconciliation, and every hour one node, holding a token lock that is renewed per chunk and released when the run ends, rebuilds the index from the job hashes; the other nodes skip. A job purged during the scan is not added back (the backfill goes through a script that checks the hash exists), a run that fails does not advance the marker and is retried, and a warning with the count is logged when jobs were missing from the index. Rolling upgrades no longer need the marker to be deleted. No public API change.
- **SQL Server — deadlock victims (error 1205) under concurrent workers (issue #279)**: the statement that releases a job's continuations (`WHERE parent_job_id = @id`) was compiled as a **clustered index scan**. `idx_nexjob_jobs_parent` is a filtered index and a parameter does not prove it is not null, and on a small table SQL Server prefers a scan; the plan is chosen on first use and then reused, so a fresh database or a restarted node ran every commit through a scan that took update locks on other jobs' rows. With several workers, that produced deadlocks and heavy blocking (on a fresh database a 2400-job run with 30 workers recorded about 50 deadlocks, and only 24 of the 2400 jobs had finished after three seconds). The four statements that release continuations (`AcknowledgeAsync`, `AcknowledgeBatchAsync`, `EnqueueContinuationsAsync` and `CommitJobResultAsync`) now read through `idx_nexjob_jobs_parent` with an index hint and an explicit `IS NOT NULL`, so the plan is a seek whatever the table size. After the change, five runs on fresh databases recorded no deadlock and completed every job. The index has been part of the SQL Server schema since the provider was added and NexJob recreates it at startup. No schema change.
- **PostgreSQL — `AddNexJobPostgres(NpgsqlDataSource)` crashed the host on databases that require a password (issue #308)**: the runtime settings store was built from `dataSource.ConnectionString`, which Npgsql returns **without the password**, so it failed with `No password has been provided but the backend requires one`, a background service threw and the host stopped within seconds. The store now opens its connections through the same `NpgsqlDataSource` as the provider (internal constructor, no public API change), so the password, the pool and any data-source configuration are shared. Affected v5.6.0 to v5.6.2 and only the `NpgsqlDataSource` overload; the connection-string overload was never affected. New integration tests run against a real PostgreSQL that requires a password.
- **Dashboard no longer offers a Workers control, and settings with no effect are no longer silent (issue #281)**: the Settings page saved a `Workers` override and showed it as effective, but the dispatcher sizes its pool once at startup and never read it. The worker count is a deployment decision, so the Workers card and the `POST settings/workers` route are removed (worker counts per node stay on the Servers page), and a stored value is ignored. `RuntimeSettings.Workers` stays for compatibility and is documented as not applied. At startup the dispatcher now logs a Warning when `QueueSettings[].Workers` or a `DefaultQueue` other than `default` is configured, because both are accepted and ignored. The `WebApi` sample no longer configures a per-queue `Workers`.

## [5.6.2] - 2026-10-01

### Fixed

- **Flaky dead-letter unit tests**: `Job_ExceedingMaxAttempts_MovesToDeadLetter` and `FailedJob_NoRetry_WhenMaxAttemptsExhausted` slept a fixed 50/100 ms before reading the metrics and could read `Failed = 0` on a slow CI runner. They now wait (bounded, 5 s) for the dead-letter result, with the same assertions. Test-only change, no library change.
- **Samples aligned with the code (issue #177)**: nothing here changes the libraries.
  - Every web sample now has its own port in `Properties/launchSettings.json` (`5001` MinimalApi, `5002` WebApi, `5004` ConfiguredRecurring, `5007` Storage, `5008` CloudTriggers, `5009` RabbitMQ, `5010` Kafka), so they can run side by side; the `WorkerService` dashboard stays on `5005`. The root and `samples/` READMEs, the `.http` files and the endpoint lists now match the real routes.
  - Configuration keys that `NexJobSettings` silently ignored were replaced by real ones (`Workers`, `PollingInterval`, `HeartbeatInterval`), and `MinimalApi` now binds its `NexJob` section (it ran with the defaults, not the values in its `appsettings.json`).
  - Recurring jobs in the `WebApi` (`email`, `maintenance`) and `WorkerService` (`maintenance`) samples targeted queues no worker polled, so they were enqueued and never ran; those queues are now polled.
  - The `WorkerService` sample dashboard now listens on `localhost` only, as its `appsettings.json` already intended.
  - README claims that contradicted the code were corrected (default retry delay, Redis throttle slots, the Storage sample's replica string being the primary, the RabbitMQ and Kafka outbox being volatile on in-memory storage, the `CloudTriggers` configuration keys).
  - A new test (`SampleConfigurationTests`) fails when a sample configures a key `NexJobSettings` ignores or schedules a recurring job on a queue that is not polled.
- **Dashboard — empty-state icons (issue #284)**: the empty states on the Jobs, Queues, Recurring, Catalog, Listeners, Failed, Servers and Job detail pages passed invalid SVG path data (a `viewBox`, or a list of points), so the icon was never drawn and the browser logged `Expected moveto path command` errors. Each page now uses valid icon path data, and `HtmlFragments.EmptyState` HTML-encodes it.
- **Standalone dashboard — network exposure warning (issue #294)**: when `LocalhostOnly` is `false` (the default), a warning is now logged at startup saying the dashboard is reachable from the network and has no authorization in standalone mode, and pointing to `LocalhostOnly = true`. The listening address and defaults are unchanged; the wiki (`10-Dashboard.md`) documents the exposure.

### Changed

- **Throttling documentation (issue #296)**: `07-Throttling.md` and the FAQ now state that a throttled job waits while `Processing` and holds its worker, give the `Workers` sizing rule, and explain that several `[Throttle]` attributes are acquired in declaration order while earlier slots stay held.
- **`NexJob` package README**: the package now ships its own short README (`src/NexJob/README.md`: what it is, install, quick start, which packages to add, links to the documentation) instead of the full repository README, matching every other NexJob package.

### Added

- **Documentation site on GitHub Pages (issue #292)**:
  - The wiki in `docs/wiki/` is now built with MkDocs (Material) and published to `https://oluciano.github.io/NexJob/` on every push to `main`, so the site always matches the released version.
  - The site has a search-friendly title and description, the root README links to it, and `PackageProjectUrl` now points to it.
  - New FAQ page (`docs/wiki/22-FAQ.md`): 12 questions on multi-node fetching, crash recovery, retention, failures, the circuit breaker, retry vs throttle, at-least-once execution, deadlines, shared databases, the dashboard, OpenTelemetry and trigger bodies. Every answer cites the source files (and tests) it was verified against.
  - `docs/site/prepare.sh` stages a copy of the wiki (Home becomes the index, asset and Changelog links are rewritten) and leaves the wiki sources untouched; `mkdocs build` runs in strict mode, so a broken link fails the build. Pull requests that touch the docs run the same build without deploying.

## [5.6.1] - 2026-09-30

### Fixed

- **Broker triggers (AwsSqs, AzureServiceBus, GooglePubSub, Kafka, RabbitMQ) — message body now reaches `IJob<string>` verbatim (issue #287)**:
  - The body was stored raw in `InputJson` but deserialized as a JSON string at execution, so any body that was not itself a JSON string literal (plain text, XML, CSV, a JSON object) failed the job with `JsonException`. On PostgreSQL a non-JSON body was rejected at enqueue by the `jsonb` column, and the trigger retried it in a loop.
  - The five triggers now store the body as a JSON string (`JsonSerializer.Serialize(body)`), so it round-trips byte for byte on every storage provider and the job decides how to parse it.
  - Behavior change: a producer that worked around the bug by publishing a JSON string literal (for example `"hello"` with quotes) now receives the quotes as part of the text.

## [5.6.0] - 2026-09-30

### Added

- **`NexJob` Core — Structured Logging Scope via `ILogger.BeginScope` during Job Execution (Issue #204)**:
  - `JobExecutor` now automatically opens an `ILogger.BeginScope` at the start of every job execution, injecting structured keys (`NexJob.JobId`, `NexJob.JobType`, `NexJob.Queue`, `NexJob.Attempt`, `NexJob.TraceParent`) as ambient properties.
  - Every log entry emitted inside `IJob.ExecuteAsync` — or internally by `JobExecutor` — automatically inherits all 5 structured fields, enabling instant correlation in Splunk, Grafana Loki, Datadog, and Elastic/Kibana without additional configuration.
  - Scope is established before job invocation and covers both the success path and all failure/retry paths.
  - `NexJob.TraceParent` is set to an empty string when no W3C `traceparent` was propagated (never null).
  - Documentation added in `docs/wiki/12-OpenTelemetry.md` with structured log sample output and query examples for Splunk, Loki, and Datadog.

- **`NexJob.Dashboard` — Operational Warning Confirmation on Inactive/Paused Queues (Issue #226)**:
  - All trigger and requeue action points now check active worker queues before allowing the operation to proceed.
  - Catalog **parameterless** direct trigger (`IJob`): a blocking `window.confirm()` dialog is shown before form submission when the target queue has no active worker nodes.
  - Catalog **parameterized modal** (`IJob<T>`): the existing passive inline warning is now a mandatory confirmation gate — the modal submit is intercepted by JS and shows a `window.confirm()` if the queue warning box is visible.
  - **Job Detail** `▶ Run Now` and `↺ Requeue` buttons: confirmation message dynamically includes a queue orphan/paused warning when the job's target queue has no active workers.
  - **Recurring Job Detail** `▶ Trigger Now` button: confirmation dialog includes queue warning when no workers listen to the recurring job's queue.
  - **Recurring Jobs list** (inline ⚡ icon): trigger icon intercepts click with a queue-aware `confirm()` per row.
  - **Failed Jobs** `↺ Requeue All`: confirmation now includes a warning when any visible queue lacks active workers.
  - All pages receive `ActiveWorkerQueues` (the set already computed once per request in `DashboardMiddleware`) — zero additional I/O per page.

- **`NexJob.Dashboard` — UX Polish, Action Ergonomics & Filter Indicators (Issues #214, #220)**:
  - Added Job Detail actions: Enqueued and Scheduled states now expose Cancel & Delete operations directly from the job detail view.
  - Added Job Checkpoint Inspector: collapsible `💾 Checkpoint State` panel on Job Detail view displaying formatted progress JSON when `CheckpointJson` is present.
  - Added Retention Payload Indicator: Job Detail displays an informational badge (`Payload stripped by retention policy (TrimPayloadOnSuccess)`) when payload is trimmed.
  - Added Destructive Action Safety: Added browser confirmation dialog (`Pause ALL recurring jobs cluster-wide?`) to the Settings page Pause All button.
  - Added Sidebar Orphan Queue Alert: Added alert badge (`.nav-counter.alert`) to the Queues sidebar item when enqueued jobs exist in queues without active listening workers.
  - Added Job Catalog Ergonomics: Enqueuing a job from the catalog redirects with `?triggered={jobId}` displaying a success notification banner with direct navigation to the job; added sort controls (`failure-rate`, `duration`, `runs`).
  - Added Active Filter Breadcrumb Chips: Active filters (status, queue, tag, search) render removable badge chips with individual `[x]` clear links and a `Clear all` button in `FilterBar`.
  - Added Circuit Breaker Drill-down Links: Direct `View Errors` navigation link to `/failed?queue={q}` for `HalfOpen` and `Recovering` circuit states.
  - Added Topology Diagram Responsiveness: Responsive layout media query wrapping topology nodes on mobile viewports and rotating connection arrows.

- **`NexJob.IntegrationTests` & Storage Providers — Integration Test Suite Synchronization for v5.6 Features (Issue #219)**:
  - Extended shared `StorageProviderTestsBase` contract suite with real-database integration tests across all 5 providers (`InMemory`, `PostgreSQL`, `SQL Server`, `MongoDB`, `Redis`).
  - Added integration contract coverage for Checkpoints (`SaveCheckpointAsync`, state persistence, and auto-clear on `AcknowledgeAsync` / `AcknowledgeBatchAsync`).
  - Hardened `AcknowledgeAsync` and `AcknowledgeBatchAsync` across `PostgresStorageProvider`, `SqlServerStorageProvider`, `RedisStorageProvider`, and `MongoStorageProvider` to automatically reset `checkpoint_json = NULL` / unset upon job acknowledgment.
  - Added integration contract coverage for Retention strategies (`PurgeOnSuccess` physical deletion and `TrimPayloadOnSuccess` payload stripping).
  - Added integration contract coverage for Job Catalog native aggregations (`GetJobCatalogAsync`).

- **`NexJob` Core & Storage Providers — Progress Checkpoints & State Saving for Long-Running Jobs (Issue #206)**:
  - Added `CheckpointJson` property to `JobRecord` for serializing and preserving arbitrary checkpoint state.
  - Extended `IJobContext` with `TState? GetCheckpoint<TState>()` and `Task SaveCheckpointAsync<TState>(TState state, int? percent, string? message, CancellationToken ct)`.
  - Added `SaveCheckpointAsync(JobId, string, int?, string?, CancellationToken)` to `IJobStorage` with default interface implementation.
  - Implemented checkpoint persistence across all 5 storage providers: `InMemoryStorageProvider`, `PostgresStorageProvider`, `SqlServerStorageProvider`, `RedisStorageProvider`, and `MongoStorageProvider`.
  - Added schema migrations V9 for PostgreSQL (`V9AddCheckpointColumn`) and SQL Server (`V9AddCheckpointColumn`) adding nullable `checkpoint_json` column.
  - Enforced state persistence on retry: `CheckpointJson` is preserved across retry attempts (`Scheduled`) and dead-letter moves (`Failed`) so interrupted jobs resume exactly from the last saved state.
  - Enforced anti-bloat cleanup: `checkpoint_json` is automatically cleared (`NULL` / unset) upon successful execution (`Succeeded`) across all storage providers.
  - Added 3N unit testing matrix (Positive, Negative, Boundary) in `tests/NexJob.Tests/JobCheckpointTests.cs` and updated `tests/NexJob.Tests/SchemaMigratorTests.cs`.
  - Documented in `docs/wiki/08-IJobContext.md` and `docs/wiki/15-Common-Scenarios.md`.

- **`NexJob` Core & `NexJob.Dashboard` — Dynamic Circuit Breaker & Queue Auto-Pausing (Issue #207)**:
  - Added declarative queue-level circuit breaker via `NexJobOptions.ConfigureQueue(queue, q => q.EnableCircuitBreaker(...))`.
  - Added 4-state lifecycle (`Closed`, `Open`, `HalfOpen`, `Recovering`) to prevent thundering herds ("metralhadora" effect) when downstream APIs experience severe outages.
  - Implemented progressive exponential backoff multiplier on cooldown for repeated probe failures up to `MaxOpenDuration`.
  - Added selective exception filtering with predicate support (`cb.BreakOn<TException>(predicate)`) and out-of-the-box helper `cb.BreakOnTransientHttpErrors()`: automatically trips on 5xx, timeouts, 429 Too Many Requests (Rate Limits), and network drops while safely ignoring client/payload bugs (400 Bad Request, 404 Not Found, 422).
  - Added `Recovering` gradual ramp-up state capping concurrency to `RecoveryConcurrency` during `RecoveryDuration` when downstream recovers.
  - Integrated with `JobDispatcherService` to bypass open queues and dispatch canary in `HalfOpen`.
  - Integrated with `IJobControlService.ResetQueueCircuitAsync` and `DashboardMiddleware` (`POST /queues/{queue}/reset-circuit`) for manual reset.
  - Updated Dashboard `/queues` cards with real-time badges (`⚡ CIRCUIT OPEN (Xs)`, `🟡 CANARY TESTING`, `🟢 RECOVERING`) and manual reset action.
  - Added 3N unit testing matrix using `TimeProvider` / `FakeTimeProvider` in `tests/NexJob.Tests/QueueCircuitBreakerTests.cs`.
  - Documented in `docs/wiki/07-Throttling.md` and `docs/wiki/13-Best-Practices.md`.

- **`NexJob` Core & Storage Providers — Anti-Bloat Retention Strategies (Issue #203)**:
   - Added declarative `[Retention(PurgeOnSuccess = bool, TrimPayloadOnSuccess = bool)]` attribute to decorate job classes.
   - Implemented immediate row purge (`PurgeOnSuccess = true`) upon successful job execution across all storage providers (`InMemory`, `PostgreSQL`, `SQL Server`, `MongoDB`, `Redis`), preventing table growth and WAL bloat in high-frequency streaming workloads.
   - Implemented payload stripping (`TrimPayloadOnSuccess = true`), wiping `InputJson` upon successful execution while preserving job state, timestamps, tags, and logs for auditability.
   - Hardened `JobExecutor` to propagate retention metadata in `JobExecutionResult` and bypass delayed batch acknowledgment when purge or trim operations are requested.
   - Preserved lifetime job catalog statistics (`/catalog`) in `InMemoryStorageProvider` via aggregated lifetime tracking counters even when individual job instances are immediately purged.
   - Documented retention strategies in `docs/wiki/06-Retry-And-Dead-Letter.md` and `docs/wiki/13-Best-Practices.md`.
   - Added 3N unit testing matrix (Positive, Negative, Boundary) in `tests/NexJob.Tests/RetentionHardeningTests.cs`.


- **`NexJob.Dashboard` — Orphan Queues & Inactive Worker Indicators (Issue #212)**:
  - Added real-time tracking of queue coverage against active worker nodes registered via `IJobStorage.GetActiveServersAsync()`.
  - Added warning badge `⚠️ NO WORKERS` and warning subtitle on `QueuesPage` cards whenever a queue has pending jobs (`Enqueued > 0`) but no active worker nodes in the cluster configured to process it.
  - Added unserved queues alert banner on `ServersPage` highlighting queues that have accumulated work without any online worker nodes.
  - Added live queue coverage check in the `CatalogPage` Trigger Modal, alerting operators before enqueuing a job if the chosen queue currently lacks active workers.
  - Added 3N unit testing matrix (Positive, Negative, Boundary) in `tests/NexJob.Tests/StandaloneDashboardTests.cs`.
  - Documented behavior in `docs/wiki/10-Dashboard.md`.

- **`NexJob` Core & `NexJob.Dashboard` — Job Catalog & Definitions View and On-Demand Triggering (Issue #202)**:
  - Added `JobCatalogItem` record encapsulating job definitions with aggregated execution telemetry (`JobType`, `Queue`, `TotalRuns`, `SucceededRuns`, `FailedRuns`, `LastExecutedAt`, `AvgDurationSeconds`).
  - Extended `IDashboardStorage` with `GetJobCatalogAsync` featuring a default interface implementation for backward compatibility with external providers (with deprecation notice for v6.0).
  - Implemented high-performance native aggregated queries (`GROUP BY job_type, queue`) in `InMemoryStorageProvider`, `PostgresStorageProvider`, and `SqlServerStorageProvider`.
  - Added `/catalog` page in `NexJob.Dashboard` rendering an interactive table under the `MONITORING` sidebar section with instant search filtering, queue filtering, failure rate badges, average duration calculations, and deep-linking to filtered execution history in `/jobs`.
  - Added on-demand ad-hoc execution (`POST /catalog/{jobType}/trigger`) for jobs directly from the catalog table, adhering to multi-cluster active selection (`?cluster={id}`) and `IsReadOnly` safety guards.
  - Added Swagger-style interactive Trigger modal for parameterized jobs (`IJob<T>`): clicking "Trigger" opens a dialog pre-filled with an automatically generated sample JSON schema for the input type (and target queue), allowing operators to edit the payload or reset to sample before triggering. Parameterless `IJob`s trigger immediately with 1 click.
  - Added 3N testing matrix (Positive, Negative, Boundary) in `tests/NexJob.Tests/InMemoryStorageProviderTests.cs` and `tests/NexJob.Tests/StandaloneDashboardTests.cs`.
  - Updated documentation in `docs/wiki/10-Dashboard.md`, root `README.md`, `src/NexJob.Dashboard/README.md`, and `samples/NexJob.Sample.WorkerService`.


- **`NexJob.Dashboard` & `NexJob.Dashboard.Standalone` — Multi-Cluster Dashboard Federation (Issue #200)**:
  - Added `DashboardCluster` descriptor encapsulating cluster identity (`Id`, `Name`), isolated storage contracts (`DashboardStorage`, `JobStorage`, `RecurringStorage`, `ControlService`, `RuntimeStore`), cluster-scoped `Queues`, and `IsReadOnly` safety mode.
  - Added `Clusters` collection and fluent `AddCluster(...)` API to `DashboardOptions` and `StandaloneDashboardOptions`.
  - Added multi-cluster switcher dropdown in Maxton header when `Clusters.Count > 1`, with active cluster selection controlled via `?cluster={id}` and seamless URL parameter preservation across redirects and actions.
  - Isolated SSE metrics stream and IMemoryCache keys by cluster (`$"nexjob:dashboard:metrics:{clusterId}"`), preventing cross-cluster cache collision.
  - Enforced `IsReadOnly` mutation guard returning `403 Forbidden` on POST actions for read-only clusters.
  - Added 3N testing matrix (Positive, Negative, Boundary) in `tests/NexJob.Tests/StandaloneDashboardTests.cs`.

- **`NexJob.Dashboard` & `NexJob.Dashboard.Standalone` — Dedicated Ops Host Mode and Queue Scoping (Issue #199)**:
  - Added `DisableWorkers` (bool, default `false`) to `StandaloneDashboardOptions`. When set to `true`, `NexJobOptions.Workers` is configured to `0`, allowing a headless worker to function as a dedicated monitoring/ops host without taking processing slots from background workers.
  - Added `Queues` (`IReadOnlyList<string>?`) to `DashboardOptions` and `StandaloneDashboardOptions` for queue scoping and isolation.
  - Scoped dashboard navigation counters, queue cards, and default `/jobs` filter exclusively to the configured queues when `Queues` is specified.
  - Added comprehensive documentation and architectural guidelines in `docs/wiki/10-Dashboard.md`, `docs/wiki/13-Best-Practices.md`, and `src/NexJob.Dashboard.Standalone/README.md`.
  - Added 3N unit testing matrix in `tests/NexJob.Tests/StandaloneDashboardTests.cs` (positive, negative, and boundary scenarios).

### Changed

- **Documentation — Wiki, package READMEs and XML docs audited against the code (Issues #173, #174, #175, #178)**:
  - Removed APIs that do not exist (`opt.UseRedis`, `UseDistributedThrottle()`, `UsePostgreSqlStorage`, `IRetryDelayFactory`, `QueueOptions`, `AddOrUpdateRecurringJobAsync`, `GetInput<T>()`, `IJobContextAccessor`) and fixed parameter and option names (`AddRecurringJob(id:, timeZoneId:)`, circuit breaker `ConsecutiveFailuresThreshold`/`OpenDuration`, dashboard options).
  - Corrected behaviour descriptions: job filters (`context.Succeeded` is only set after the pipeline; filters are singletons), `AddNexJobJobs` (internal jobs are registered), dashboard requeue (same job, attempts reset), single dashboard authorization handler, retention (`RetentionFailed` governs failed jobs), telemetry tag and metric names, and the state diagram (no `Retried`/`DeadLetter` status).
  - Documented what the code really does for the broker triggers (job type precedence, idempotency key and failure handling per broker), appsettings-bindable options versus code-only options (retention is code-only), the dashboard settings page, and the `using` namespaces of each storage package.
  - Rewrote the testing guide so its examples run (they use a started host and fast retry delays) and added the v5.5.0 to v5.6.0 upgrade notes to the migration guide.
  - `IRecurringStorage`: the XML documentation of `DeleteRecurringJobAsync` and `ForceDeleteRecurringJobAsync` described the two methods the other way round; corrected (no behaviour change).
  - The `[Unreleased]` section no longer has a duplicated `### Added` heading.

- **`NexJob.MongoDB` — The `DateTimeOffset` serializer is no longer registered globally (Issue #263, step 2)**:
  - NexJob used to register a process-wide `DateTimeOffsetSerializer` (string representation), which silently changed how the host application serialized its own `DateTimeOffset` values. The same representation is now applied through a convention to NexJob's own documents only (jobs, recurring jobs, servers, execution logs). The stored format is unchanged (ISO 8601 string at `+00:00`), so **no data migration is needed** and old and new nodes can run together.
  - Applications that unknowingly depended on the old global registration can pass `keepLegacyGlobalDateTimeOffsetSerializer: true` to `AddNexJobMongoDB` for one release; the flag will be removed afterwards.
  - Storing BSON `DateTime` instead of strings is deliberately not part of this change: it would require migrating existing documents, because range filters on dates do not match string-typed values.

### Fixed

- **`NexJob` Core — Recurring jobs from `appsettings.json` follow the configuration on every start (Issue #261)**:
  - `RecurringJobRegistrar` used to skip an entry that already existed in storage, so changing a cron, queue or input in configuration had no effect after the first start. It now always upserts the definition; the storage keeps what an operator changed in the dashboard (cron override, paused, deleted).
  - The first run is now the next cron occurrence, like a job registered from code. Before, `NextExecution` was set one second in the past, so every configured job fired immediately on the first registration.
  - The time zone is resolved before anything is stored, so an invalid `TimeZoneId` fails that entry's registration with an error log instead of storing a job whose next run cannot be computed.
  - **Behaviour change:** configured jobs no longer run once at startup.

- **`NexJob.Kafka`, `NexJob.RabbitMQ`, `NexJob.Trigger.AzureServiceBus` — Transient enqueue failures no longer lose or dead-letter messages (Issue #265)**:
  - Enqueue failures are classified. **Permanent** ones (missing `nexjob.job_type`, malformed payload) can never succeed; **transient** ones (storage or network errors, timeouts) can.
  - Kafka used to commit the *next* record's offset after a failed enqueue when a dead-letter topic was configured, dead-lettering (and skipping) messages on any storage blip. It now retries the same record in place with a 1 s / 2 s / 5 s / ... / 30 s backoff and does not consume the next record meanwhile. A permanent failure goes to the dead-letter topic and is committed; without a topic it is logged at `Error` and committed so a poison message cannot block the partition (previously it was never committed and was redelivered forever).
  - RabbitMQ nacks a transient failure with `requeue: true` after a one-second pause (no hot loop) instead of `requeue: false`, which discarded the message. A permanent failure is still nacked without requeue.
  - Azure Service Bus abandons a transient failure so it is delivered again (the entity's `MaxDeliveryCount` decides when it is dead-lettered) instead of dead-lettering it immediately; permanent failures are still dead-lettered.
  - A failed Kafka offset commit after a successful enqueue is no longer treated as an enqueue failure.

- **`NexJob.RabbitMQ` — Only `MessageId` is used as the idempotency key (Issue #266)**:
  - The trigger used `CorrelationId`, falling back to a SHA-256 of the body, as the idempotency key. Messages sharing a correlation id (a whole order flow, a request/reply chain) or carrying identical bodies were silently deduplicated and acknowledged without running. The key is now `MessageId` when it is set and non-blank; otherwise there is no key and every delivery creates a job.
  - **Behaviour change:** publishers that relied on `CorrelationId` for deduplication must set a unique `MessageId`.

- **`NexJob.Kafka` — The idempotency key is the record position, not the message key (Issue #264)**:
  - The trigger used the Kafka message key as the job idempotency key, so every later record sharing a key with an active job (for example the same customer id) was silently dropped, and its offset committed. The key is now `kafka:{topic}:{partition}:{offset}`: redelivery of the same record is still deduplicated, and different records always produce different jobs.
  - **Behaviour change:** applications that relied on key-based deduplication must deduplicate in the job itself.

- **`NexJob.Redis` — Distributed throttle slots survive node crashes without leaking (Issue #267, part 2)**:
  - The single global counter (INCR/DECR with a one-hour TTL) is replaced by a sorted set of holders in `nexjob:throttle:holders:{resource}`. Each running job owns one entry with an expiry; the owning node refreshes it every `HeartbeatInterval`, and expired entries are dropped before every acquire (using the Redis clock).
  - A slot left by a crashed node is reclaimed after `3 x HeartbeatInterval` (90 s by default) instead of up to an hour. Releasing removes only the caller's own holder, so a node can never free another node's slot.
  - `DistributedThrottleTtl` is not deprecated: it now caps how long a single job may hold a slot (a slot older than that stops being refreshed).
  - **Rolling upgrade:** old nodes keep counting in the previous key, so the global limit can be exceeded until all nodes run this version.

- **`NexJob.Redis` — Dashboard queries and retention no longer scan the keyspace (Issue #262, part 2)**:
  - Every job is now listed in a `nexjob:index:all` sorted set (score = creation time), written atomically by the enqueue script and removed on delete and purge. `GetJobsAsync` without filters pages straight from the index (cost proportional to the page); filtered listing, `GetJobsByTagAsync`, `GetJobCatalogAsync` and `PurgeJobsAsync` walk the index with pipelined reads instead of `SCAN`ning every key.
  - Jobs stored before this version are indexed once, on first use, by an idempotent backfill guarded by a `nexjob:index:ready` marker. The same backfill fills the Succeeded/Failed sets, so the **upgrade note of part 1 no longer applies**: Succeeded/Failed totals are exact after the first metrics call.
  - Index entries whose job hash disappeared (for example after a crash between two writes) are skipped and removed as they are found.

- **All providers — Batch acknowledgment releases continuations (Issue #257)**:
  - `AcknowledgeAsync` and `AcknowledgeBatchAsync` now move every child waiting on the acknowledged parent from `AwaitingContinuation` to `Enqueued` in InMemory, PostgreSQL, SQL Server, MongoDB and Redis (SQL providers do it in the same transaction; Redis in the same Lua script). With `EnableBatchAcknowledgment = true`, `ContinueWith` children now run.
  - The startup warning and the documented limitation from the previous step are removed.

- **`NexJob` Core — `EnableBatchAcknowledgment` limitation with continuations is documented and warned about (Issue #257, documentation step)**:
  - Jobs acknowledged in a batch do not release their `ContinueWith` children. The wiki (`05-Continuations.md`, `11-Configuration-Reference.md`) and the option's XML documentation now say so, and `JobDispatcherService` logs one warning at startup when the option is enabled.
  - The provider-side fix (releasing children from `AcknowledgeAsync`/`AcknowledgeBatchAsync`) is still open in #257.

- **`NexJob` Core — Distributed throttle no longer busy-spins or over-releases slots (Issue #267, part 1)**:
  - A job waiting on a full `[Throttle]` used to retry in a tight `Task.Yield()` loop, hammering Redis with acquire calls and ignoring cancellation. It now backs off 250 ms plus 0-100 ms of jitter between attempts and observes the cancellation token.
  - `ThrottleRegistry` tracks the slots it really took from the distributed store, so a release only decrements the global counter for those. When the store is unavailable and the registry degrades to local-only throttling, releases no longer push the Redis counter down.
  - The holder-set redesign (crash-safe slots with TTL) is not part of this change.

- **`NexJob.MongoDB` — Dates are stored and compared in UTC (Issue #263, step 1)**:
  - MongoDB stores `DateTimeOffset` as an ISO string, and scheduling filters and sorts compare those strings, so a value written with a non-UTC offset (for example `-03:00`) was compared as the wrong instant: jobs scheduled or retried with a local offset ran hours early, and jobs with a positive offset ran late. Every date written for jobs, recurring jobs, servers and execution logs, and every date used in a filter or update, is now normalised to UTC.
  - Documents already stored with a non-UTC offset are not migrated and stay wrong until rewritten; only future-scheduled jobs created with a local offset are affected.
  - Step 2 (a BSON `DateTime` serializer and removing the global `DateTimeOffset` registration) is not part of this change.

- **`NexJob.Redis` — Metrics no longer scan every job hash (Issue #262, part 1)**:
  - `GetMetricsAsync` and `GetQueueMetricsAsync` (run every 15 s by each node's heartbeat and on every health probe) previously read the whole job keyspace. They now derive counts from structures that already exist: queue sorted sets (Enqueued), `nexjob:processing` (Processing), `nexjob:scheduled` (Scheduled), plus two new sorted sets, `nexjob:status:Succeeded` and `nexjob:status:Failed`, maintained by every path that finishes, deletes, purges or requeues a job. Recent failures come from the Failed set.
  - **Upgrade note:** jobs that finished before this version are not in the new sets, so `Succeeded`/`Failed` totals start at 0 and grow as jobs finish; older jobs drop out of nothing (they are simply not counted) and disappear from the store through normal retention. Enqueued, Processing and Scheduled are exact immediately.
  - Dashboard list queries (`GetJobsAsync` and similar) still scan; that is tracked for part 2.

- **`NexJob` Core — Recurring jobs fire once per occurrence and an invalid time zone no longer causes an enqueue storm (Issue #260)**:
  - `RecurringJobSchedulerService` re-reads the recurring job after taking the lock and only fires it if it is still due, so a second instance holding a stale due list no longer fires an occurrence another instance already fired.
  - The next execution is now computed before the job is enqueued. An unresolvable time zone or invalid cron logs an error and enqueues nothing, instead of enqueuing the job on every polling cycle.

- **`NexJob.Redis` — Due scheduled and retry jobs are promoted atomically (Issue #255)**:
  - Promotion used a client-side read/`HSET`/`ZREM`/`ZADD` sequence, so two nodes could both promote (and run) the same job, and a crash mid-way could lose it. It is now a single Lua script that removes the `nexjob:scheduled` entry first and only enqueues jobs still in `Scheduled` state; stale entries (missing hash, job already running) are just dropped.
  - Large backlogs drain in bounded batches of 100 per script call, up to 5 calls per fetch.

- **`NexJob.Redis` — Released continuations are queued atomically on commit (Issue #254)**:
  - Committing a parent successfully marked its `ContinueWith` children `Enqueued` but never added them to a queue, so they were never fetched. The commit script now moves every child still `AwaitingContinuation` into its queue ZSET in the same atomic step (children in any other state are left untouched) and deletes the continuation set.
  - Job hashes now store a precomputed `queueScore`; children written before this field existed fall back to priority and the commit time.

- **`NexJob` Core — Storage errors no longer fail successful jobs or kill the heartbeat (Issue #256)**:
  - A storage error while updating the heartbeat is logged as a warning and the loop keeps running, so a transient blip no longer lets the orphan watcher re-run a healthy job.
  - The success commit now runs outside the job failure path. If it fails it is retried up to 3 times (100 ms, 500 ms, 2 s); if it still fails the error is logged and the job stays `Processing` for the orphan watcher (at-least-once). It is never marked failed, retried by the policy, or dead-lettered because of it.
  - The dispatcher worker task logs any unhandled execution error instead of leaving an unobserved task exception.

- **`NexJob` Core — Graceful shutdown stops fetching and no longer burns attempts on interrupted jobs (Issue #259)**:
  - `JobDispatcherService` stops polling and fetching as soon as `StopAsync` begins, so no new job is claimed while the drain runs. Running jobs still receive the host stopping token only after `ShutdownTimeout` expires.
  - A job that throws `OperationCanceledException` while the shutdown token is cancelled is requeued immediately (`RetryAt = now`) without consuming its attempt and is never dead-lettered. An `OperationCanceledException` thrown without a shutdown request remains a normal failure.
  - Docs: `HostOptions.ShutdownTimeout` must be greater than `NexJobOptions.ShutdownTimeout` (see `docs/wiki/13-Best-Practices.md`).

- **`NexJob.Trigger.SalesforceStreaming` — OAuth token expiry and `ConnectTimeout` are honoured (Issue #230)**:
  - The cached OAuth token is no longer reused forever. It records `ExpiresAt` from the response `expires_in` (default 2 hours when missing or invalid) and is refreshed once it is within 60 seconds of expiry, matching `NexJob.Trigger.Salesforce`. `SalesforceStreamingTokenResult` gains an optional `ExpiresAt` init property; its constructor is unchanged.
  - `SalesforceStreamingTriggerOptions.ConnectTimeout` was ignored (the Bayeux `HttpClient` used a fixed 150 s). The client timeout is now `ConnectTimeout + 30 s`, so the default still yields 150 s.
  - `ConnectTimeout` must now be greater than zero; a zero or negative value fails options validation at startup.

- **`NexJob.Redis` — Idempotency key lives as long as the job (Issue #238)**:
  - The `nexjob:idempotency:{key}` key no longer expires after a fixed 7 days, which let `DuplicatePolicy.RejectAlways` enqueue the same key again while the job was still retained. It now has no TTL and is released together with the job by retention purge, `DeleteJobAsync` and `PurgeOnSuccess`, only while it still points to that job.
  - Keys created before this change keep their existing 7-day expiry.

- **`NexJob.Redis` — Enqueue is atomic: job hash and queue entry are created together (Issue #239)**:
  - `EnqueueAsync` now inserts the job id into its queue, the scheduled set or the parent's continuation set inside the same Lua script that creates the hash and idempotency key. A crash between the two steps can no longer leave an `Enqueued` job that is in no queue.

- **`NexJob.Redis` — Orphan requeue is atomic and no longer re-enqueues finished jobs (Issue #240)**:
  - `RequeueOrphanedJobsAsync` now decides in one Lua script: it requeues (or fails, when attempts are exhausted) only a job whose processing entry still has the heartbeat the scan read **and** whose status is still `Processing`.
  - Previously a job committed between the scan and the write was flipped back to `Enqueued` and could run twice; a stale processing entry of a finished job is now just removed.

- **`NexJob.Postgres`, `NexJob.SqlServer`, `NexJob.MongoDB` — `DuplicatePolicy.AllowAfterFailed` no longer drops the enqueue after a finished job (Issue #234, #176)**:
  - Enqueuing with the same idempotency key after the previous job reached `Succeeded`, `Failed` or `Expired` silently did nothing: the unique index covered every job, so the insert failed and the old job's id was returned as accepted. Default recurring jobs (`SkipIfRunning`) fired once and then stopped until the old job was purged.
  - The idempotency key is now unique only among **active** jobs (`Enqueued`, `Processing`, `Scheduled`, `AwaitingContinuation`). PostgreSQL and SQL Server get migration **V10** (drops the old index, creates the active-only unique index and a lookup index); MongoDB replaces the `idempotency_key` index at startup.
  - A concurrent-enqueue conflict now resolves to the **active** winner and retries if that job already finished; the duplicate pre-check looks at the latest job per key.
  - `RequeueJobAsync` now throws `InvalidOperationException` ("another active job already holds its idempotency key") instead of a raw database error when re-activating a finished job whose key is held by a newer active job.
  - `docs/wiki/17-Idempotency.md`: the `AllowAfterFailed` matrix now matches the code (allowed after every terminal state) and warns that the policy does not prevent duplicate side effects.

- **`NexJob.MongoDB` — `FetchNextAsync` and `FetchBatchAsync` now honor the queue order (Issue #235)**:
  - Queues are claimed in the order given, and inside a queue the highest job priority and oldest job win, matching PostgreSQL, SQL Server and InMemory. Each claim stays an atomic `FindOneAndUpdate`.
  - Previously a high-priority job in a later queue was returned before a normal job in the first queue.

- **`NexJob.SqlServer` — `FetchNextAsync` returns `null` for an empty queue list instead of throwing (Issue #236)**:
  - With no queues the generated `VALUES` list was empty and the call failed with `SqlException: Incorrect syntax near ')'`. It now returns `null` without touching the database, like `FetchBatchAsync` and the other providers.

- **`NexJob.Redis` — `PurgeJobsAsync` no longer leaks `nexjob:logs:{id}` keys and counts purged jobs exactly (Issue #233)**:
  - Retention purge now deletes each job's separate logs key together with its job hash, so memory no longer grows for jobs that were purged.
  - The returned count is the number of job hashes actually removed; it is no longer inflated when a delete reports zero.

- **`NexJob.MongoDB` — Orphan requeue no longer overwrites the original error message (Issue #232)**:
  - When an orphaned job has exhausted its attempts, `RequeueOrphanedJobsAsync` now sets "Orphaned execution exceeded maximum attempts." only if the job has no recorded error, matching PostgreSQL, SQL Server, Redis and InMemory.
  - Previously the real exception message from the last attempt was replaced by the generic text.

- **`NexJob` Core — InMemory `PurgeJobsAsync` no longer deletes `Failed` jobs early via `RetainDeadLetter` (Issue #231)**:
  - `Failed` jobs are now purged by `RetainDeadLetter` only when `RetainFailed` is zero, matching PostgreSQL, SQL Server and Redis.
  - Previously a `Failed` job still within `RetainFailed` could be deleted as soon as it exceeded the shorter `RetainDeadLetter`.

- **`NexJob.Postgres` — Job input `{}` / `null` was read back as an empty string, so the job failed to deserialize (Issue #237)**:
  - `PostgresJobRow.ToRecord()` now reports an empty payload only for `Succeeded` jobs (where `TrimPayloadOnSuccess` can have stripped it). Every other state returns the stored JSON unchanged.
  - Fixes jobs with an empty-record input, `IJob` without input, and no-input recurring jobs failing with `JsonException` on PostgreSQL.
  - Known limitation: a `Succeeded` job with a legitimate `{}` input is shown as "payload stripped" in the dashboard.

- **`NexJob.Postgres`, `NexJob.Redis`, `NexJob.MongoDB` — Storage Catalog and Payload Trimming Alignment (Issue #222)**:
  - Fixed PostgreSQL `TrimPayloadOnSuccess` to update `input_json = '{}'::jsonb`, eliminating PostgreSQL error `22P02: invalid input syntax for type json`.
  - Added `JobCatalogRow` mapping in `PostgresStorageProvider.GetJobCatalogAsync` to ensure reliable Dapper materialization into `JobCatalogItem`.
  - Implemented `GetJobCatalogAsync` in `RedisStorageProvider` by scanning `nexjob:jobs:*`, grouping by `(JobType, Queue)`, and aggregating run statistics (totals, successes, failures, last execution, and average duration).
  - Implemented `GetJobCatalogAsync` in `MongoStorageProvider` by querying and grouping jobs by `(JobType, Queue)` to calculate catalog metrics.


- **`NexJob.Dashboard` — Multi-Cluster Navigation & Action URL Preservation and Read-Only UI Guard**:
  - Ensured active cluster parameter (`?cluster={id}`) is systematically preserved across sidebar navigation, header logo, search bar, breadcrumbs, and pagination.
  - Implemented client-side navigation interceptor in `HtmlShell` that automatically propagates the active cluster ID across internal page transitions when browsing a non-default cluster.
  - Fixed form action URLs across `RecurringJobDetailPage`, `JobDetailPage`, `RecurringPage`, `FailedPage`, and `SettingsPage` to append and preserve the `?cluster={id}` query parameter, preventing mutations on remote clusters from inadvertently hitting the default cluster.
  - Enforced client-side UI `IsReadOnly` guards across all dashboard pages: mutating buttons (`Trigger Now`, `Pause`, `Force Delete`, `Requeue`, `Apply`, `Reset`, bulk actions) are cleanly hidden when viewing a read-only cluster and a `ReadOnlyBanner` is displayed.
  - Preserved active cluster parameter in log streaming and execution modal fetches.

- **`NexJob` Core — Worker Poisons Foreign Jobs on Shared Queue (Issue #201)**:
  - Introduced `ForeignJobTypeException` in `NexJob.Exceptions` thrown by `DefaultJobInvokerFactory` when a worker dequeues a job whose type or input type cannot be resolved in the local assembly/runtime.
  - Hardened `JobExecutor.ExecuteJobAsync` to catch `ForeignJobTypeException` separately from standard execution failures: the worker rolls back the attempt increment, defers the job with a configurable `ForeignJobRetryDelay` (default 5s) via `CommitJobResultAsync`, and avoids dead-lettering (`IDeadLetterDispatcher` is never invoked).
  - Added `ForeignJobRetryDelay` option to `NexJobOptions` (default 5s).
  - Added 3N unit testing matrix in `tests/NexJob.Tests/JobExecutorHardeningTests.cs` and `tests/NexJob.Tests/DefaultJobInvokerFactoryHardeningTests.cs`.

## [5.5.0] - 2026-09-25

### Fixed

- **`NexJob.SqlServer` — Non-blocking application lock for scheduled job promotion during concurrent batch polling**:
  - Guarded scheduled/retry job promotion in `FetchNextAsync` and `FetchBatchAsync` with non-blocking `sp_getapplock @Resource = 'nexjob_promote_scheduled', @LockTimeout = 0` and `ROWLOCK, READPAST`.
  - Prevents transaction lock contention and 1205 deadlock victim errors across parallel workers during intense concurrent bursts.

### Added

- **`NexJob` Core & `NexJob.SqlServer` — High-Throughput Dynamic Batch Fetching & Batch Acknowledgment**:
  - Implemented dynamic batch fetching in `JobDispatcherService` based on currently available worker capacity (`availableSlots = 1 + workerSlots.CurrentCount`), eliminating single-job roundtrip bottlenecks during high queue backlogs (issues #191, #192).
  - Extended `IJobStorage` with `FetchBatchAsync` and `AcknowledgeBatchAsync` with default interface implementations for 100% backward compatibility with external providers (issues #191, #192).
  - Implemented atomic `FetchBatchAsync` in `SqlServerStorageProvider` using `SELECT TOP (@maxBatchSize) ... WITH (UPDLOCK, READPAST)` (issue #191).
  - Added opt-in `EnableBatchAcknowledgment` in `NexJobOptions` using an asynchronous `Channel<JobId>` flusher to commit successful completions in batches, reducing SQL Server write roundtrips and log flushes by over 90% (issue #192).
  - Validated in a real-world load test with Apache Kafka + SQL Server (150,000 jobs): throughput increased from ~30 jobs/s to ~320-470 jobs/s (~10x to 15x speedup) with zero duplicate records and zero deadlocks (issues #191, #192).
- **`NexJob.Postgres` — High-Throughput Dynamic Batch Fetching & Batch Acknowledgment**:
  - Implemented atomic `FetchBatchAsync` in `PostgresStorageProvider` using `SELECT ... FOR UPDATE SKIP LOCKED LIMIT @maxBatchSize` and `RETURNING *` (issue #191).
  - Implemented vectorized `AcknowledgeBatchAsync` in `PostgresStorageProvider` using `WHERE id = ANY(@Ids)` to eliminate per-job WAL transaction log overhead (issue #192).

- **`NexJob.Storage` — Native Batch Processing for MongoDB, Redis, and InMemory Providers**:
  - Implemented atomic `FetchBatchAsync` and `AcknowledgeBatchAsync` in `InMemoryStorageProvider` with locked collection processing and zero allocations (issue #195).
  - Implemented high-throughput batching in `MongoStorageProvider` with atomic batch claims and vectorized `AcknowledgeBatchAsync` via `UpdateManyAsync` (issue #195).
  - Implemented server-side Lua scripts `FetchBatchScript` and `AcknowledgeBatchScript` in `RedisStorageProvider` allowing sub-millisecond atomic batch claims and single-roundtrip acknowledgments (issue #195).
  - Added full 3N unit and contract integration test coverage across all storage providers (issue #195).

## [5.4.1] - 2026-09-24

### Added

- **`NexJob.Triggers` — Complete `IListenerRegistry` Integration Across External Triggers**:
  - Integrated `IListenerRegistry` into `NexJob.Trigger.Salesforce` (`SalesforceTriggerHandler`), registering Pub/Sub API listener with `"Salesforce (Pub/Sub API)"` broker type and tracking operational status (`Starting`, `Listening`, `Reconnecting`, `Faulted`, `Stopped`) (issue #187).
  - Integrated `IListenerRegistry` into `NexJob.Trigger.SalesforceStreaming` (`SalesforceStreamingTriggerHandler`), tracking Bayeux streaming listener state (`Starting`, `Listening`, `Reconnecting`, `Stopped`) (issue #187).
  - Integrated `IListenerRegistry` into `NexJob.Trigger.AzureServiceBus` (`AzureServiceBusTriggerHandler`), reporting operational transitions and processor errors to the listener registry (issue #187).
  - Integrated `IListenerRegistry` into `NexJob.Trigger.AwsSqs` (`AwsSqsTriggerHandler`), reporting SQS polling loop lifecycle, immediate startup faults, and transient network reconnections (issue #187).
  - Integrated `IListenerRegistry` into `NexJob.Trigger.GooglePubSub` (`GooglePubSubTriggerHandler`), tracking subscriber client lifecycle and immediate startup faults (issue #187).
  - Preserved 100% backward binary compatibility with optional `IListenerRegistry? listenerRegistry = null` constructor parameters across all trigger packages (issue #187).
  - Added complete 3N testing matrix (Positive happy path, Negative failure/fault handling, and Boundary null registry) to each trigger unit test project (issue #187).

## [5.4.0] - 2026-09-23

### Added

- **`NexJob.Dashboard` — Modern Enterprise Layout, Multi-Theme Switcher, Cluster Topology & Real-Time Controls (Maxton-Inspired)**:
  - Implemented an executive 64px Top Header with collapsible sidebar toggle (hamburger ☰), global search bar with keyboard shortcut (`Ctrl + K`), real-time cluster health status badge (`HEALTHY`, `DEGRADED`, `INCIDENT`), theme customizer trigger (🎨), and quick external links (issue #184).
  - Implemented an Offcanvas Theme Customizer drawer supporting 5 distinct themes: `Blue Theme` (Midnight - default), `Dark`, `Light`, `Semi-Dark` (dark sidebar/header with light content), and `Bordered` (clean 1px high-contrast borders without heavy shadows), fully persisted in `localStorage` (issue #184).
  - Re-structured sidebar navigation into organized categories (`MONITORING`, `EXECUTION`, `SYSTEM`) with counter badges and responsive collapsed mini-sidebar mode (issue #184).
  - Implemented visual **Cluster Pipeline Topology Map** in native SVG and animated CSS flowchart connecting Ingress/Triggers ➔ Queue Buffers ➔ Processing Worker Nodes with live activity indicators (issue #184).
  - Implemented real-time **SSE Live Log Streaming** on `/jobs/{id}` terminal viewer, dynamically appending log entries during execution without page refreshes (issue #184).
  - Added quick time-window filters (`1h`, `6h`, `24h`, `7d`, `All Time`) on `/jobs` search filter bar (issue #184).
  - Added programmatic and interactive queue pause/resume controls directly in `/queues` view with immediate status synchronization (issue #184).
  - Modernized job execution logs and payload viewers into terminal-styled code windows with header dots, syntax color tokens, and 1-click clipboard copy buttons (issue #184).
  - 100% self-contained in native CSS and vanilla JS — zero external NPM or CDN dependencies (issue #184).
  - Added full 3N testing matrix (Positive/Header & Drawer, Cluster Topology rendering, Period filtering, Negative/Resilient Shell on 404, Boundary/Null & Empty inputs) in `StandaloneDashboardTests` (issue #184).

- **`NexJob.Dashboard` & Triggers — Active Event Triggers & Listeners Visibility**:
  - Implemented `IListenerRegistry` and `DefaultListenerRegistry` in Core to register and track active event triggers and broker listeners in a thread-safe manner (issue #182).
  - Added connection lifecycle tracking (`Starting`, `Listening`, `Reconnecting`, `Faulted`, `Stopped`) in `RabbitMqTriggerHandler` and `KafkaTriggerHandler` without impacting the hot-path execution pipeline (issue #182).
  - Added dedicated `/listeners` page in `NexJob.Dashboard` rendering broker types, target queues/topics, consumer groups, mapped job types, uptime, error diagnostic details, and direct filtering links to generated jobs (issue #182).
  - Added `Listeners` status badge to sidebar navigation and an Event Listeners summary widget to the dashboard Overview page (issue #182).
  - Added complete 3N unit testing matrix covering positive registration/rendering, negative broker connection failure/reconnection, and invalid input/empty state handling (issue #182).

## [5.3.0] - 2026-09-21

### Added

- **`NexJob.Kafka` — Custom `ConsumerConfig` and `ProducerConfig` Support**:
  - Added `ConfigureConsumer` delegate to `KafkaTriggerOptions` (`Action<ConsumerConfig>`), allowing custom SASL/SSL authentication, custom certificates (both file paths via `SslCaLocation`/`SslCertificateLocation` and raw PEM strings via `SslCaPem`/`SslCertificatePem`/`SslKeyPem`), custom timeouts, and advanced Kafka consumer tuning without global environment variables (issue #179).
  - Added `ConfigureProducer` delegate to `KafkaProducerOptions` (`Action<ProducerConfig>`), allowing identical SASL/SSL credentials, TLS certificates, and broker tuning on the resilient Kafka outbox producer (issue #179).
  - Propagated consumer security configuration to internal dead-letter producer in `ConfluentKafkaConsumer`, ensuring dead-letter forwarding honors broker TLS/SASL settings (issue #179).
  - Preserved critical invariants: `EnableAutoCommit` is strictly enforced to `false` on consumer trigger, and options values (`BootstrapServers`, `GroupId`, `Acks`, `EnableIdempotence`) take precedence over delegate overrides (issue #179).
  - Added 3N testing matrix (Positive/SSL+Certs, Negative/Invariant enforcement, Boundary/Null) in both `KafkaTriggerTests` and `KafkaProducerTests` (issue #179).

## [5.2.0] - 2026-09-20

### Added

- **Storage & Job Retention — Dead-Letter Retention & Batched Chunked Purging**:
  - Added configurable `RetentionDeadLetter` to `NexJobOptions` and `RetentionPolicy` (default 60 days) to prevent unbounded accumulation of dead-letter jobs (issue #145).
  - Added configurable `RetentionBatchSize` to `NexJobOptions` and `BatchSize` to `RetentionPolicy` (default 1,000 rows) with runtime override support via `IRuntimeSettingsStore` (issue #145).
  - Implemented batched/chunked deletion loops in `PurgeJobsAsync` across all storage providers (PostgreSQL, SQL Server, Redis, MongoDB, InMemory) to prevent lock escalation, WAL/transaction log bloat, and replication lag during retention cleanup cycles (issue #145).
  - Added dead-letter retention configuration card and runtime controls to Dashboard (`SettingsPage`) and API endpoint `/settings/retention` (issue #145).
  - Added 3N unit testing matrix (positive, negative, boundary/fallback) and integration tests covering dead-letter retention and batched purging (issue #145).

- **Consumer-Driven Triggers & Idempotency Hardening**:
  - Implemented consumer-driven job mapping across all broker triggers (`NexJob.Kafka`, `NexJob.RabbitMQ`, `NexJob.Trigger.AzureServiceBus`, `NexJob.Trigger.GooglePubSub`, `NexJob.Trigger.AwsSqs`) allowing subscribers to bind explicit job types without requiring publishers to inject `nexjob.job_type` headers (issue #163).
  - Added generic registration overloads `.Add{Broker}Trigger<TJob>()` on `NexJobBuilder` and `IServiceCollection` across Kafka, RabbitMQ, Azure Service Bus, Google Pub/Sub, and AWS SQS (issue #163).
  - Implemented configurable `JobType` on `KafkaTriggerOptions`, `RabbitMqTriggerOptions`, `AzureServiceBusTriggerOptions`, and `GooglePubSubTriggerOptions` with strict precedence hierarchy: (1) message header/attribute if present, (2) configured `JobType`, (3) error/dead-letter if neither is present (issue #163).
  - Hardened `NexJob.RabbitMQ` idempotency key resolution with deterministic SHA-256 payload hashing (`Convert.ToHexString(SHA256.HashData(body))`) when both `CorrelationId` and `MessageId` are omitted by external producers, preventing duplicate executions across broker redeliveries (issue #163).
  - Added comprehensive 3N unit test coverage across all affected trigger packages (precedence, fallback, invalid input/whitespace, and DI registration) (issue #163).

- **`NexJob.StressTests`**:
  - Implemented production load and stress testing suite in `tests/NexJob.StressTests` targeting high-concurrency storage and trigger backpressure scenarios (issue #159).
  - Implemented `PostgresStorageStressTests` executing 3,000 jobs under 20-worker contention against PostgreSQL (`NpgsqlDataSource`), asserting zero deadlocks (`40P01`), connection pool stability, and 100% completion (issue #159).
  - Implemented `RedisStorageStressTests` executing 3,000 jobs concurrently under Redis multiplexer load, asserting zero connection drops and consistent status transitions (issue #159).
  - Implemented `TriggerBackpressureStressTests` asserting bounded memory and strict prefetch limit enforcement under artificial storage write slowdown (issue #159).
  - Created dedicated on-demand GitHub Actions stress testing workflow `.github/workflows/stress-tests.yml` with containerized PostgreSQL and Redis services (issue #159).

- **`NexJob.Benchmarks`**:
  - Implemented `StorageProviderLatencyBenchmark` comparing enqueue latency across InMemory, Redis, and PostgreSQL providers via `IScheduler` (issue #159).
  - Parameterized single-enqueue latency benchmarks by payload size (`PayloadBytes: 0, 1024, 10240`) to evaluate serialization scaling with `System.Text.Json` vs `Newtonsoft.Json` (issue #157).
  - Implemented `ConcurrentEnqueueBenchmark` measuring multi-threaded enqueue throughput and lock contention across varying parallelism levels (`ConcurrencyLevel: 10, 50`) (issue #157).
  - Implemented `DispatchLatencyBenchmark` isolating and measuring wake-up channel dispatch latency from enqueue to execution (issue #157).
  - Added comprehensive `benchmarks/NexJob.Benchmarks/README.md` detailing benchmark suites, execution commands, and verified .NET 8 results (issue #157).

- **Samples & Reference Architecture**:
  - Comprehensive modernization of all existing samples and addition of reference projects for all plugins, brokers, and storage topologies (issue #128):
    - `NexJob.Sample.MinimalApi`: Modernized to .NET 8 Minimal APIs with deadline enforcement, segregated `IDashboardStorage`, and dead-letter handling.
    - `NexJob.Sample.WebApi`: Modernized with dual storage (InMemory / PostgreSQL), REST endpoints for job lifecycle management, recurring jobs, and `.http` test definitions.
    - `NexJob.Sample.WorkerService`: Headless console Worker Service demonstrating embedded standalone HTTP dashboard server and graceful shutdown.
    - `NexJob.Sample.ConfiguredRecurring`: Clean declarative recurring job schedules from `appsettings.json` with timezone support.
    - `NexJob.Sample.RabbitMQ`: Production outbox producer and trigger consumer demonstrating the 5 trigger guarantees and automatic acks.
    - `NexJob.Sample.Kafka`: Partitioned event publishing via Outbox and consumer trigger with offset tracking, plus direct Kafka consumer ingestion pipeline (`SaveCustomerJob`) persisting to Microsoft SQL Server with node telemetry and simulation endpoints.
    - `NexJob.Sample.Storage`: Enterprise storage topology with PostgreSQL primary write path, isolated PostgreSQL read replica (`UseDashboardReadReplica`), Redis distributed throttle (`UseDistributedThrottle`), OpenTelemetry instrumentation, and custom pipeline filters (`IJobExecutionFilter`).
    - `NexJob.Sample.CloudTriggers`: Unified cloud consumer triggers covering AWS SQS, Azure Service Bus, Google Cloud Pub/Sub, and Salesforce (gRPC CDC and CometD Streaming), including interactive simulation endpoints.
    - `samples/docker-compose.yml`: Ready-to-run local infrastructure stack with PostgreSQL 16, Redis 7, RabbitMQ 3.13 Management, Kafka KRaft, and Microsoft SQL Server 2022.
    - `samples/README.md`: Centralized catalog documentation with architecture matrix, quickstart commands, and scenario guides.

- **`NexJob.Postgres`**:
  - Implemented `AddNexJobPostgres(this IServiceCollection services, NpgsqlDataSource dataSource)` registration overload enabling reuse of pre-configured application data sources with connection pooling and telemetry (issue #148).
  - Implemented `IDisposable` and `IAsyncDisposable` on `PostgresStorageProvider` with explicit ownership tracking (`ownsDataSource`), safely managing lifecycle for internal vs externally injected data sources (issue #148).

- **`NexJob.Telemetry`**:
  - Implemented standard OpenTelemetry `ObservableGauge` instruments: `nexjob.queue.depth` (tagged with `nexjob.queue`), `nexjob.workers.active`, and `nexjob.workers.total` for Kubernetes HPA and Prometheus autoscaling (issue #146, PR #153).
  - Background asynchronous metric sampling via `ServerHeartbeatService` polling `IDashboardStorage.GetQueueMetricsAsync` without impacting execution hot path.
  - Thread-safe, non-allocating atomic worker observation in `JobDispatcherService` via `Volatile.Read` and `NexJobMetrics.SyncLock`.
  - Comprehensive unit test suite with 3N matrix covering gauges, provider registration, exception handling, and tag assertions (`tests/NexJob.Tests/NexJobMetricsTests.cs`).
  - Documented metric instruments and scrape semantics in `docs/wiki/12-OpenTelemetry.md`.

- **Documentation**:
  - Added dedicated, comprehensive `README.md` files for all storage provider packages (`NexJob.Postgres`, `NexJob.SqlServer`, `NexJob.MongoDB`, `NexJob.Redis`) and dashboard packages (`NexJob.Dashboard`, `NexJob.Dashboard.Standalone`) (PR #137).
  - Added dedicated `README.md` for `NexJob.Trigger.GooglePubSub`.

- **Engineering Governance**:
  - Added disciplined `nexjob-task-cycle` agent skill with technical grooming, 3N test matrix, boundary enforcement, and automated verification gates (commit `eb1b91a`, PR #138).
  - Added comprehensive 3N unit test suite for Azure Service Bus trigger (`tests/NexJob.Trigger.AzureServiceBus.Tests`).

### Fixed

- **Host Shutdown Cooperative Cancellation Propagation (`NexJob`)**:
  - Propagated the host stopping token from `JobDispatcherService` to `JobExecutor.ExecuteJobAsync(job, stoppingToken)` so in-flight jobs observe host cancellation during graceful drain (issue #144).
  - Linked `JobExecutor` internal CTS with the host stopping token via `CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)`, ensuring in-flight jobs observe `cancellationToken.IsCancellationRequested == true` and cooperatively cancel before process termination (issue #144).
  - Updated `JobDispatcherService.StopAsync` to link the host's `cancellationToken` with `ShutdownTimeout`, guaranteeing immediate cancellation propagation and clean drain if host termination is forced (issue #144).
  - Added unit and end-to-end tests verifying that triggering host shutdown cancels active jobs cooperatively (`tests/NexJob.Tests/JobExecutorTests.cs`, `tests/NexJob.Tests/GracefulShutdownTests.cs`) (issue #144).

- **`NexJob.StressTests`**:
  - Configured `AllowAdmin = true` on Redis connection multiplexer for `FlushDatabaseAsync` during test initialization.
  - Isolated static job execution counters into distinct `PostgresStressJob` and `RedisStressJob` types and added `[Collection("StressTests")]` to prevent state collision under parallel test execution.

- **`NexJob.Dashboard`**:
  - Resolved `System.InvalidOperationException: The current thread is not associated with the Dispatcher` when rendering Blazor pages with asynchronous network storage providers (PostgreSQL, SQL Server, MongoDB) by preserving Dispatcher execution context during component parameter loading (issue #128).

- **Core Storage Providers**:
  - Prevented infinite requeue loops for orphaned poison-pill jobs when retry attempts are exhausted in `OrphanedJobWatcherService` across all 5 storage providers (`InMemoryStorageProvider`, `PostgresStorageProvider`, `SqlServerStorageProvider`, `MongoJobStorage`, `RedisJobStorage`) (issue #143, PR #149).
  - Eliminated banned `.Result` sync-over-async invocations in `InMemoryStorageProvider` (commit `fada371`).

- **`NexJob.Trigger.AwsSqs`**:
  - Resolved message visibility timeout flakiness and delays on enqueue failures by immediately invoking `ChangeMessageVisibilityAsync(VisibilityTimeout = 0)` (issue #141, PR #150).

- **CI Test Suite**:
  - Included missing integration test suites (RabbitMQ, Salesforce) and broadened regex filters in `.github/workflows/ci.yml` (issue #139, PR #152).

### Changed

- **Documentation (`README.md`)**:
  - Synchronized package versions across the entire ecosystem to `v5.1.0` (issue #157).
  - Added `NexJob.Trigger.SalesforceStreaming` and updated storage provider package identifiers (issue #157).
  - Updated benchmark table with verified .NET 8 RyuJIT measurements (NexJob 13.35 µs / 2.10 KB vs Hangfire 35.95 µs / 11.20 KB) (issue #157).
  - Expanded project roadmap through releases v3.0.0, v4.0.0, and v5.1.0 (issue #157).
  - Added catalog reference table and guide to the modernized `samples/` directory (issue #157).

- **`NexJob.Postgres`**:
  - `PostgresStorageProvider` now builds and manages an internal `NpgsqlDataSource` by default instead of instantiating raw `NpgsqlConnection` per operation, leveraging Npgsql 7/8+ connection pooling, prepared statement caching, and automatic multi-host failover (issue #148).

- **Health Checks**:
  - Made health check timeout (`HealthCheckTimeout`, default 3s) and dead-letter failure threshold (`HealthCheckFailedThreshold`, default 10) configurable via `NexJobOptions` and `NexJobSettings` (issue #147, PR #151).

- **`NexJob.Trigger.AwsSqs`**:
  - Standardized trigger handler and extension naming across SQS packages (commit `107b5bf`).

## [5.1.0] - 2026-09-17

### Added

- **`NexJob.Trigger.SalesforceStreaming`**:
  - Implemented Salesforce Streaming API trigger supporting the CometD/Bayeux protocol over HTTP long-polling (issue #133).
  - Compatible with PushTopic (`/topic/*`), Change Data Capture (`/data/*`), Platform Events (`/event/*`), and Generic Streaming (`/u/*`) channels.
  - Multi-authentication support: OAuth 2.0 Username-Password flow, OAuth 2.0 Client Credentials flow, and direct Session ID / Bearer token.
  - Bayeux protocol client (`ISalesforceBayeuxClient`) handling handshake, replay extension subscription, long-polling connect loop, and graceful disconnect.
  - Resilient Replay ID checkpointing via `IStreamingReplayIdStore`, including atomic file-based persistence (`FileStreamingReplayIdStore`) and in-memory store (`InMemoryStreamingReplayIdStore`).
  - Automatic session recovery and re-handshake on `403::Unknown client` / session expiry with exponential backoff on connection drops.
  - All 5 Trigger Guarantees: at-least-once delivery, broker-native idempotency keys, W3C `traceparent` context propagation, automatic dispatcher wake-up signaling, and replay ID commits strictly after successful enqueue.
  - Fluent registration extensions on `IServiceCollection` and `NexJobBuilder` (`AddSalesforceStreamingTrigger<TJob>` and `AddNexJobSalesforceStreamingTrigger`).
  - Comprehensive unit test suite with 3N matrix achieving 90.1% line coverage (`tests/NexJob.Trigger.SalesforceStreaming.Tests`).
  - Complete documentation in `src/NexJob.Trigger.SalesforceStreaming/README.md` and `docs/wiki/19-Triggers.md`.

### Fixed

- **CI/Publishing**:
  - Included `NexJob.Trigger.SalesforceStreaming` in packaging and NuGet publication workflows (PR #136).

## [5.0.0] - 2026-09-17

### Added

- **`NexJob.Trigger.Salesforce`**:
  - Implemented Salesforce Pub/Sub API trigger consuming Change Data Capture (CDC) events and custom Platform Events over bidirectional gRPC streams.
  - Apache Avro schema caching (`ISalesforceSchemaService`) and binary payload decoding to JSON.
  - Resilient Replay ID checkpointing via `IReplayIdStore`, including atomic file-based persistence (`FileReplayIdStore`) and memory store (`InMemoryReplayIdStore`).
  - Replay fallback policies (`ReplayFallbackPolicy.FailFast`, `ResetToLatest`, `ResetToEarliest`) handling expired retention offsets.
  - Automatic OAuth2 client credentials token provider (`SalesforceTokenProvider`) with thread-safe caching and proactive refresh.
  - Fluent registration extensions on `NexJobBuilder` and `IServiceCollection` (`AddSalesforceTrigger<TJob>` and `AddSalesforceTrigger`).
  - Unit tests (`tests/NexJob.Trigger.Salesforce.Tests`, 85 tests) and in-process mock Kestrel gRPC/OAuth integration tests (`tests/NexJob.Trigger.Salesforce.IntegrationTests`).
  - Complete documentation in `src/NexJob.Trigger.Salesforce/README.md` and `docs/wiki/19-Triggers.md`.

- **`NexJob.RabbitMQ` (Unified Package)**:
  - Added resilient RabbitMQ Outbox Producer enabling durable message publishing backed by NexJob storage, Publisher Confirms, retries with backoff, and dead-letter handling.
  - `RabbitMqProducerOptions`: Configuration options with DataAnnotations validation and `ValidateOnStart()` fail-fast startup.
  - `IRabbitMqProducerClient` & `RabbitMqProducerClient`: Singleton producer client with thread-safe channel management and Publisher Confirms (`ConfirmSelect` / `WaitForConfirms`).
  - `RabbitMqProducerJob`: Durable `IJob<RabbitMqPublishPayload>` background job with W3C `traceparent` context propagation and custom headers injection.
  - `scheduler.EnqueueRabbitMqAsync`: Unified scheduling extensions supporting strongly typed objects `<T>` (serialized via `System.Text.Json`), raw strings, and raw byte arrays.
  - `scheduler.EnqueueRabbitMqRawAsync`: Dedicated overloads for explicit raw string and binary byte array publishing.
  - Fluent registration extensions on `NexJobBuilder` and `IServiceCollection`: `AddRabbitMqProducer` and `AddRabbitMqTrigger`.
  - `tests/NexJob.RabbitMQ.IntegrationTests`: End-to-end integration testing with real RabbitMQ broker via Testcontainers.
  - Complete documentation: `src/NexJob.RabbitMQ/README.md`, `docs/wiki/21-RabbitMQ.md`, and 12-Factor App guidelines in `docs/wiki/11-Configuration-Reference.md`.

- **`NexJob.Kafka` (Unified Package)**:
  - Added resilient Kafka Outbox Producer enabling durable message publishing backed by NexJob storage, retries with jitter, and dead-letter handling.
  - `KafkaProducerOptions`: Broker options with DataAnnotations validation and `ValidateOnStart()` fail-fast startup.
  - `IKafkaProducerClient` & `ConfluentKafkaProducerClient`: Singleton producer client with automatic graceful flush on application shutdown.
  - `KafkaProducerJob`: Durable `IJob<KafkaPublishPayload>` background job with OpenTelemetry `traceparent` context propagation and custom headers injection.
  - `scheduler.EnqueueKafkaAsync`: Unified scheduling extensions supporting strongly typed objects `<T>` (serialized via `System.Text.Json`), raw strings, and raw byte arrays.
  - `scheduler.EnqueueKafkaRawAsync`: Dedicated overloads for explicit raw string and binary byte array publishing.
  - Fluent registration extensions on `NexJobBuilder` and `IServiceCollection`: `AddKafkaProducer` and `AddKafkaTrigger`.
  - `tests/NexJob.Kafka.IntegrationTests`: Integration testing with real Kafka broker via Testcontainers.
  - Complete documentation: `src/NexJob.Kafka/README.md`, `docs/wiki/20-Kafka.md`, and 12-Factor App / Docker / Kubernetes guidelines in `docs/wiki/11-Configuration-Reference.md`.

### Changed

- **Package Renaming**:
  - Renamed `NexJob.Trigger.RabbitMQ` to `NexJob.RabbitMQ` (`NexJob.RabbitMQ.csproj`), unifying consumer triggers and outbox producer into a single first-class integration package.
  - Renamed `NexJob.Trigger.Kafka` to `NexJob.Kafka` (`NexJob.Kafka.csproj`), unifying consumer triggers and outbox producer into a single first-class integration package.

## [4.0.1] - 2026-09-17

### Security

- Resolved transitive vulnerabilities across solution:
  - `NexJob.Trigger.AzureServiceBus`: pinned `System.Text.Json` to `8.0.5` (fixes GHSA-8g4q-xg66-9fp4).
  - `NexJob.MongoDB`: updated `SharpCompress` to `0.48.0` (fixes GHSA-6c8g-7p36-r338) and `Snappier` to `1.3.1` (fixes GHSA-pggp-6c3x-2xmx).
  - Integration test suites: updated `SSH.NET` to `2026.0.0` (fixes GHSA-q939-rpr3-3284).

### Fixed

- Pinned broker package dependencies in `NexJob.Trigger.AzureServiceBus` (`7.18.2`), `NexJob.Trigger.GooglePubSub` (`3.18.0`), and `NexJob.Trigger.Kafka` (`2.14.0`) to avoid transitive package downgrade errors (`NU1605`) on .NET 8 SDKs.

### Documentation

- Documented `NexJob.Dashboard` and `NexJob.Dashboard.Standalone` packages in `README.md` and wiki.
- Clarified mandatory `builder.Services.AddMemoryCache()` requirement and troubleshooting instructions for dashboard setup.

## [4.0.1] - 2026-09-17

### Security

- Resolved transitive vulnerabilities across solution:
  - `NexJob.Trigger.AzureServiceBus`: pinned `System.Text.Json` to `8.0.5` (fixes GHSA-8g4q-xg66-9fp4).
  - `NexJob.MongoDB`: updated `SharpCompress` to `0.48.0` (fixes GHSA-6c8g-7p36-r338) and `Snappier` to `1.3.1` (fixes GHSA-pggp-6c3x-2xmx).
  - Integration test suites: updated `SSH.NET` to `2026.0.0` (fixes GHSA-q939-rpr3-3284).

### Fixed

- Pinned broker package dependencies in `NexJob.Trigger.AzureServiceBus` (`7.18.2`), `NexJob.Trigger.GooglePubSub` (`3.18.0`), and `NexJob.Trigger.Kafka` (`2.14.0`) to avoid transitive package downgrade errors (`NU1605`) on .NET 8 SDKs.

### Documentation

- Documented `NexJob.Dashboard` and `NexJob.Dashboard.Standalone` packages in `README.md` and wiki.
- Clarified mandatory `builder.Services.AddMemoryCache()` requirement and troubleshooting instructions for dashboard setup.

## [4.0.1] - 2026-09-17

### Security

- Resolved transitive vulnerabilities across solution:
  - `NexJob.Trigger.AzureServiceBus`: pinned `System.Text.Json` to `8.0.5` (fixes GHSA-8g4q-xg66-9fp4).
  - `NexJob.MongoDB`: updated `SharpCompress` to `0.48.0` (fixes GHSA-6c8g-7p36-r338) and `Snappier` to `1.3.1` (fixes GHSA-pggp-6c3x-2xmx).
  - Integration test suites: updated `SSH.NET` to `2026.0.0` (fixes GHSA-q939-rpr3-3284).

### Fixed

- Pinned broker package dependencies in `NexJob.Trigger.AzureServiceBus` (`7.18.2`), `NexJob.Trigger.GooglePubSub` (`3.18.0`), and `NexJob.Trigger.Kafka` (`2.14.0`) to avoid transitive package downgrade errors (`NU1605`) on .NET 8 SDKs.

### Documentation

- Documented `NexJob.Dashboard` and `NexJob.Dashboard.Standalone` packages in `README.md` and wiki.
- Clarified mandatory `builder.Services.AddMemoryCache()` requirement and troubleshooting instructions for dashboard setup.

## [4.0.0] - 2026-04-21

### Fixed

- `MigrationPipeline` now throws `InvalidOperationException` on an incomplete migration chain instead of silently returning a partial result — previously masked broken migration sequences at startup.
- `JobInvoker` disposes `IServiceScope` on `PrepareAsync` failure — eliminates scope leak when job type resolution or payload deserialization fails before execution begins.
- `GooglePubSubTrigger.StartAsync` now detects an immediate subscriber fault via `Task.Yield()` + `IsFaulted` check and propagates it before returning — the host no longer considers startup successful when the subscriber fails synchronously. `StopAsync` continues to observe `_runTask` for late faults.
- `AwsSqsTrigger.StartAsync` now detects an immediate polling loop fault via `Task.Yield()` + `IsFaulted` check and propagates it before returning — startup failures (invalid queue URL, missing credentials) are no longer invisible to the host. `Task.Run` token changed to `CancellationToken.None` so the loop is not cancelled by the startup token; shutdown remains controlled by `_stoppingCts`. `StopAsync` continues to surface faults from the polling task.
- `RecurringJobService` now acquires the distributed lock before enqueuing due jobs — prevents duplicate recurring job firings on multi-instance deployments where the scheduler tick fires simultaneously.
- `JobDispatcherService` adds a 5-second back-off on the error path to prevent a hot polling loop when storage is unavailable.
- `ServerHeartbeatService.StartAsync` downgrades the registration failure from `LogError` to `LogWarning` and explicitly describes the degraded mode: jobs execute but the instance does not appear in dashboard or cluster tracking.
- `InMemoryStorageProvider.FindExistingJobByKey` now uses the idempotency index instead of a full scan; nested lock in `CommitJobResultAsync` removed.

### Changed

- `DefaultScheduler`: extracted `CommitEnqueueAsync` to eliminate 8× copy-pasted post-enqueue flow across all storage branches.
- `JobTypeResolver`: removed dead `try/catch`, eliminated spurious `async` state machines, fixed `ToList` inside hot loop.
- `DefaultDeadLetterDispatcher`: handler resolution no longer uses reflection — replaced with a compiled expression cache, removing reflection from the execution hot path.
- `InMemoryStorageProvider`: extracted `ResolveDuplicate` helper to consolidate 6× copy-pasted duplicate-policy branching.
- `DashboardController`: extracted `HandleActionsAsync` into focused private methods per action group.
- `JobDispatcherService`: worker slot release centralised via `slotTransferred` flag — eliminates duplicate release risk on the fast-exit path.
- `RecurringJobService`: removed misleading `CalculateNextExecution` wrapper, inline sentinel at the single call site.
- `RedisStorageProvider.EnqueueAsync`: replaced recursive retry with an explicit bounded loop.
- `CommitJobResultAsync` (PostgreSQL, SQL Server, MongoDB, Redis): flattened with guard clauses and private state helpers to match `InMemoryStorageProvider` structure.

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
