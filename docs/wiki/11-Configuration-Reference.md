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

    // Deferral delay before foreign jobs become visible again for other workers
    options.ForeignJobRetryDelay = TimeSpan.FromSeconds(5); // Default: 5s

    // Heartbeat interval (how often processing jobs update their heartbeat)
    options.HeartbeatInterval = TimeSpan.FromSeconds(30); // Default: 30s

    // Heartbeat timeout (when orphan watcher re-enqueues a job)
    options.HeartbeatTimeout = TimeSpan.FromMinutes(5); // Default: 5min

    // Graceful shutdown: how long running jobs get to finish. Keep HostOptions.ShutdownTimeout above this value.
    options.ShutdownTimeout = TimeSpan.FromSeconds(30); // Default: 30s

    // Identity of this node in the dashboard (default: generated from machine name and process)
    options.ServerId = "worker-eu-1";

    // How often this node reports its heartbeat to storage
    options.ServerHeartbeatInterval = TimeSpan.FromSeconds(15); // Default: 15s

    // Health check thresholds (see NexJobHealthCheck)
    options.HealthCheckTimeout = TimeSpan.FromSeconds(5);       // Default: 5s
    options.HealthCheckFailedThreshold = 100;                   // Default: 100

    // Queues to process
    options.Queues = new[] { "default", "emails", "reports" }; // Default: ["default"]

    // Retention policies (auto-purge old jobs)
    options.RetentionSucceeded = TimeSpan.FromDays(7);   // Default: 7 days
    options.RetentionFailed = TimeSpan.FromDays(30);     // Default: 30 days
    options.RetentionExpired = TimeSpan.FromDays(7);     // Default: 7 days
    options.RetentionDeadLetter = TimeSpan.FromDays(60); // Default: 60 days
    options.RetentionInterval = TimeSpan.FromHours(1);   // How often to purge
    options.RetentionBatchSize = 1000;                   // Chunk size to prevent DB lock contention

    // Maximum time a job may hold a distributed throttle slot in Redis. Slots of crashed nodes are reclaimed after
    // 3 x HeartbeatInterval regardless; this only caps a live job. Must exceed your longest job execution time.
    // Only relevant when AddNexJobDistributedThrottle() is enabled.
    options.DistributedThrottleTtl = TimeSpan.FromHours(1); // Default: 1 hour

    // Max lines in job execution log
    options.MaxJobLogLines = 200; // Default: 200

    // Enable high-throughput asynchronous batch acknowledgment
    // Aggregates completed job acknowledgments to cut database write roundtrips by 90%+
    options.EnableBatchAcknowledgment = true; // Default: false

    // Delay before the next attempt, for jobs without [Retry(InitialDelay = ...)].
    // Receives the number of attempts made so far. Default: attempt^4 + 15 seconds plus jitter.
    options.RetryDelayFactory = attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt));
});
```

### Which options can also come from `appsettings.json`

Options are read from the `NexJob` configuration section when you call `AddNexJob(configuration)`. Not all of them are:

| Option | `appsettings.json` key |
|---|---|
| `Workers`, `MaxAttempts`, `MaxJobLogLines`, `ServerId` | same name |
| `PollingInterval`, `HeartbeatInterval`, `ServerHeartbeatInterval`, `HeartbeatTimeout` | same name, as a `TimeSpan` string (`"00:00:10"`) |
| `HealthCheckTimeout`, `HealthCheckFailedThreshold` | same name |
| `Queues`, `QueueSettings`, `RecurringJobs` | same name |
| `ShutdownTimeout` | `ShutdownTimeoutSeconds` (a number of seconds) |
| Dashboard settings | `Dashboard` object (`Path`, `Title`, `PollIntervalSeconds`, `Port`, `LocalhostOnly`) |
| `ForeignJobRetryDelay`, `RetentionSucceeded`, `RetentionFailed`, `RetentionExpired`, `RetentionDeadLetter`, `RetentionInterval`, `RetentionBatchSize`, `DistributedThrottleTtl`, `EnableBatchAcknowledgment`, `RetryDelayFactory` | **code only** (`AddNexJob(options => ...)`); retention can also be adjusted at runtime from the dashboard settings page |

A key that is not in this table is ignored silently, so a typo in `appsettings.json` has no effect.

---

## Queue-Specific Settings

Use `ConfigureQueue` to give a queue an execution window and/or a circuit breaker (see [Throttling](07-Throttling.md#queue-level-dynamic-circuit-breaker)).

```csharp
using NexJob.Configuration;

builder.Services.AddNexJob(options =>
{
    options.ConfigureQueue("notifications", queue =>
    {
        // Only process between 8 AM and 6 PM, São Paulo time
        queue.ExecutionWindow = new ExecutionWindowSettings
        {
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(18, 0),
            TimeZone = "America/Sao_Paulo",   // default "UTC"
        };
    });
});
```

- A window can cross midnight (`StartTime` after `EndTime`, for example 22:00 to 06:00).
- Jobs enqueued to a queue outside its window stay `Enqueued` and are fetched when the window opens.
- `ConfigureQueue` called twice for the same queue name edits the same settings; the name match ignores case.

---

## appsettings.json

```json
{
  "NexJob": {
    "Workers": 20,
    "MaxAttempts": 5,
    "PollingInterval": "00:00:10",
    "ShutdownTimeoutSeconds": 45,
    "Queues": ["default", "emails"],
    "Dashboard": {
      "Path": "/dashboard",
      "Title": "My Jobs",
      "Port": 5005
    },
    "RecurringJobs": [
      {
        "Job": "CleanupJob",
        "Cron": "0 2 * * *",
        "TimeZoneId": "America/New_York"
      },
      {
        "Job": "ReportJob",
        "Input": "{\"ReportType\": \"daily\"}",
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

> [!NOTE]
> Retention periods (`RetentionSucceeded`, `RetentionFailed`, ...) are **not** read from `appsettings.json`; set them in code as shown above, or change them at runtime on the dashboard settings page.

---

## Environment Variables (Docker & Kubernetes / 12-Factor Apps)

NexJob is built for cloud-native deployments. Configuration does not need to live in `appsettings.json` — all options can be supplied via environment variables in Docker and Kubernetes.

### 1. Hierarchical .NET Mapping (`NexJob__*`)
Any `NexJob` configuration key can be supplied using the standard .NET double-underscore (`__`) syntax:

```bash
# In Dockerfile, docker-compose.yml, or Kubernetes Deployment manifest:
export NexJob__Workers=20
export NexJob__PollingInterval="00:00:10"
export NexJob__MaxAttempts=5
export NexJob__ShutdownTimeoutSeconds=45
export NexJob__Dashboard__Port=5005
```

When registering via `IConfiguration`:
```csharp
builder.Services.AddNexJob(builder.Configuration);
```
.NET binds the environment variables into the same settings as `appsettings.json`, so only the keys in the table above are honoured.

### 2. Direct Retrieval (`Environment.GetEnvironmentVariable`)
Options actions allow direct environment variable retrieval without requiring configuration providers:

```csharp
builder.Services.AddNexJob(options =>
{
    if (int.TryParse(Environment.GetEnvironmentVariable("NEXJOB_WORKERS"), out var workers))
    {
        options.Workers = workers;
    }
});
```

### 3. Broker Integrations (Kafka & RabbitMQ)
NexJob does not read any broker-specific environment variable by itself (there is no built-in `KAFKA_*` or `RABBITMQ_*` convention). Read the values you choose and assign them to the trigger or producer options:

```csharp
builder.Services.AddNexJob()
    .AddKafkaTrigger(options =>
    {
        options.BootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS") ?? "localhost:9092";
        options.Topic = Environment.GetEnvironmentVariable("KAFKA_TOPIC") ?? "orders";
        options.GroupId = "orders-consumer";
    })
    .AddRabbitMqTrigger(options =>
    {
        options.HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
        options.QueueName = "orders";
    });
```

---

## Runtime Settings

Modifiable at runtime via dashboard or API. Persisted in storage.

| Setting | Description |
|---|---|
| `PausedQueues` | Pause specific queues |
| `RecurringJobsPaused` | Pause all recurring job scheduling |
| `PollingInterval` | Change poll frequency at runtime |
| `RetentionSucceeded` | Adjust retention for succeeded jobs |
| `RetentionFailed` | Adjust retention for failed jobs |
| `RetentionExpired` | Adjust retention for expired jobs |
| `RetentionDeadLetter` | Adjust retention for dead-letter jobs |
| `RetentionBatchSize` | Maximum rows deleted per batch during purge (no field on the dashboard page; set it through `IRuntimeSettingsStore`) |

> **The worker count is not a runtime setting.** It is sized from `NexJobOptions.Workers` when the host starts, so changing it means changing the configuration and redeploying. The dashboard has no control for it, and a `Workers` value saved by an older version is ignored.
>
> **Settings that are accepted but have no effect:** `QueueSettings[].Workers` (there is no per-queue pool) and a `DefaultQueue` other than `default`. NexJob logs a warning at startup when either is configured.

The other settings apply on the next dispatcher, scheduler or retention cycle.

---

## Dashboard Options

```csharp
// ASP.NET Core (DashboardOptions)
app.UseNexJobDashboard("/dashboard", options =>
{
    options.Title = "My Jobs";                            // default "NexJob"
    options.MetricsCacheTtl = TimeSpan.FromSeconds(3);    // default 3s
    options.Queues = ["default", "emails"];               // optional: scope the dashboard to these queues
});

// Standalone (StandaloneDashboardOptions)
builder.Services.AddNexJobStandaloneDashboard(options =>
{
    options.Port = 5005;                 // default 5005
    options.Path = "/dashboard";         // default "/dashboard"
    options.Title = "NexJob Dashboard";  // default "NexJob"
    options.LocalhostOnly = true;        // default true: loopback only
    options.PollIntervalSeconds = 3;     // default 3
    options.DisableWorkers = false;      // true = ops-only host that processes no jobs
    options.Queues = null;               // optional queue scope
});
```

---

## Next Steps

- [Storage Providers](09-Storage-Providers.md) — Configure storage
- [Dashboard](10-Dashboard.md) — Dashboard configuration
- [Best Practices](13-Best-Practices.md) — Production configuration guidelines
