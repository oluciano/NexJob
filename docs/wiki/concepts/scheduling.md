---
title: "Scheduling Jobs: Enqueue, Delay, and Deadline Options"
sidebarTitle: "Scheduling"
description: "Enqueue jobs for immediate execution, schedule them at a future time, set priorities, and enforce deadlines with NexJob's IScheduler."
---

NexJob's `IScheduler` interface is your single entry point for dispatching background work. Inject it into any service, controller, or minimal API handler, then choose whether to run a job immediately, after a relative delay, or at a precise UTC timestamp. Every method returns the `JobId` of the created record, which you can use for continuations, tag queries, or status checks.

## Immediate Enqueue

Call `EnqueueAsync` to place a job in the queue right now. The dispatcher picks it up on the next wake-up cycle.

```csharp
// No input
await scheduler.EnqueueAsync<SendEmailJob>(cancellationToken: ct);

// With typed input
await scheduler.EnqueueAsync<SendEmailJob, SendEmailInput>(
    new SendEmailInput("user@example.com", "Welcome!"),
    cancellationToken: ct);
```

The job transitions to `Enqueued` state immediately and is eligible for processing straight away.

## Scheduled Execution

### Delay with TimeSpan

Use `ScheduleAsync` when you want the job to become eligible after a relative duration from now:

```csharp
// Run in 30 minutes
await scheduler.ScheduleAsync<SendReminderJob>(
    delay: TimeSpan.FromMinutes(30),
    cancellationToken: ct);

// With input
await scheduler.ScheduleAsync<SendReminderJob, ReminderInput>(
    input: new ReminderInput(userId),
    delay: TimeSpan.FromMinutes(30),
    cancellationToken: ct);
```

### Schedule at a Specific Time

Use `ScheduleAtAsync` when you need the job to run at a known UTC instant, such as the start of a business day:

```csharp
// Run tomorrow at 8 AM UTC
var runAt = new DateTimeOffset(
    DateTimeOffset.UtcNow.Date.AddDays(1).AddHours(8),
    TimeSpan.Zero);

await scheduler.ScheduleAtAsync<GenerateReportJob>(
    runAt: runAt,
    cancellationToken: ct);

// With input
await scheduler.ScheduleAtAsync<GenerateReportJob, ReportInput>(
    input: new ReportInput("monthly"),
    runAt: runAt,
    cancellationToken: ct);
```

Scheduled jobs are stored with status `Scheduled` and a `ScheduledAt` timestamp. The dispatcher will not pick them up until `UtcNow >= ScheduledAt`.

## Priority

Every job has a priority level that controls the order in which the dispatcher fetches work. The default is `Normal`. Within the same priority, jobs are processed FIFO.

```csharp
await scheduler.EnqueueAsync<CriticalAlertJob>(
    priority: JobPriority.Critical,
    cancellationToken: ct);

await scheduler.EnqueueAsync<BackgroundSyncJob>(
    priority: JobPriority.Low,
    cancellationToken: ct);
```

| Priority | Value | Typical Use Case |
|---|---|---|
| `Critical` | 1 | Payment failures, security alerts |
| `High` | 2 | User-facing operations needing fast response |
| `Normal` | 3 | Default — emails, notifications, webhooks |
| `Low` | 4 | Cleanup, archival, analytics, batch imports |

The dispatcher fetches jobs in ascending value order, so `Critical` (1) is always processed before `Low` (4).

## Queue Routing

Route jobs to dedicated processing pipelines by specifying a queue name. This lets you isolate workloads — for example, keeping heavy computation off the same workers that serve user-facing notifications.

```csharp
await scheduler.EnqueueAsync<HeavyComputationJob>(
    queue: "compute",
    cancellationToken: ct);

await scheduler.EnqueueAsync<SendEmailJob>(
    queue: "notifications",
    cancellationToken: ct);
```

Configure which queues each dispatcher instance monitors:

```csharp
builder.Services.AddNexJob(options =>
{
    options.Queues = new[] { "default", "notifications", "compute" };
});
```

A dispatcher only processes the queues listed in its `Queues` configuration. Deploy separate worker instances with different queue lists to achieve workload isolation. See [Queues](../concepts/queues.md) for fetch order, per-queue settings and runtime control.

## Deadlines

Use `deadlineAfter` to mark a job as expired if it has not started executing within the given window. This is useful for time-sensitive notifications that have no value once the window has passed.

```csharp
// Expire this job if it hasn't started within 10 minutes
await scheduler.EnqueueAsync<SendPromotionalEmailJob>(
    deadlineAfter: TimeSpan.FromMinutes(10),
    cancellationToken: ct);
```

!!! warning
    A job past its deadline is marked `Expired` and **never executes** — it does not retry and no dead-letter handler is called. Only use `deadlineAfter` when skipping the work is an acceptable outcome.

      `deadlineAfter` is supported only on `EnqueueAsync`. Scheduled jobs (`ScheduleAsync`, `ScheduleAtAsync`) do not accept a deadline — use the `runAt` timestamp as your natural deadline instead.


## Idempotency Keys and Tags

Attach an `idempotencyKey` to prevent the same logical job from being enqueued twice, and use `tags` to attach searchable metadata:

```csharp
await scheduler.EnqueueAsync<ProcessOrderJob, ProcessOrderInput>(
    input: new ProcessOrderInput(orderId),
    idempotencyKey: $"order-{orderId}",
    tags: new[] { "order", orderId.ToString() },
    cancellationToken: ct);

// Retrieve by tag later
var orderJobs = await scheduler.GetJobsByTagAsync(orderId.ToString(), ct);
```

See [Idempotency](../concepts/idempotency.md) for the full deduplication rules and `DuplicatePolicy` options.

## IScheduler Method Cheat Sheet

The table below lists every method on `IScheduler` and its key parameters:

| Method | Purpose | Key Parameters |
|---|---|---|
| `EnqueueAsync<TJob>(...)` | Run immediately, no input | `queue`, `priority`, `idempotencyKey`, `duplicatePolicy`, `tags`, `deadlineAfter`, `ct` |
| `EnqueueAsync<TJob, TInput>(input, ...)` | Run immediately with typed input | `input`, `queue`, `priority`, `idempotencyKey`, `duplicatePolicy`, `tags`, `deadlineAfter`, `ct` |
| `ScheduleAsync<TJob>(delay, ...)` | Run after a relative delay | `delay` (`TimeSpan`), `queue`, `idempotencyKey`, `ct` |
| `ScheduleAsync<TJob, TInput>(input, delay, ...)` | Run with input after a delay | `input`, `delay` (`TimeSpan`), `queue`, `idempotencyKey`, `ct` |
| `ScheduleAtAsync<TJob>(runAt, ...)` | Run at a fixed UTC time | `runAt` (`DateTimeOffset`), `queue`, `idempotencyKey`, `ct` |
| `ScheduleAtAsync<TJob, TInput>(input, runAt, ...)` | Run with input at a fixed UTC time | `input`, `runAt` (`DateTimeOffset`), `queue`, `idempotencyKey`, `ct` |
| `RecurringAsync<TJob>(id, cron, ...)` | Create or update a cron schedule | `recurringJobId`, `cron`, `timeZone`, `queue`, `concurrencyPolicy`, `ct` |
| `RecurringAsync<TJob, TInput>(id, input, cron, ...)` | Cron schedule with typed input | `recurringJobId`, `input`, `cron`, `timeZone`, `queue`, `concurrencyPolicy`, `ct` |
| `RemoveRecurringAsync(id, ct)` | Delete a recurring schedule | `recurringJobId`, `ct` |
| `ContinueWithAsync<TJob>(parentJobId, ...)` | Run after parent job succeeds | `parentJobId`, `queue`, `ct` |
| `ContinueWithAsync<TJob, TInput>(parentJobId, input, ...)` | Run with input after parent succeeds | `parentJobId`, `input`, `queue`, `ct` |
| `GetJobsByTagAsync(tag, ct)` | Find jobs by tag | `tag`, `ct` |

### Parameter Quick Reference

<div class="grid cards" markdown>
  -   **queue**

    Queue name. Defaults to `"default"` when `null`.

  -   **priority**

    `Critical` (1), `High` (2), `Normal` (3, default), `Low` (4).

  -   **idempotencyKey**

    Unique deduplication key. Returns the existing `JobId` if the job is still active.

  -   **duplicatePolicy**

    `AllowAfterFailed` (default), `RejectIfFailed`, `RejectAlways`.

  -   **deadlineAfter**

    Max time before the job expires if not picked up. `EnqueueAsync` only.

  -   **tags**

    Searchable string labels for dashboard filtering and `GetJobsByTagAsync`.

</div>
