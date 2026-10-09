---
title: "NexJob Migration Guide: Breaking Changes and Upgrades"
sidebarTitle: "Migration"
description: "Step-by-step migration guides for NexJob major version upgrades, including breaking changes, renamed APIs, and configuration updates."
---

This page covers every breaking change between NexJob releases and tells you exactly what to update in your code. Schema migrations for PostgreSQL and SQL Server apply automatically at startup. Follow the steps in order for each version jump you're crossing.

## v5.10.0 → v6.0.0

No schema migration. One behavior changes for everyone who enqueues without naming a queue.

**The implicit `default` queue is now `{prefix}.default`.** The prefix is `NexJobOptions.QueuePrefix` or, when unset, the full lowercase entry assembly name. What this means for you:

- **Stored jobs keep running.** Every host also polls the legacy `default` queue; no row is renamed. Recurring jobs already stored with queue `default` keep firing into it.
- **New jobs go to the new queue.** The dashboard, the queue metric tags and any query by queue name show `{prefix}.default` for them. Update dashboards and alerts that filter on `default`.
- **Set the prefix yourself in production.** A derived prefix changes when the assembly is renamed and jobs stay in the old queue. The host logs a warning while the prefix is derived.
- **Rolling deploy.** A node still on v5 enqueues and polls `default`; a v6 node drains it, so nothing is lost. Jobs enqueued by v6 nodes into `{prefix}.default` are not seen by v5 nodes until they are upgraded.
- **Configuration keyed by `"default"`** (`ConfigureQueue`, pause, circuit breaker, execution window) applies to the prefixed queue as well.
- **Triggers** (`TargetQueue = "default"`) enqueue into the prefixed queue. A queue you name explicitly is not prefixed.
- **Dashboard queue scope** (`DashboardOptions.Queues`) lists stored names: add `{prefix}.default` next to `default`.

## v5.9.0 → v5.10.0

Nothing in the public API is removed, there is no schema migration and no stored format changes. One thing is visible to your users.

#### The dashboard has a layout for narrow screens

Up to 768 px wide, the sidebar is icon-only, the header drops the search box and the Docs and GitHub links, and the pages fit the screen (before, they scrolled horizontally). Nothing to do. If you embedded the dashboard behind a proxy or a screenshot test at a phone width, expect a different picture.

#### Rolling upgrade and rollback

Nothing is stored differently, so old and new nodes run together without any special step, and you can go back to 5.9.0 at any time. Upgrade one node first and open the dashboard on a phone-sized window.

New and opt-in, with no action needed: `EnvironmentName` on `DashboardOptions` and `StandaloneDashboardOptions` ([Dashboard](../integrations/dashboard.md#configuration-options)) shows a Production, Staging or Development badge and prefixes the browser tab title.

## v5.8.0 → v5.9.0

Nothing in the public API is removed and there is no schema migration. Two things need your attention.

#### If you implement `IScheduler`

!!! warning
    `IScheduler` has four new members: `EnqueueAsync<TJob>(int maxAttempts, ...)`, `EnqueueAsync<TJob, TInput>(TInput input, int maxAttempts, ...)`, `ScheduleAsync<TJob>(TimeSpan delay, int maxAttempts, ...)` and `ScheduleAsync<TJob, TInput>(TInput input, TimeSpan delay, int maxAttempts, ...)`. They are new overloads, so every existing call still compiles and behaves as before. A class that implements `IScheduler` itself (a test double, a decorator) must add them, or it no longer compiles.

#### `MaxAttempts` is now the real limit

For a class with `[Retry(n)]`, the `MaxAttempts` stored on a new job, and `IJobContext.MaxAttempts`, now report `n`. Before, they reported the global default while the attribute decided the outcome. `[Retry(0)]` is stored as `1`. Jobs already stored keep working: the attribute still applies to a row stored with the global default.

#### Rolling upgrade

There is no new stored format. While old and new nodes run together, an old node ignores a per-job `maxAttempts` and applies the class's `[Retry]`, and it does not know `IgnoreRetryAttemptExceptions`, `[ExecutionTimeout]` or `DaysOfWeek`. Everything else is compatible. Upgrade one node first, watch the failed and dead-letter counts, then the rest. To roll back, return to v5.8.0: nothing stored needs to be undone.

New and opt-in, with no action needed: [`[ExecutionTimeout]`](../concepts/retries-and-dead-letter.md#execution-timeout), [`IgnoreRetryAttemptExceptions`](../concepts/retries-and-dead-letter.md#failures-that-should-not-be-retried), [per-job `maxAttempts`](../concepts/retries-and-dead-letter.md#per-job-attempt-limit), and [`DaysOfWeek`](../guides/execution-windows.md#only-on-some-days) (the default is every day).

## v5.7.0 → v5.8.0

Nothing in the public API is removed. This release changes **behaviour** that you may depend on, and it adds a stored field. Read both parts before a rolling upgrade.

#### Deadlines now take effect

!!! warning
    **Behaviour change.** `deadlineAfter` was accepted but never stored by the PostgreSQL, SQL Server, MongoDB and Redis providers, so a job that was already past its deadline still ran. From v5.8.0 the deadline is stored and enforced: a job fetched after its deadline is marked `Expired` and does not execute. If your application passes `deadlineAfter`, expect `Expired` jobs where you used to see executed ones. Jobs enqueued **before** the upgrade have no stored deadline and are not affected.


Other behaviour that changes, each described in the changelog:

- A throttled job that cannot get its slot within about 5 seconds is returned to the queue instead of holding its worker. It does not use an attempt, and it can run in a different order than it was enqueued.
- A job interrupted by shutdown, or deferred because its type is not available in the process, no longer consumes an attempt on database providers (it used to, in spite of the log message).
- Pausing a queue takes effect on each node's next polling cycle; running jobs are never interrupted.
- A job whose attempts are used up because the node running it died now calls its `IDeadLetterHandler<TJob>` and the `IDeadLetterForwarder`s, with an `OrphanedJobException`. Before, it became `Failed` silently. If you have a handler that alerts or forwards, expect it to start firing for those jobs.

#### What is stored differently

| Provider | Change | What happens on upgrade |
| --- | --- | --- |
| PostgreSQL | nullable `expires_at` column on `nexjob_jobs` (migration V11) | Applied automatically by the first v5.8 node that starts. Existing rows keep `NULL`. |
| SQL Server | nullable `expires_at` column on `nexjob_jobs` (migration V11) | Same. |
| Redis | `expiresAt` field in the job hash, set only when the job has a deadline | Nothing to run. Existing hashes simply lack the field. |
| MongoDB | `ExpiresAt` element in the job document, written **only** when the job has a deadline | Nothing to run. Existing documents simply lack the element. |

The upgrade of a storage that already holds jobs (enqueued, scheduled, with continuations, an orphan left `Processing`, finished and failed history) is covered by tests on all four database providers.

#### Rolling upgrade: v5.7 and v5.8 nodes on the same storage

| Provider | Old (v5.7) node reads what a v5.8 node wrote | New (v5.8) node reads what a v5.7 node wrote |
| --- | --- | --- |
| PostgreSQL, SQL Server | Yes. It ignores the extra column, and its own inserts leave `expires_at` empty. | Yes, tested. |
| Redis | Yes. It ignores the extra hash field. | Yes. |
| MongoDB | **Yes, as long as the job has no deadline.** A document with a deadline carries an element that v5.7 does not know, and the MongoDB driver throws on it, so a v5.7 node fails to read that job. | Yes. |

Two rules follow:

1. **MongoDB: do not use `deadlineAfter` until every node runs v5.8.** If you must, upgrade all nodes together instead of one by one.
2. **Until every node is on v5.8, deadlines are enforced only by v5.8 nodes.** A v5.7 node ignores the stored deadline and may still execute an expired job, as it always did.

v5.8 documents also ignore elements they do not know, so the same constraint will not apply to the next upgrade.

**Rollback.** Going back to v5.7 is safe on PostgreSQL, SQL Server and Redis: the extra column or field is ignored and can stay. On MongoDB, first make sure no job with a deadline is left (or remove the `ExpiresAt` element from those documents), because v5.7 cannot read them.

## v5.6.2 → v5.7.0



#### Update the standalone dashboard's LocalhostOnly default

!!! warning
    **Breaking behavior change.** The standalone dashboard now listens on **loopback only** by default (`LocalhostOnly = true`). Before v5.7.0, the default was `false` (all interfaces). If you reach the dashboard from another host or a published container port, the dashboard will no longer be reachable after upgrading without a configuration change.


If you need the dashboard reachable from outside the container, set `LocalhostOnly = false` **and** register an `IDashboardAuthorizationHandler`. Without a handler, NexJob logs a startup warning.

```csharp
builder.Services.AddNexJobStandaloneDashboard(options =>
{
    options.LocalhostOnly = false; // Required for published container ports
});

// Register an authorization handler (required when LocalhostOnly = false)
builder.Services.AddSingleton<IDashboardAuthorizationHandler, MyDashboardAuthHandler>();
```

The embedded server has no authentication middleware, so your handler must authenticate directly from `context.Request`.


#### Update the standalone dashboard's authorization handler (if you registered one)

!!! warning
    **Breaking behavior change.** A registered `IDashboardAuthorizationHandler` is now **enforced** in standalone mode. Before v5.7.0, it was silently ignored.


If your handler relied on `context.User` being populated (e.g., `context.User.Identity?.IsAuthenticated == true`), it will now deny every request because the embedded server runs no authentication middleware. Update the handler to authenticate from the raw request instead:

```csharp
public sealed class DashboardAuth : IDashboardAuthorizationHandler
{
    public Task<bool> AuthorizeAsync(HttpContext context)
    {
        // Authenticate from headers, query string, IP, or a cookie — not context.User
        var apiKey = context.Request.Headers["X-Dashboard-Key"].ToString();
        return Task.FromResult(apiKey == "my-secret");
    }
}
```


#### Redis: no action required for job index reconciliation

The Redis job index is now reconciled every hour under a distributed lock. You no longer need to delete any Redis keys after a rolling upgrade. NexJob logs a warning with a count when it finds jobs that were missing from the index, which means a node is still on an older version.


#### SQL Server: ensure idx_nexjob_jobs_parent exists

NexJob now uses an index hint on continuation-release statements to eliminate deadlock victims under concurrent workers. The index `idx_nexjob_jobs_parent` has been part of the schema since the SQL Server provider was introduced and is recreated at startup. If you dropped it manually, restart the service to recreate it.


#### PostgreSQL: upgrade if you use AddNexJobPostgres(NpgsqlDataSource)

On v5.6.0–v5.6.2, `AddNexJobPostgres(NpgsqlDataSource)` crashed the host on databases that require a password:

```text
No password has been provided but the backend requires one.
```

Upgrade to v5.7.0. No code change is required after upgrading. As a temporary workaround on v5.6.x, use the connection string overload:

```csharp
// Workaround for v5.6.0–v5.6.2 only
builder.Services.AddNexJobPostgres("Host=...;Username=nexjob;Password=secret;Database=nexjob");
```




---

## v5.6.0 → v5.6.1



#### Update broker trigger publishers if you wrapped bodies in JSON string quotes

!!! warning
    **Breaking behavior change.** Broker triggers (SQS, Service Bus, Pub/Sub, Kafka, RabbitMQ) now pass the message body verbatim to `IJob<string>`. Previously, only a body that was itself a JSON string literal would execute; all other formats failed.


If you worked around the old bug by publishing a JSON string literal — for example, sending `"\"hello\""` with explicit surrounding quotes so the body was a valid JSON string — your job now receives those quotes as literal text. Remove the extra quoting from your publisher:

```csharp
// Before (workaround for the old bug)
await producer.PublishAsync("\"hello\"");

// After (correct — send the plain value)
await producer.PublishAsync("hello");
```




---

## v5.5.0 → v5.6.0



#### Update Kafka trigger deduplication logic

!!! warning
    **Breaking behavior change.** The Kafka trigger idempotency key changed from the message key to the record position: `kafka:{topic}:{partition}:{offset}`. Records that shared a message key used to be silently deduplicated; now each record produces a separate job.


If you relied on the Kafka message key for deduplication, move that logic inside the job itself. Check whether a job with the same logical key already exists before acting:

```csharp
public async Task ExecuteAsync(string body, CancellationToken ct)
{
    var key = ParseKeyFromBody(body);
    if (await _db.AlreadyProcessed(key, ct)) return; // idempotent guard
    // ...
}
```


#### Update RabbitMQ trigger publishers to use MessageId for deduplication

!!! warning
    **Breaking behavior change.** The RabbitMQ idempotency key is now `MessageId` only. `CorrelationId` and the body hash are no longer used. Without a `MessageId`, every delivery creates a new job (no deduplication).


If you relied on `CorrelationId` for deduplication, set a unique `MessageId` on each published message:

```csharp
var props = channel.CreateBasicProperties();
props.MessageId = Guid.NewGuid().ToString(); // Unique per message
```


#### Check Kafka and RabbitMQ dead-letter topic configuration

Transient enqueue failures (storage down, timeouts) are now retried instead of being dead-lettered or lost. Permanent failures (missing job type, malformed payload) still go to the dead-letter topic. If you cannot afford to skip a Kafka poison message on permanent failure, configure a dead-letter topic:

```csharp
.AddKafkaTrigger(options =>
{
    options.DeadLetterTopic = "orders-dlq"; // Required if you can't skip poison messages
});
```


#### Verify recurring jobs from appsettings.json no longer run at startup

!!! warning
    **Behavior change.** Recurring jobs declared in `appsettings.json` used to fire immediately on first registration. They now schedule their first run at the next cron occurrence.


If you depended on the immediate first run, trigger the job manually on startup or adjust your cron to run shortly after the expected deploy time.


#### Set HostOptions.ShutdownTimeout above NexJobOptions.ShutdownTimeout

Graceful shutdown now stops fetching immediately and requeues interrupted jobs without consuming an attempt. To ensure running jobs have time to finish, `HostOptions.ShutdownTimeout` must be **greater** than `NexJobOptions.ShutdownTimeout` (default 30 s):

```csharp
builder.Services.Configure<HostOptions>(opts =>
{
    opts.ShutdownTimeout = TimeSpan.FromSeconds(60); // > NexJob's ShutdownTimeout
});
```


#### Redis: upgrade all nodes together (or follow the rolling upgrade procedure)

!!! warning
    The Redis job index and the Succeeded/Failed sets are only maintained by nodes running v5.6.0+. Nodes still on v5.5.0 enqueue and finish jobs without writing to them, so those jobs are missing from the dashboard and from retention until indexed.

      **Preferred:** stop all nodes, deploy v5.6.0 everywhere, start them.

      **If you already upgraded node by node:** once all nodes run v5.6.0, delete the Redis key `nexjob:index:ready` using `DEL nexjob:index:ready`. The next dashboard or metrics call runs the backfill — it is idempotent. On a large Redis keyspace, expect that first call to be slow.

      From v5.7.0, the index is reconciled every hour under a lock. This hard requirement applies only to v5.6.x.


#### MongoDB: handle the DateTimeOffset serializer change

The `DateTimeOffset` serializer is no longer registered globally. NexJob now applies it only to its own documents. If your application unknowingly relied on the old global registration, pass the opt-in flag for one release:

```csharp
builder.Services.AddNexJobMongoDB(connectionString, keepLegacyGlobalDateTimeOffsetSerializer: true);
```

Remove this flag in the next release cycle — it will be removed in a future version.




---

## v2.x → v3.0



#### Update code that chains calls after AddNexJob

!!! warning
    **Breaking change.** `AddNexJob` now returns `NexJobBuilder` instead of `IServiceCollection`. Code that chains non-NexJob extensions directly after `AddNexJob` will no longer compile.


```csharp
// Before (v2) — no longer compiles
services.AddNexJob(opt => { })
        .AddSingleton<MyService>();

// After (v3) — use .Services to access IServiceCollection
services.AddNexJob(opt => { })
        .Services
        .AddSingleton<MyService>();

// NexJob-specific extensions still chain directly
services.AddNexJob(opt => { })
        .AddNexJobJobs(typeof(Program).Assembly)
        .UseDashboardReadReplica("replica-conn");
```


#### Update custom storage provider implementations

!!! warning
    **Breaking change.** `IStorageProvider` is now the composition of three separate interfaces: `IJobStorage`, `IRecurringStorage`, and `IDashboardStorage`. If you implemented a custom storage provider, split it and register all four types in DI.


Standard users (built-in providers: PostgreSQL, SQL Server, Redis, MongoDB, InMemory) — no action required.

```csharp
// Custom provider registration in v3
services.TryAddSingleton<MyProvider>();
services.TryAddSingleton<IStorageProvider>(sp => sp.GetRequiredService<MyProvider>());
services.TryAddSingleton<IJobStorage>(sp => sp.GetRequiredService<MyProvider>());
services.TryAddSingleton<IRecurringStorage>(sp => sp.GetRequiredService<MyProvider>());
services.TryAddSingleton<IDashboardStorage>(sp => sp.GetRequiredService<MyProvider>());
```




---

## v0.7.x → v0.8.0



#### Replace DashboardOptions.RequireAuth with IDashboardAuthorizationHandler

!!! warning
    **Breaking change.** The `RequireAuth` boolean property has been removed from `DashboardOptions`. It only supported ASP.NET Core cookie authentication and has been replaced by the flexible `IDashboardAuthorizationHandler` interface.


```csharp
// Before (v0.7.x) — no longer compiles
app.UseNexJobDashboard("/dashboard", opt =>
{
    opt.RequireAuth = true;
});

// After (v0.8.0) — implement and register IDashboardAuthorizationHandler
public sealed class DashboardAuth : IDashboardAuthorizationHandler
{
    public Task<bool> AuthorizeAsync(HttpContext context) =>
        Task.FromResult(context.User.Identity?.IsAuthenticated == true);
}

builder.Services.AddSingleton<IDashboardAuthorizationHandler, DashboardAuth>();
app.UseNexJobDashboard("/dashboard");
```




---

## v0.5.x → v0.6.0



#### Update EnqueueAsync call sites — return type changed

!!! warning
    **Breaking change.** `IScheduler.EnqueueAsync` now returns `JobId` directly instead of `EnqueueResult`.


```csharp
// Before
EnqueueResult result = await scheduler.EnqueueAsync<MyJob>(ct);
Guid id = result.JobId;

// After
JobId id = await scheduler.EnqueueAsync<MyJob>(ct);
```


#### Check DuplicatePolicy default behavior

The default `DuplicatePolicy` is now `AllowAfterFailed` (allow re-enqueue after a terminal failure state). The previous implicit behavior was to reject duplicates. If you relied on strict deduplication across a job's full lifetime, set the policy explicitly:

```csharp
await scheduler.EnqueueAsync<MyJob, MyInput>(
    input,
    idempotencyKey: "my-key",
    duplicatePolicy: DuplicatePolicy.RejectAlways,
    cancellationToken: ct);
```




---

## v0.4.x → v0.5.0



#### Update IJob<T>.ExecuteAsync signature

!!! warning
    **Breaking change.** The `input` parameter is now the **first** parameter in `IJob<T>.ExecuteAsync`, before `CancellationToken`.


```csharp
// Before (v0.4.x)
public Task ExecuteAsync(CancellationToken ct, MyInput input) { ... }

// After (v0.5.0)
public Task ExecuteAsync(MyInput input, CancellationToken ct) { ... }
```


#### Update recurring job registration

!!! warning
    **Breaking change.** `RecurringJobSettings` moved from `NexJobOptions.RecurringJobs` to a separate collection configured via `AddRecurringJob` methods.


Use `AddRecurringJob` on the `NexJobBuilder` returned by `AddNexJob`, or declare recurring jobs in `appsettings.json` using the `RecurringJobs` array.


#### Update IScheduler.ContinueWithAsync call sites

`IScheduler.ContinueWithAsync` now returns the `JobId` of the created child job. Update any code that previously discarded or did not expect a return value.


#### Replace IJobContext.Progress with ReportProgressAsync

`IJobContext.Progress` has been replaced by `IJobContext.ReportProgressAsync`. Update job implementations that reported progress:

```csharp
// Before
context.Progress = 50;

// After
await context.ReportProgressAsync(50, "Halfway done", ct);
```




---

## Schema Migration for Job Payloads

When your job input type changes, existing queued jobs may carry payloads that no longer match the current type. NexJob handles this automatically with `IJobMigration`.



#### Define the migration

```csharp
// Old input (v1)
public sealed record SendEmailInputV1(string To, string Subject);

// New input (v2)
public sealed record SendEmailInputV2(string To, string Subject, string ReplyTo);

// Migration from v1 to v2
public sealed class SendEmailV1ToV2 : IJobMigration<SendEmailInputV1, SendEmailInputV2>
{
    public SendEmailInputV2 Migrate(SendEmailInputV1 old) =>
        new(old.To, old.Subject, ReplyTo: "noreply@example.com");
}
```


#### Register the migration

```csharp
builder.Services.AddJobMigration<SendEmailInputV1, SendEmailInputV2, SendEmailV1ToV2>();
```


#### Declare the schema version on the job class

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




When the dispatcher fetches a job with a mismatched schema version, it deserializes the stored payload as the old type, runs the migration, and passes the new type to the job. No manual data migration is required.
