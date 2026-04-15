# Migration

Breaking changes, API updates, and schema migration between NexJob versions.

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
- `UseDistributedThrottle()` — global Redis-backed throttle enforcement
- `NexJobOptions.DistributedThrottleTtl` — configurable slot TTL

See the [full migration guide](migration-v2-to-v3.md) for details.

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
- **Job Retention** — automatic cleanup of terminal jobs (`Succeeded`, `Failed`, `Expired`) via configurable TTL. Configurable via `NexJobOptions` and the dashboard Settings page.
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
