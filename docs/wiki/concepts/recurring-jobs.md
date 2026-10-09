---
title: "Recurring Jobs: Cron-Based Job Scheduling in NexJob"
sidebarTitle: "Recurring Jobs"
description: "Schedule recurring background jobs in NexJob using cron expressions in code or appsettings.json, with full timezone, queue, and concurrency support."
---

Recurring jobs in NexJob run on a cron schedule. Each time the schedule fires, NexJob creates a new `JobRecord` — recurring jobs are schedules, not persistent long-running processes. This means every firing gets its own lifecycle, its own retry budget, and its own dead-letter handling, completely independent of the previous firing.

## Code-Based Registration

The most common way to register recurring jobs is inside `AddNexJob` at startup. These registrations are applied on every application start.

### Basic Recurring Job

```csharp
builder.Services.AddNexJob(options =>
{
    // Run cleanup every day at 2 AM UTC
    options.AddRecurringJob<CleanupOldLogsJob>(
        id: "cleanup-daily",
        cron: "0 2 * * *");
});
```

### With Typed Input

Pass an input value to a job that implements `IJob<TInput>`:

```csharp
builder.Services.AddNexJob(options =>
{
    options.AddRecurringJob<GenerateReportJob, ReportInput>(
        id: "weekly-report",
        cron: "0 9 * * 1",   // Every Monday at 9 AM
        input: new ReportInput("weekly"));
});
```

### With a Time Zone

Evaluate the cron expression in a specific time zone instead of UTC:

```csharp
builder.Services.AddNexJob(options =>
{
    options.AddRecurringJob<SendDailyDigestJob>(
        id: "daily-digest",
        cron: "0 8 * * *",
        timeZoneId: "America/New_York");
});
```

### With a Dedicated Queue

Route recurring job firings to a specific queue:

```csharp
builder.Services.AddNexJob(options =>
{
    options.AddRecurringJob<HeavyAnalyticsJob>(
        id: "analytics-hourly",
        cron: "0 * * * *",
        queue: "compute");
});
```

!!! tip
    Use [crontab.guru](https://crontab.guru) to validate and understand your cron expressions before deploying. NexJob supports standard 5-field cron expressions as well as 6-field expressions with a seconds field.


## Configuration via appsettings.json

You can declare recurring jobs in `appsettings.json` instead of code. This is useful when you want operations teams to adjust schedules without redeploying.

```json
{
  "NexJob": {
    "RecurringJobs": [
      {
        "Job": "CleanupOldLogsJob",
        "Cron": "0 2 * * *",
        "TimeZoneId": "America/New_York",
        "Queue": "default"
      },
      {
        "Job": "GenerateReportJob",
        "Input": "{\"ReportType\": \"weekly\"}",
        "Cron": "0 9 * * 1",
        "Queue": "reports"
      }
    ]
  }
}
```

Register with configuration:

```csharp
// Configuration only
builder.Services.AddNexJob(builder.Configuration);

// Combined with code options
builder.Services.AddNexJob(builder.Configuration, options =>
{
    options.Workers = 20;
});
```

!!! note
    **Configuration rules to keep in mind:**
      - The `Job` field must match the class name exactly (not the fully qualified name). If two jobs share the same class name, give each entry an explicit `Id` field.
      - `Input` is a JSON string with escaped inner quotes — it is **not** a nested JSON object.
      - Available per-entry fields: `Id`, `Job`, `Cron`, `Input`, `Queue` (defaults to the [default queue](queues.md#the-default-queue), `{prefix}.default`), `TimeZoneId`, `ConcurrencyPolicy` (defaults to `SkipIfRunning`), and `Enabled` (defaults to `true`).


### Behavior on Application Restart

The configuration is applied on **every** start:

- If you change `Cron`, `Queue`, `Input`, or `TimeZoneId`, the stored schedule is updated after the next restart.
- Changes made via the dashboard (pausing a job, overriding a cron) are preserved and are **not** overwritten by the configuration file.
- A newly registered (or re-applied) job fires at its **next cron occurrence** — it does not fire immediately on startup.
- An invalid `Cron` or unknown `TimeZoneId` causes that single entry to fail registration. The error is logged and the remaining entries are processed normally.

## Concurrency Policy

Control what happens when a new cron firing occurs while the previous instance of the same job is still executing.

```csharp
builder.Services.AddNexJob(options =>
{
    // Default: skip if the previous instance hasn't finished
    options.AddRecurringJob<SlowSyncJob>(
        id: "slow-sync",
        cron: "*/5 * * * *",
        concurrencyPolicy: RecurringConcurrencyPolicy.SkipIfRunning);

    // Allow both instances to run in parallel
    options.AddRecurringJob<IndependentTaskJob>(
        id: "independent-task",
        cron: "0 * * * *",
        concurrencyPolicy: RecurringConcurrencyPolicy.AllowConcurrent);
});
```

| Policy | Behavior |
|---|---|
| `SkipIfRunning` (default) | Uses idempotency key `recurring:{id}` — skips the new firing if a previous instance is still active |
| `AllowConcurrent` | No deduplication — every firing creates a new `JobRecord` regardless of running instances |

## Updating and Removing Recurring Jobs

### Update at Runtime

Call `RecurringAsync` on `IScheduler` to create or update a schedule at runtime without restarting the application:

```csharp
await scheduler.RecurringAsync<CleanupOldLogsJob>(
    recurringJobId: "cleanup-daily",
    cron: "0 3 * * *",   // Move from 2 AM to 3 AM
    cancellationToken: ct);
```

### Remove a Schedule

```csharp
await scheduler.RemoveRecurringAsync("cleanup-daily", ct);
```

Removing a schedule deletes the recurring definition only. Any `JobRecord` instances that were already created from previous firings are not affected.

## How the Scheduler Works

Understanding the firing mechanism helps you reason about timing and multi-node deployments:


### Periodic poll

NexJob polls the recurring job table every `PollingInterval` (default: 15 seconds).

### Find due schedules

It identifies any recurring jobs where `NextExecution <= UtcNow`.

### Acquire distributed lock

A distributed lock is acquired before firing to prevent duplicate `JobRecord` creation across multiple nodes or workers.

### Create and enqueue

A new `JobRecord` is created and placed in the configured queue with the `Enqueued` status.

### Calculate next execution

The `NextExecution` timestamp is recalculated from the cron expression and time zone, ready for the next poll.




Each fired `JobRecord` is fully independent: if one firing fails, its retries and dead-letter handling operate in isolation, and the next scheduled firing is unaffected.
