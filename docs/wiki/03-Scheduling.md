# Scheduling

Control when and how jobs execute: immediate enqueue, delayed schedule, priority ordering, and deadline enforcement.

---

## Immediate Enqueue

```csharp
await scheduler.EnqueueAsync<SendEmailJob>(cancellationToken: ct);
```

With input:

```csharp
await scheduler.EnqueueAsync<SendEmailJob, SendEmailInput>(
    new SendEmailInput("user@example.com", "Welcome!"),
    cancellationToken: ct);
```

The job transitions to `Enqueued` and is picked up by the dispatcher on the next wake-up signal or poll cycle.

---

## Scheduled Execution

### Delay with TimeSpan

```csharp
// Run in 30 minutes
await scheduler.ScheduleAsync<SendReminderJob>(
    delay: TimeSpan.FromMinutes(30),
    cancellationToken: ct);
```

### Schedule at Specific Time

```csharp
// Run tomorrow at 8 AM UTC
var runAt = DateTimeOffset.UtcNow.AddDays(1).Date.AddHours(8);
await scheduler.ScheduleAtAsync<GenerateReportJob>(
    runAt: runAt,
    cancellationToken: ct);
```

With input:

```csharp
await scheduler.ScheduleAtAsync<GenerateReportJob, ReportInput>(
    input: new ReportInput("monthly"),
    runAt: runAt,
    cancellationToken: ct);
```

The job is stored as `Scheduled` with `ScheduledAt` set. The dispatcher will not pick it up until `UtcNow >= ScheduledAt`.

---

## Priority

Jobs have four priority levels. Default is `Normal`.

```csharp
await scheduler.EnqueueAsync<CriticalAlertJob>(
    priority: JobPriority.Critical,
    cancellationToken: ct);

await scheduler.EnqueueAsync<BackgroundSyncJob>(
    priority: JobPriority.Low,
    cancellationToken: ct);
```

| Priority | Value | Use Case |
|---|---|---|
| `Critical` | 1 | Alerts, payment failures |
| `High` | 2 | User-facing operations |
| `Normal` | 3 | Default — emails, notifications |
| `Low` | 4 | Cleanup, archival, analytics |

The dispatcher fetches jobs in priority order (lowest number = highest priority). Within the same priority, jobs are processed FIFO.

---

## Queues

Route jobs to different processing pipelines.

```csharp
// Enqueue to a specific queue
await scheduler.EnqueueAsync<HeavyComputationJob>(
    queue: "compute",
    cancellationToken: ct);

await scheduler.EnqueueAsync<SendEmailJob>(
    queue: "notifications",
    cancellationToken: ct);
```

Configure which queues the dispatcher processes:

```csharp
builder.Services.AddNexJob(options =>
{
    options.Queues = new[] { "default", "notifications", "compute" };
});
```

**Rule:** A dispatcher only processes queues listed in its `Queues` configuration. Deploy different worker instances with different queue configurations for workload isolation.

---

## Deadline

Jobs expire if not executed within the specified time.

```csharp
// This job expires if not picked up within 10 minutes
await scheduler.EnqueueAsync<SendPromotionalEmailJob>(
    deadlineAfter: TimeSpan.FromMinutes(10),
    cancellationToken: ct);
```

When the deadline passes, the job is marked as `Expired` and **never executes**. See [Mental Model](00-Mental-Model.md#deadline-behavior) for details.

**Important:** `deadlineAfter` is only supported for immediate enqueue (`EnqueueAsync`). Scheduled jobs (`ScheduleAsync`, `ScheduleAtAsync`) do not support deadlines — use `ScheduledAt` as your deadline instead.

---

## Idempotency Key

Prevent duplicate jobs by providing a unique key.

```csharp
// Only one job with this key can be active at a time
await scheduler.EnqueueAsync<ProcessOrderJob, ProcessOrderInput>(
    input: new ProcessOrderInput(orderId),
    idempotencyKey: $"order-{orderId}",
    cancellationToken: ct);
```

See [Idempotency](17-Idempotency.md) for duplicate policies.

---

## Tags

Attach metadata for querying later.

```csharp
await scheduler.EnqueueAsync<ProcessOrderJob, ProcessOrderInput>(
    input: new ProcessOrderInput(orderId),
    tags: new[] { "order", orderId.ToString() },
    cancellationToken: ct);

// Query later
var orderJobs = await scheduler.GetJobsByTagAsync(orderId.ToString(), ct);
```

---

## `IScheduler` Method Reference & Cheat Sheet

The table below summarizes all scheduling methods and available optional parameters:

| Method | Purpose | Key Arguments |
|---|---|---|
| `EnqueueAsync<TJob>(...)` | Run immediately (no input) | `queue`, `priority`, `idempotencyKey`, `duplicatePolicy`, `tags`, `deadlineAfter`, `ct` |
| `EnqueueAsync<TJob, TInput>(input, ...)` | Run immediately with typed input | `input`, `queue`, `priority`, `idempotencyKey`, `duplicatePolicy`, `tags`, `deadlineAfter`, `ct` |
| `ScheduleAsync<TJob>(delay, ...)` | Run after a relative time delay | `delay` (`TimeSpan`), `queue`, `idempotencyKey`, `ct` |
| `ScheduleAsync<TJob, TInput>(input, delay, ...)` | Run with input after a time delay | `input`, `delay` (`TimeSpan`), `queue`, `idempotencyKey`, `ct` |
| `ScheduleAtAsync<TJob>(runAt, ...)` | Run at a fixed UTC timestamp | `runAt` (`DateTimeOffset`), `queue`, `idempotencyKey`, `ct` |
| `ScheduleAtAsync<TJob, TInput>(input, runAt, ...)` | Run with input at a fixed UTC timestamp | `input`, `runAt` (`DateTimeOffset`), `queue`, `idempotencyKey`, `ct` |
| `AddOrUpdateRecurringJobAsync<TJob>(...)` | Schedule a recurring cron job | `recurringJobId`, `cron`, `timeZone`, `queue`, `concurrencyPolicy`, `ct` |
| `AddOrUpdateRecurringJobAsync<TJob, TInput>(...)`| Recurring cron job with typed payload | `recurringJobId`, `input`, `cron`, `timeZone`, `queue`, `concurrencyPolicy`, `ct` |

### Parameter Quick Reference
- **`queue`** *(string?)*: Queue name. Defaults to `"default"`.
- **`priority`** *(JobPriority)*: `JobPriority.Critical` (1), `High` (2), `Normal` (3 - default), `Low` (4).
- **`idempotencyKey`** *(string?)*: Unique deduplication key. Returns existing `JobId` if active.
- **`duplicatePolicy`** *(DuplicatePolicy)*: `AllowAfterFailed` (default), `AllowAfterTerminal`, `RejectAlways`.
- **`deadlineAfter`** *(TimeSpan?)*: Maximum time before job expires if not picked up by a worker.
- **`tags`** *(IReadOnlyList&lt;string&gt;?)*: Searchable metadata tags for dashboard and querying.

---

## Next Steps

- [Recurring Jobs](04-Recurring-Jobs.md) — Cron-based recurring execution
- [Continuations](05-Continuations.md) — Chain jobs together
- [Idempotency](17-Idempotency.md) — Prevent duplicate execution
