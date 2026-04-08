# Recurring Jobs

Schedule jobs to run on a cron expression. Each firing creates a new `JobRecord` — recurring jobs are schedules, not persistent executions.

---

## Code-Based Configuration

### Simple Recurring Job

```csharp
builder.Services.AddNexJob(options =>
{
    // Run cleanup every day at 2 AM
    options.AddRecurringJob<CleanupOldLogsJob>(
        recurringJobId: "cleanup-daily",
        cron: "0 2 * * *");
});
```

### With Input

```csharp
builder.Services.AddNexJob(options =>
{
    options.AddRecurringJob<GenerateReportJob, ReportInput>(
        recurringJobId: "weekly-report",
        cron: "0 9 * * 1", // Monday 9 AM
        input: new ReportInput("weekly"));
});
```

### With Time Zone

```csharp
builder.Services.AddNexJob(options =>
{
    options.AddRecurringJob<SendDailyDigestJob>(
        recurringJobId: "daily-digest",
        cron: "0 8 * * *",
        timeZone: TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
});
```

### With Queue

```csharp
builder.Services.AddNexJob(options =>
{
    options.AddRecurringJob<HeavyAnalyticsJob>(
        recurringJobId: "analytics-hourly",
        cron: "0 * * * *",
        queue: "compute");
});
```

---

## Configuration via appsettings.json

Define recurring jobs in configuration instead of code.

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
        "Input": { "ReportType": "weekly" },
        "Cron": "0 9 * * 1",
        "Queue": "reports"
      }
    ]
  }
}
```

Register with configuration:

```csharp
builder.Services.AddNexJob(builder.Configuration);
```

Or combined with code options:

```csharp
builder.Services.AddNexJob(builder.Configuration, options =>
{
    options.Workers = 20;
});
```

**Rule:** The `Job` field must match the class name (not fully qualified). Input is deserialized using the job's `IJob<T>` input type.

---

## Concurrency Policy

Control what happens when a recurring job fires while the previous instance is still running.

```csharp
builder.Services.AddNexJob(options =>
{
    // Default: skip if the previous instance is still running
    options.AddRecurringJob<SlowSyncJob>(
        recurringJobId: "slow-sync",
        cron: "*/5 * * * *",
        concurrencyPolicy: RecurringConcurrencyPolicy.SkipIfRunning);

    // Allow concurrent executions
    options.AddRecurringJob<IndependentTaskJob>(
        recurringJobId: "independent-task",
        cron: "0 * * * *",
        concurrencyPolicy: RecurringConcurrencyPolicy.AllowConcurrent);
});
```

| Policy | Behavior |
|---|---|
| `SkipIfRunning` (default) | Uses idempotency key `recurring:{Id}` — skips if previous instance is still active |
| `AllowConcurrent` | No deduplication — each firing creates a new `JobRecord` |

---

## Removing Recurring Jobs

```csharp
await scheduler.RemoveRecurringAsync("cleanup-daily", ct);
```

This removes the schedule — it does not affect already-created `JobRecord` instances.

---

## How It Works

1. `RecurringJobSchedulerService` polls every `PollingInterval` (default: 15s)
2. Finds recurring jobs where `NextExecution <= UtcNow`
3. Acquires a distributed lock to prevent duplicate firings across nodes
4. Creates a new `JobRecord` and enqueues it
5. Calculates the next execution time

**Implication:** Each firing is an independent job with its own lifecycle, retries, and dead-letter handling. If one firing fails, it does not affect the next scheduled firing.

---

## Next Steps

- [Continuations](05-Continuations.md) — Chain jobs after each other
- [Configuration Reference](11-Configuration-Reference.md) — All recurring options
- [Dashboard](10-Dashboard.md) — Monitor recurring job executions
