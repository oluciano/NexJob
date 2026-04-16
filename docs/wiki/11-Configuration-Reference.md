# Configuration Reference

All NexJob options, settings, and configuration patterns.

---

## NexJobOptions (Code Configuration)

```csharp
builder.Services.AddNexJob(options =>
{
    // Worker concurrency
    options.Workers = 10; // Default: 10

    // Max retry attempts (global)
    options.MaxAttempts = 10; // Default: 10

    // Polling interval when no wake-up signal
    options.PollingInterval = TimeSpan.FromSeconds(15); // Default: 15s

    // Heartbeat interval (how often processing jobs update their heartbeat)
    options.HeartbeatInterval = TimeSpan.FromSeconds(30); // Default: 30s

    // Heartbeat timeout (when orphan watcher re-enqueues a job)
    options.HeartbeatTimeout = TimeSpan.FromMinutes(5); // Default: 5min

    // Graceful shutdown timeout
    options.ShutdownTimeout = TimeSpan.FromSeconds(30); // Default: 30s

    // Queues to process
    options.Queues = new[] { "default", "emails", "reports" }; // Default: ["default"]

    // Retention policies (auto-purge old jobs)
    options.RetentionSucceeded = TimeSpan.FromDays(7);  // Default: 7 days
    options.RetentionFailed = TimeSpan.FromDays(30);    // Default: 30 days
    options.RetentionExpired = TimeSpan.FromDays(7);    // Default: 7 days
    options.RetentionInterval = TimeSpan.FromHours(1);  // How often to purge

    // TTL for distributed throttle slots in Redis.
    // Must exceed your longest expected job execution time.
    // Only relevant when UseDistributedThrottle() is enabled.
    options.DistributedThrottleTtl = TimeSpan.FromHours(1); // Default: 1 hour

    // Max lines in job execution log
    options.MaxJobLogLines = 200; // Default: 200

    // Custom retry delay factory
    options.RetryDelayFactory = new CustomRetryFactory();
});
```

---

## Queue-Specific Settings

Configure execution windows per queue.

```csharp
builder.Services.AddNexJob(options =>
{
    options.QueueSettings["notifications"] = new QueueOptions
    {
        // Only process between 8 AM and 6 PM
        ExecutionStartHour = 8,
        ExecutionEndHour = 18,
    };
});
```

Jobs enqueued to a queue outside its execution window will wait until the window opens.

---

## appsettings.json

```json
{
  "NexJob": {
    "Workers": 20,
    "MaxAttempts": 5,
    "PollingInterval": "00:00:10",
    "Queues": ["default", "emails"],
    "RetentionSucceeded": "7.00:00:00",
    "RetentionFailed": "30.00:00:00",
    "RetentionExpired": "7.00:00:00",
    "Dashboard": {
      "Enabled": true
    },
    "RecurringJobs": [
      {
        "Job": "CleanupJob",
        "Cron": "0 2 * * *",
        "TimeZoneId": "America/New_York"
      },
      {
        "Job": "ReportJob",
        "Input": { "ReportType": "daily" },
        "Cron": "0 9 * * *"
      }
    ]
  }
}
```

Register:

```csharp
builder.Services.AddNexJob(builder.Configuration);
```

Or combined:

```csharp
builder.Services.AddNexJob(builder.Configuration, options =>
{
    options.Workers = 30; // Overrides appsettings
});
```

---

## Runtime Settings

Modifiable at runtime via dashboard or API. Persisted in storage.

| Setting | Description |
|---|---|
| `Workers` | Override worker count without restart |
| `PausedQueues` | Pause specific queues |
| `RecurringJobsPaused` | Pause all recurring job scheduling |
| `PollingInterval` | Change poll frequency at runtime |
| `RetentionSucceeded` | Adjust retention for succeeded jobs |
| `RetentionFailed` | Adjust retention for failed jobs |
| `RetentionExpired` | Adjust retention for expired jobs |

Changes apply on the next dispatcher cycle.

---

## Dashboard Options

```csharp
// ASP.NET Core
app.UseNexJobDashboard("/dashboard", options =>
{
    options.Title = "My Jobs";
    options.PollIntervalSeconds = 5;
});

// Standalone
builder.Services.AddNexJobStandaloneDashboard(options =>
{
    options.Port = 5005;
    options.Host = "localhost";
    options.Title = "NexJob Dashboard";
});
```

---

## Next Steps

- [Storage Providers](09-Storage-Providers.md) — Configure storage
- [Dashboard](10-Dashboard.md) — Dashboard configuration
- [Best Practices](13-Best-Practices.md) — Production configuration guidelines
