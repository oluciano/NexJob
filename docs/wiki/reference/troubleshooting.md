---
title: "NexJob Troubleshooting Guide: Fix Common Job Issues"
sidebarTitle: "Troubleshooting"
description: "Debug common NexJob problems: jobs not running, stuck in Processing, unexpectedly Expired, high memory usage, and dashboard errors."
---

NexJob is designed to surface diagnostic information through structured logs, metrics, and the dashboard, but some problems require you to know where to look. This guide walks through the most common issues and their fixes, organized by symptom.

???+ "Jobs not being picked up (staying Enqueued or Scheduled)"
    **Symptom:** A job stays in `Enqueued` or `Scheduled` indefinitely and never moves to `Processing`.

    **Check 1 — The host is not running as a background service**

    NexJob workers run as `BackgroundService` instances. If your app exits immediately after registering services, the dispatcher never starts.

    ```csharp
    // Worker Service — ensure you call RunAsync
    var host = Host.CreateDefaultBuilder(args)
        .ConfigureServices(services => services.AddNexJob())
        .Build();

    await host.RunAsync();
    ```

    **Check 2 — Queue name mismatch**

    The dispatcher only processes queues listed in `options.Queues`. If you enqueued a job on `"emails"` but your options only list `"default"`, the job sits untouched.

    Verify the job's queue name matches your configuration:

    ```csharp
    builder.Services.AddNexJob(options =>
    {
        options.Queues = new[] { "default", "emails" }; // Add any queues your jobs use
    });
    ```

    **Check 3 — Scheduled job not yet due**

    `ScheduleAsync` and `ScheduleAtAsync` set an earliest run time, not a deadline. The dispatcher will not pick up the job until `ScheduledAt` is reached. Check `JobRecord.ScheduledAt` against `UtcNow` in the dashboard.

    **Check 4 — Queue is paused**

    A queue paused from the dashboard or via `IRuntimeSettingsStore` stops the dispatcher from fetching from it entirely. Check the dashboard Queues page for a paused badge. A pause is picked up on each node's next polling cycle, so a job fetched by a cycle that was already running can still start right after you pause.

    **Check 5 — All worker slots are occupied**

    If every worker is busy with long-running jobs, no new jobs start. Open the dashboard and look for jobs stuck in `Processing`. Increase `Workers` or add `[Throttle]` to prevent resource exhaustion:

    ```csharp
    options.Workers = 50; // Increase from default 10
    ```


???+ "Jobs stuck in Processing (orphaned jobs)"
    **Symptom:** A job shows `Processing` in the dashboard but no worker is actively running it. It never completes.

    **Cause: Orphaned job**

    A previous worker process crashed while executing the job. The job record is stuck in `Processing` with a stale heartbeat.

    **Diagnose:** In the dashboard Job Detail view, compare `ProcessingStartedAt` and `HeartbeatAt`. If `UtcNow − HeartbeatAt > HeartbeatTimeout` (default 5 minutes), the job is orphaned.

    **Fix: Wait for automatic recovery**

    NexJob automatically detects stale heartbeats and re-enqueues orphaned jobs. It runs every `HeartbeatTimeout` (default every 5 minutes), so recovery takes between 5 and ~10 minutes with defaults. If the job has already consumed all its attempts, it is marked `Failed` instead of re-enqueued.

    To recover faster, reduce `HeartbeatTimeout` — but keep it higher than your longest job's expected duration, or healthy long-running jobs will be falsely requeued:

    ```csharp
    options.HeartbeatTimeout = TimeSpan.FromMinutes(2); // Faster detection
    options.HeartbeatInterval = TimeSpan.FromSeconds(20); // Workers update more often
    ```


???+ "Jobs unexpectedly Expired"
    **Symptom:** Jobs are marked `Expired` before you expected them to run.

    **Cause 1: Tight deadline on a busy queue**

    The `deadlineAfter` parameter passed to `EnqueueAsync` is checked when the dispatcher picks up the job — not when it was enqueued. If the queue is busy and the job waits longer than the deadline, it expires before it ever runs.

    ```csharp
    // Increase the deadline to give the job more time to wait in queue
    await scheduler.EnqueueAsync<MyJob>(
        deadlineAfter: TimeSpan.FromMinutes(30),
        cancellationToken: ct);
    ```

    You can also add workers to reduce queue wait time.

    **Cause 2: Deadline used with ScheduleAsync**

    `deadlineAfter` only applies to `EnqueueAsync`. Scheduled jobs use `ScheduledAt` as an *earliest* start time, not a hard deadline. If you need a "not after" constraint on a scheduled job, embed it in the input and check it at execution time:

    ```csharp
    var runAt = DateTimeOffset.UtcNow.AddMinutes(30);
    await scheduler.ScheduleAtAsync<MyJob, MyInput>(
        new MyInput(NotAfter: runAt.AddMinutes(10)),
        runAt,
        cancellationToken: ct);

    // Inside the job:
    // if (DateTimeOffset.UtcNow > input.NotAfter) return; // too late, skip
    ```

    **Cause 3: Queue was paused or circuit breaker was open**

    Jobs do not expire while paused — but the clock keeps running. If a queue was paused or its circuit breaker was open past the deadline, the job expires when a worker finally tries to pick it up.


???+ "Jobs failing immediately (exception details and retry config)"
    **Symptom:** A job fails on its first execution, or retries exhaust quickly.

    **Step 1: Read the exception**

    Open the Job Detail view in the dashboard. The execution log captures the full exception type, message, and stack trace. This is always your first stop.

    **Step 2: Understand retry counting**

    `MaxAttempts` counts **total executions**, not retries. `MaxAttempts = 3` means the job runs at most 3 times. To extend retry life:

    ```csharp
    builder.Services.AddNexJob(options =>
    {
        options.MaxAttempts = 10; // Global default
    });

    // Or per-job with the [Retry] attribute:
    [Retry(5)]
    public sealed class MyJob : IJob { ... }
    ```

    **Step 3: Adjust retry delay**

    The default backoff is `attempt^4 + 15 + random(30) × (attempt+1)` seconds — roughly 16–74 s before the 2nd try, and about 1.8 h before the 10th. Override it globally:

    ```csharp
    options.RetryDelayFactory = attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt));
    ```

    **Step 4: Check DI registration**

    If the job constructor throws because a dependency is missing, the job fails before your code runs. Verify the job's dependencies are registered in the DI container.


???+ "Dead-letter handler not triggered"
    **Symptom:** A job exhausts its attempts and moves to `Failed`, but your `IDeadLetterHandler<TJob>` is never called.

    **Check 1: Handler is registered in DI**

    The dead-letter dispatcher resolves handlers from the DI container. If you forgot to register the handler, nothing is called:

    ```csharp
    builder.Services.AddScoped<IDeadLetterHandler<MyJob>, MyJobDeadLetterHandler>();
    ```

    **Check 2: Handler is throwing**

    The dead-letter dispatcher swallows handler exceptions and logs them — a throwing handler never blocks the rest of the system, but it also never surfaces visibly unless you check the logs. Search your logs for the handler's type name at `Error` level.

    **Check 3: Job was orphaned, not failed by a retry**

    If an orphaned job has consumed all its attempts, it is marked `Failed` with the message `"Orphaned execution exceeded maximum attempts."`. From v5.8.0 the dead-letter handler and the `IDeadLetterForwarder`s **are** called for it, with an `OrphanedJobException` (on the built-in providers; a custom storage that does not implement `IOrphanedJobReporter` still fails the job without calling them). If the handler did not run, look in the logs for the warning `... stopped sending heartbeats and no attempts were left`. See [When the node dies](../concepts/retries-and-dead-letter.md#when-the-node-dies).


???+ "Dashboard not loading or showing no data"
    **Symptom:** The dashboard returns a 404, shows a blank page, or shows no jobs.

    **Check 1: Middleware is registered**

    Make sure you have called the correct setup method:

    ```csharp
    // ASP.NET Core
    app.UseNexJobDashboard();

    // Worker Service
    builder.Services.AddNexJobStandaloneDashboard();
    ```

    **Check 2: NuGet package is installed**

    The dashboard is a separate package. If you see a compiler error like `CS1061: 'IApplicationBuilder' does not contain a definition for 'UseNexJobDashboard'`, install the package and add the `using` directive:

    ```bash
    # ASP.NET Core
    dotnet add package NexJob.Dashboard

    # Worker Services
    dotnet add package NexJob.Dashboard.Standalone
    ```

    ```csharp
    using NexJob.Dashboard;           // for UseNexJobDashboard()
    // or
    using NexJob.Dashboard.Standalone; // for AddNexJobStandaloneDashboard()
    ```

    **Check 3: IMemoryCache not registered**

    If the dashboard is mounted in a host that never called `AddNexJob()`, you may see a startup exception:

    ```
    InvalidOperationException: No service for type 'Microsoft.Extensions.Caching.Memory.IMemoryCache'
    ```

    Fix it by adding memory cache registration:

    ```csharp
    builder.Services.AddMemoryCache();
    ```

    `AddNexJob()` and `AddNexJobStandaloneDashboard()` already call this, so you only see the error in unusual multi-host setups.

    **Check 4: Storage mismatch**

    If the dashboard and dispatcher use different connection strings, the dashboard reads from a different database and sees no jobs. Confirm both use the same storage provider and connection string.

    **Check 5: Jobs were purged by retention**

    Short retention periods may have already deleted the jobs you're looking for. Check your `RetentionSucceeded` / `RetentionFailed` settings, or extend them:

    ```csharp
    options.RetentionSucceeded = TimeSpan.FromDays(14);
    ```

    **Check 6: Standalone dashboard is binding to loopback**

    The standalone dashboard binds to `localhost` only by default. In a container, set `LocalhostOnly = false` and register an `IDashboardAuthorizationHandler`.


???+ "High memory usage or poor throughput"
    **Symptom:** Memory grows over time, or throughput is lower than expected under load.

    **Batch acknowledgment**

    By default, every job completion is a synchronous database write. Enable batch acknowledgment to aggregate completions and cut write roundtrips by over 90%:

    ```csharp
    options.EnableBatchAcknowledgment = true;
    ```

    !!! warning
        With batch acknowledgment enabled, `ContinueWith` continuation jobs are released correctly as of v5.6.0. On earlier versions, continuations were not triggered.


    **Worker count and throttle sizing**

    A throttled job that cannot get its slot waits briefly in `Processing`, holding its worker slot, and after about 5 seconds is returned to the queue (it does not use an attempt), so a saturated resource no longer keeps every worker busy for long. Jobs on that resource can still be delayed and run in a different order than they were enqueued. Keep `Workers` above the sum of the `maxConcurrent` values across your throttles, or isolate throttled jobs in a dedicated queue. See [How Waiting Works](../guides/throttling.md#how-waiting-works).

    **Retention and log accumulation**

    Without retention, completed jobs accumulate indefinitely. Confirm `RetentionInterval` is set (default 1 hour) and that the retention periods are not set to `TimeSpan.Zero` for statuses you don't intend to keep forever. Per-job anti-bloat attributes can help for high-volume jobs:

    ```csharp
    [Retention(PurgeOnSuccess = true)]       // Delete the row as soon as the job succeeds
    public sealed class HighFrequencyJob : IJob { ... }

    [Retention(TrimPayloadOnSuccess = true)] // Keep the row, wipe the input payload
    public sealed class BigPayloadJob : IJob<HugeInput> { ... }
    ```


???+ "Database connection issues"
    **Symptom:** Jobs fail with connection-related exceptions, or the host logs warnings about pool exhaustion.

    **Pool sizing rule of thumb**

    NexJob typically uses about `Workers + 10` connections per node. For a 3-node cluster each with 20 workers, allow ~90 connections. As of v5.7.0, NexJob logs the detected pool size and worker count at startup, and warns when the pool is smaller than `Workers`.

    **PostgreSQL: `AddNexJobPostgres(NpgsqlDataSource)` on password-protected databases**

    On NexJob v5.6.0–v5.6.2, passing a pre-built `NpgsqlDataSource` to `AddNexJobPostgres` caused the host to crash within seconds on databases that require a password:

    ```
    No password has been provided but the backend requires one.
    ```

    Upgrade to v5.7.0 or later, or use the connection string overload as a temporary workaround:

    ```csharp
    builder.Services.AddNexJobPostgres("Host=...;Username=...;Password=...;Database=nexjob");
    ```

    **SQL Server: deadlocks under concurrent workers**

    SQL Server can produce error 1205 (deadlock victim) under high concurrency. This was partially addressed in v5.7.0 with index hints on continuation release statements. Monitor your application logs for `SqlException` with error code 1205. Consider using PostgreSQL for the highest-throughput workloads.

