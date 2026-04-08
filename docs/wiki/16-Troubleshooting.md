# Troubleshooting

Common problems, their causes, and how to fix them.

---

## Job Not Running

**Symptoms:** Job stays in `Enqueued` or `Scheduled` state indefinitely.

### Cause 1: Dispatcher Not Running

NexJob jobs are executed by `BackgroundService` instances. If your app doesn't run as a host, the dispatcher won't start.

**Diagnose:** Check if your app calls `host.Run()` or `app.Run()`.

**Fix:** Ensure the application runs as a host:

```csharp
// Worker Service
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services => services.AddNexJob())
    .Build();

await host.RunAsync();
```

### Cause 2: Queue Not Configured

The dispatcher only processes queues listed in `options.Queues`.

**Diagnose:** Check the job's queue name vs `options.Queues`.

**Fix:**

```csharp
builder.Services.AddNexJob(options =>
{
    options.Queues = new[] { "default", "emails" }; // Add the job's queue
});
```

### Cause 3: Scheduled Job Not Due Yet

Scheduled jobs wait until `ScheduledAt` is reached.

**Diagnose:** Check `JobRecord.ScheduledAt` vs `UtcNow`.

**Fix:** Wait for the scheduled time, or enqueue immediately with `EnqueueAsync` instead of `ScheduleAsync`.

### Cause 4: Workers Exhausted

All worker slots are occupied by long-running jobs.

**Diagnose:** Check the dashboard for jobs stuck in `Processing`. Check `options.Workers`.

**Fix:** Increase worker count or add `[Throttle]` to prevent resource exhaustion.

```csharp
options.Workers = 50; // Increase from default 10
```

---

## Job Stuck in Queue

**Symptoms:** Job is `Enqueued` but never transitions to `Processing`.

### Cause: Orphaned Worker

A previous worker crashed while processing this job. The job is stuck in `Processing` state.

**Diagnose:** Check `JobRecord.ProcessingStartedAt` and `JobRecord.HeartbeatAt`. If `UtcNow - HeartbeatAt > HeartbeatTimeout`, the job is orphaned.

**Fix:** The `OrphanedJobWatcherService` handles this automatically (default: 5 minutes). Wait for the orphan watcher to re-enqueue it, or manually reduce the timeout:

```csharp
options.HeartbeatTimeout = TimeSpan.FromMinutes(2); // Faster detection
```

---

## Deadline Expiring Unexpectedly

**Symptoms:** Jobs marked as `Expired` before you expected.

### Cause: Tight Deadline on Busy Queue

`deadlineAfter` is checked when the dispatcher picks up the job. If the queue is busy, jobs may wait longer than the deadline.

**Diagnose:** Check the job's `ExpiresAt` and compare to when it was actually picked up.

**Fix:** Increase the deadline or add more workers to reduce queue wait time.

```csharp
await scheduler.EnqueueAsync<MyJob>(
    deadlineAfter: TimeSpan.FromMinutes(30), // Increased from 5 minutes
    cancellationToken: ct);
```

### Cause: Deadline on Scheduled Job

`deadlineAfter` only works with `EnqueueAsync`. Scheduled jobs ignore the deadline.

**Diagnose:** Check if you used `ScheduleAsync` or `ScheduleAtAsync` with `deadlineAfter`.

**Fix:** Use `ScheduledAt` as your deadline — calculate the latest acceptable execution time at schedule time.

```csharp
// Instead of deadlineAfter, schedule at the latest acceptable time
var latestExecutionTime = DateTimeOffset.UtcNow.AddMinutes(30);
await scheduler.ScheduleAtAsync<MyJob>(latestExecutionTime, cancellationToken: ct);
```

---

## Duplicate Jobs

**Symptoms:** Same job executed multiple times with the same input.

### Cause: No Idempotency Key

Without an `idempotencyKey`, every `EnqueueAsync` creates a new job.

**Diagnose:** Check if `EnqueueAsync` was called multiple times without `idempotencyKey`.

**Fix:** Provide an idempotency key. See [Idempotency](17-Idempotency.md).

```csharp
await scheduler.EnqueueAsync<ProcessOrderJob, ProcessOrderInput>(
    new ProcessOrderInput(orderId),
    idempotencyKey: $"order-{orderId}",
    cancellationToken: ct);
```

### Cause: Retry Re-Executing Same Work

A job partially completes an external action (e.g., charges a card) then throws. On retry, it charges again.

**Diagnose:** Check the dead-letter handler or job logs for duplicate external calls.

**Fix:** Make the job idempotent by checking state before acting.

```csharp
public async Task ExecuteAsync(PaymentInput input, CancellationToken ct)
{
    var alreadyCharged = await _db.Payments.ExistsAsync(input.OrderId, ct);
    if (alreadyCharged) return; // Skip if already done

    await _db.Payments.ChargeAsync(input.OrderId, input.Amount, ct);
}
```

---

## Dashboard Not Showing Jobs

**Symptoms:** Dashboard is empty or shows no data.

### Cause: Storage Mismatch

The dashboard reads from the same storage as the dispatcher. If they use different connection strings, the dashboard sees nothing.

**Diagnose:** Check that `AddNexJob()` and the dashboard share the same storage configuration.

**Fix:** Ensure both use the same provider and connection string.

### Cause: No Jobs in Storage

Jobs may have been purged by retention policies.

**Diagnose:** Check `RetentionSucceeded`, `RetentionFailed`, `RetentionExpired` settings.

**Fix:** Increase retention periods or check for recent jobs:

```csharp
options.RetentionSucceeded = TimeSpan.FromDays(14); // Keep longer
```

### Cause: Dashboard Route Not Enabled

**Diagnose:** Check if `UseNexJobDashboard()` or `AddNexJobStandaloneDashboard()` is called.

**Fix:**

```csharp
// ASP.NET Core
app.UseNexJobDashboard();

// Worker Service
builder.Services.AddNexJobStandaloneDashboard();
```

---

## Next Steps

- [Idempotency](17-Idempotency.md) — Prevent duplicates
- [Dashboard](10-Dashboard.md) — Monitor and debug visually
- [Best Practices](13-Best-Practices.md) — Avoid these issues proactively
