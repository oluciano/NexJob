# Migration

Breaking changes, API updates, and schema migration between NexJob versions.

## v5.6.0 → v5.6.1

| Area | What changed | What to do |
|---|---|---|
| Broker triggers (SQS, Service Bus, Pub/Sub, Kafka, RabbitMQ) | The message body now reaches `IJob<string>` verbatim as text. Before, only a body that was itself a JSON string literal executed; any other body failed the job. | If you worked around it by publishing a JSON string literal (for example `"hello"` with quotes), the job now receives the quotes as part of the text. Publish the plain text instead. |

## v5.5.0 → v5.6.0

> This section covers what to check when upgrading; the [Changelog](../../CHANGELOG.md) is the complete list of changes.

### Behaviour changes to check before upgrading

| Area | What changed | What to do |
|---|---|---|
| **Kafka trigger** | The job idempotency key is `kafka:{topic}:{partition}:{offset}`, no longer the message key. Records that share a key used to be silently dropped; now each becomes a job. | If you relied on the message key to deduplicate, deduplicate inside the job. |
| **RabbitMQ trigger** | The idempotency key is `MessageId` only. `CorrelationId` and the body hash are no longer used; without a `MessageId` there is no deduplication. | Set a unique `MessageId` when publishing if you need deduplication. |
| **Kafka / RabbitMQ / Azure Service Bus triggers** | Transient enqueue failures (storage down, timeouts) are retried, requeued or abandoned instead of being dead-lettered or lost. Kafka retries the same record in place; a message that can never be enqueued is dead-lettered (or, without a DLT topic, logged and committed). | Configure a dead-letter topic on Kafka if you cannot afford to skip a poison message. |
| **Recurring jobs in `appsettings.json`** | The configuration is applied on every start and a first-time job runs at its next cron occurrence, no longer immediately. | Nothing to change; configured jobs no longer run once at every startup. Retention is still code-only. |
| **Graceful shutdown** | The dispatcher stops fetching as soon as shutdown begins. A job cancelled by shutdown is requeued without consuming an attempt. | Make sure `HostOptions.ShutdownTimeout` is larger than `NexJobOptions.ShutdownTimeout` (see [Best Practices](13-Best-Practices.md#graceful-shutdown)). |
| **`EnableBatchAcknowledgment`** | Batch acknowledgment now releases continuations (`ContinueWith` children) like the default path. | Nothing to change. |
| **MongoDB** | NexJob no longer registers a global `DateTimeOffset` serializer; dates are stored as UTC strings. | If your application relied on the old global registration, pass `keepLegacyGlobalDateTimeOffsetSerializer: true` to `AddNexJobMongoDB` for this release. |
| **Redis distributed throttle** | Slots are per-job entries under `nexjob:throttle:holders:{resource}` that expire if their node stops refreshing them. `DistributedThrottleTtl` now caps how long one job may hold a slot. | During a rolling upgrade, old nodes keep counting in the previous key, so the global limit can be exceeded until every node is upgraded. |
| **Redis dashboard queries** | A job index (`nexjob:index:all`) is built once, on first use, for jobs that already exist. The first dashboard or metrics call after the upgrade scans the keyspace once. | Nothing to change; expect that first call to be slower on a large dataset. |
| **Job filters** | The documentation was wrong about `context.Succeeded`/`context.Exception` (they are only set after the whole pipeline finished) and about filter lifetime. | If a filter reads them after `await next(ct)`, switch to `try/catch` around `next`. Register filters as singletons. |

> [!WARNING]
> **Redis: upgrade every node together.** The job index and the Succeeded/Failed sets are maintained by the new version only. Nodes still on v5.5.0 keep enqueuing and finishing jobs without writing to them, so those jobs are missing from the dashboard lists and from retention (which now walks the index) until they are indexed. The one-time backfill runs on the first dashboard or metrics call after the upgrade and is then marked done (`nexjob:index:ready`).
>
> - Preferred: stop all nodes, deploy v5.6.0 everywhere, start them.
> - If you already upgraded node by node: once **all** nodes run v5.6.0, delete the key `nexjob:index:ready` (`DEL nexjob:index:ready`). The next dashboard or metrics call runs the backfill again; it is idempotent and adds the missing jobs and counts.
> - The backfill scans the job keyspace once and is not locked, so several nodes may do it at the same time. On a large Redis, expect that first call to be slow; upgrading a single node first and opening the dashboard there lets you absorb that cost before the rest.
> - The distributed throttle also uses a new key while mixed versions run (see the table above).

### Schema changes

PostgreSQL and SQL Server apply these automatically on startup:

- **V9** — `checkpoint_json` column on `nexjob_jobs` (job progress checkpoints).
- **V10** — the idempotency key is unique only among active jobs (`Enqueued`, `Processing`, `Scheduled`, `AwaitingContinuation`), so a finished job no longer blocks a new one with the same key.

V10 drops and recreates indexes on `nexjob_jobs` at startup. On a very large table that can take time and hold locks on writes while the index is built (not measured), so upgrade one node first and consider a quiet period for big deployments.

### New in v5.6

Progress checkpoints (`IJobContext.SaveCheckpointAsync`), the queue circuit breaker, `[Retention]` anti-bloat strategies, the structured logging scope, and the dashboard job catalog, multi-cluster federation, ops-host mode and orphan-queue indicators. See the [Changelog](../../CHANGELOG.md).

---

## v5.4.1 → v5.5.0

- **Batch fetching and acknowledgment** (opt in with `EnableBatchAcknowledgment`) for PostgreSQL, SQL Server, MongoDB, Redis and InMemory: workers fetch as many jobs as they have free slots, and successful jobs are acknowledged in batches.
- **SQL Server:** scheduled-job promotion is guarded by a non-blocking application lock, so parallel workers no longer contend on it.
- **Breaking changes:** none.

---

## v5.4.0 → v5.4.1
 
### Highlights & Bug Fixes

- **`IListenerRegistry` Integration Across All External Triggers:** Added unified connection state tracking (`Starting`, `Listening`, `Reconnecting`, `Faulted`, `Stopped`) to `NexJob.Trigger.Salesforce`, `NexJob.Trigger.SalesforceStreaming`, `NexJob.Trigger.AzureServiceBus`, `NexJob.Trigger.AwsSqs`, and `NexJob.Trigger.GooglePubSub`.
- **Dashboard Visibility:** All external triggers now report their status directly to the Dashboard `/listeners` page and the Cluster Pipeline Topology Map.
- **Breaking Changes:** None. 100% backwards compatible (optional `IListenerRegistry? = null` parameter in constructors).

---

## v5.3.0 → v5.4.0

### Highlights & New Features

- **Enterprise Maxton Dashboard Layout:** Complete UI redesign with 64px Top Header, responsive sidebar toggle (☰), shortcut search (`Ctrl + K`), live cluster health indicator, and 5 instant-switch themes (`Blue Theme` default, `Dark`, `Light`, `Semi-Dark`, `Bordered`) persisted in `localStorage`.
- **Cluster Pipeline Topology Map:** Native SVG & CSS interactive topology visualization connecting Ingress/Triggers ➔ Queue Buffers ➔ Workers with live activity pulses.
- **Real-Time SSE Live Log Streaming:** Dynamic execution log streaming on `/jobs/{id}` via Server-Sent Events.
- **Active Event Triggers & Listeners Visibility:** Dedicated `/listeners` page in dashboard driven by Core `IListenerRegistry`, tracking real-time broker connection status (`Listening`, `Reconnecting`, `Faulted`).
- **Kafka Custom Security Configurations:** Support for `ConfigureConsumer` and `ConfigureProducer` delegates in `NexJob.Kafka` for SASL/SSL credentials, raw PEM certificate strings, and custom client tuning.
- **Breaking Changes:** None. Fully backwards compatible.

---

## v2.x → v3.0

### Breaking changes

#### 1. `AddNexJob` returns `NexJobBuilder`

```csharp
// Before (v2):
services.AddNexJob(opt => { ... })
        .AddSingleton<MyService>();

// After (v3):
services.AddNexJob(opt => { ... })
        .Services                        // access IServiceCollection
        .AddSingleton<MyService>();

// NexJob-specific extensions chain directly:
services.AddNexJob(opt => { ... })
        .AddNexJobJobs(typeof(Program).Assembly)
        .UseDashboardReadReplica("replica-conn");
```

#### 2. Custom storage providers must implement 3 interfaces

If you implemented a custom `IStorageProvider`, split it into
`IJobStorage`, `IRecurringStorage`, and `IDashboardStorage`.
`IStorageProvider` is now `IJobStorage + IRecurringStorage + IDashboardStorage`.

Register all 4 in DI:
```csharp
services.TryAddSingleton<MyProvider>();
services.TryAddSingleton<IStorageProvider>(sp => sp.GetRequiredService<MyProvider>());
services.TryAddSingleton<IJobStorage>(sp => sp.GetRequiredService<MyProvider>());
services.TryAddSingleton<IRecurringStorage>(sp => sp.GetRequiredService<MyProvider>());
services.TryAddSingleton<IDashboardStorage>(sp => sp.GetRequiredService<MyProvider>());
```

**Standard users (built-in providers): no action required.**

### New features in v3

- `UseDashboardReadReplica()` — route dashboard queries to a read replica
- `IJobControlService` — programmatic requeue/delete/pause from application code
- `AddNexJobDistributedThrottle()` (`NexJob.Redis`) — global Redis-backed throttle enforcement
- `NexJobOptions.DistributedThrottleTtl` — configurable slot TTL

---

## Schema Migration (Job Payloads)

When your job input type changes, existing jobs in storage may have incompatible payloads. NexJob handles this with automatic schema migration.

### Define the Migration

```csharp
// Old input (v1)
public sealed record SendEmailInputV1(string To, string Subject);

// New input (v2)
public sealed record SendEmailInputV2(string To, string Subject, string ReplyTo);

// Migration implementation
public sealed class SendEmailV1ToV2 : IJobMigration<SendEmailInputV1, SendEmailInputV2>
{
    public SendEmailInputV2 Migrate(SendEmailInputV1 old)
    {
        return new SendEmailInputV2(old.To, old.Subject, ReplyTo: "noreply@example.com");
    }
}
```

### Register the Migration

```csharp
builder.Services.AddJobMigration<SendEmailInputV1, SendEmailInputV2, SendEmailV1ToV2>();
```

### Declare Schema Version on Job

```csharp
[SchemaVersion(2)]
public sealed class SendEmailJob : IJob<SendEmailInputV2>
{
    public async Task ExecuteAsync(SendEmailInputV2 input, CancellationToken ct)
    {
        // input is always v2 — old v1 payloads are migrated automatically
    }
}
```

When the dispatcher fetches a job with a mismatched schema version, it:

1. Deserializes the stored payload as the old type
2. Runs the migration
3. Passes the new type to the job

---

## v0.5.x → v0.6.0

### Breaking Changes

- `AddNexJob()` now defaults to InMemory storage. Previously required explicit provider configuration.
- `CommitJobResultAsync` is now the atomic commit path for all job finalization. Provider implementations that used separate calls to `AcknowledgeAsync` and `SaveExecutionLogsAsync` have been consolidated.

### API Changes

- `EnqueueAsync` now returns `JobId` directly instead of `EnqueueResult`. The `EnqueueResult` type is only used by `IStorageProvider`.
- `DuplicatePolicy` default is now `AllowAfterFailed` (previously was implicit reject-on-duplicate behavior).

### Config Changes

No configuration changes required for existing `NexJobOptions` usage.

---

## v0.7.x → v0.8.0

### Breaking Changes

**`DashboardOptions.RequireAuth` removed**

The `RequireAuth` boolean property has been removed from `DashboardOptions`. It only supported ASP.NET Core authentication and has been replaced by the more flexible `IDashboardAuthorizationHandler` interface.

```csharp
// BEFORE (v0.7.x) — no longer compiles
app.UseNexJobDashboard("/dashboard", opt =>
{
    opt.RequireAuth = true;
});

// AFTER (v0.8.0) — implement IDashboardAuthorizationHandler
public sealed class DashboardAuth : IDashboardAuthorizationHandler
{
    public Task<bool> AuthorizeAsync(HttpContext context) =>
        Task.FromResult(context.User.Identity?.IsAuthenticated == true);
}

builder.Services.AddSingleton<IDashboardAuthorizationHandler, DashboardAuth>();
app.UseNexJobDashboard("/dashboard");
```

See [Dashboard](10-Dashboard.md) for authorization examples.

### New Features

- **`IDashboardAuthorizationHandler`** — pluggable dashboard authorization. Implement and register in DI.
- **Persistent `IRuntimeSettingsStore`** — all storage providers (PostgreSQL, SQL Server, Redis, MongoDB) now persist runtime settings across restarts. Dashboard overrides survive deploys.
- **Job Retention** — automatic cleanup of terminal jobs (`Succeeded`, `Failed`, `Expired`) via configurable TTL. Configurable via `NexJobOptions` (code) and the dashboard Settings page.
- **`IJobExecutionFilter`** — middleware pipeline for cross-cutting job execution behaviour.

### Schema Changes

PostgreSQL and SQL Server providers apply two new migrations automatically on startup:

- **V7** — `nexjob_settings` table for persistent runtime configuration

---

## v0.4.x → v0.5.0

### Breaking Changes

- `IJob<T>.ExecuteAsync` signature changed: `input` parameter is now the first parameter (before `cancellationToken`).
- `RecurringJobSettings` moved from `NexJobOptions.RecurringJobs` to a separate collection configured via `AddRecurringJob` methods.

### API Changes

- `IScheduler.ContinueWithAsync` now returns `JobId` for the child job.
- `IJobContext.Progress` replaced with `ReportProgressAsync`.

---

## General Migration Guidelines

### Before Upgrading

1. Review the [Changelog](../../CHANGELOG.md) for breaking changes
2. Run tests against the new version
3. Check storage provider compatibility (schema changes are handled by the provider)

### After Upgrading

1. Verify all jobs register correctly
2. Check the dashboard for job execution
3. Monitor metrics for any regression in `nexjob.jobs.failed`

---

## Next Steps

- [Storage Providers](09-Storage-Providers.md) — Provider-specific migration notes
- [Configuration Reference](11-Configuration-Reference.md) — Updated configuration options
- [Changelog](../../CHANGELOG.md) — Full version history
