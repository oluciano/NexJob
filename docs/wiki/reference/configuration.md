---
title: "NexJob Configuration Reference: All Options Explained"
sidebarTitle: "Configuration"
description: "Complete reference for NexJobOptions, appsettings.json configuration, environment variables, queue settings, runtime settings, and dashboard options."
---

NexJob exposes every tunable through `NexJobOptions`, a flat class you configure in code, in `appsettings.json`, or through environment variables. This page covers all options, their defaults, which ones you can change without a restart, and how to configure the dashboard in both ASP.NET Core and standalone worker hosts.

## NexJobOptions: Full Code Configuration

Pass a delegate to `AddNexJob` to configure options in code. Code configuration supports every option — a few options, such as `RetryDelayFactory`, `RetentionInterval`, `RetentionBatchSize`, `EnableBatchAcknowledgment`, `ForeignJobRetryDelay`, and all retention periods, are **code-only** and cannot be set through `appsettings.json`.

```csharp
builder.Services.AddNexJob(options =>
{
    // ── Concurrency ─────────────────────────────────────────────────────────
    // Maximum number of jobs that execute concurrently on this host.
    // Each worker runs in its own Task. Keep this below your storage
    // connection pool size to avoid contention.
    // 0 means that this host does not fetch or execute jobs (see DisableWorkers below).
    options.Workers = 10; // Default: 10

    // ── Identity ─────────────────────────────────────────────────────────────
    // Human-readable name for this node shown in the dashboard.
    // Defaults to MachineName + Guid when null.
    options.ServerId = "worker-eu-1";

    // ── Retry ────────────────────────────────────────────────────────────────
    // Total executions allowed before a job is moved to dead-letter.
    // MaxAttempts = 3 means the job runs at most 3 times total, not 3 retries.
    options.MaxAttempts = 10; // Default: 10

    // Custom retry delay factory. Receives the attempt number (1-based).
    // Default: attempt^4 + 15 + random(30) × (attempt+1) seconds.
    // Set to TimeSpan.Zero in tests to eliminate delays.
    options.RetryDelayFactory = attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt));

    // ── Polling ──────────────────────────────────────────────────────────────
    // How often the dispatcher polls storage when no wake-up signal arrives.
    // Local enqueues wake the dispatcher immediately; this only affects
    // jobs enqueued from other nodes or external processes.
    options.PollingInterval = TimeSpan.FromSeconds(15); // Default: 15s

    // Delay before a foreign job (one whose type is not loaded in this
    // process) is released back to storage for another node to pick up.
    options.ForeignJobRetryDelay = TimeSpan.FromSeconds(5); // Default: 5s

    // ── Heartbeat ────────────────────────────────────────────────────────────
    // How often active workers refresh their heartbeat timestamp.
    options.HeartbeatInterval = TimeSpan.FromSeconds(30); // Default: 30s

    // How often this node refreshes its own server-level heartbeat.
    options.ServerHeartbeatInterval = TimeSpan.FromSeconds(15); // Default: 15s

    // When a Processing job's heartbeat is older than this, the orphan
    // watcher re-enqueues it. Must be greater than your longest job's
    // expected duration to avoid false re-enqueues.
    options.HeartbeatTimeout = TimeSpan.FromMinutes(5); // Default: 5min

    // ── Shutdown ─────────────────────────────────────────────────────────────
    // How long running jobs get to finish during graceful shutdown.
    // Keep HostOptions.ShutdownTimeout above this value.
    options.ShutdownTimeout = TimeSpan.FromSeconds(30); // Default: 30s

    // ── Execution timeout ────────────────────────────────────────────────────
    // Longest a job may run when its type has no [ExecutionTimeout]. null = unbounded.
    // Cancellation is cooperative: a job that ignores its CancellationToken keeps its worker slot.
    options.DefaultExecutionTimeout = TimeSpan.FromMinutes(30); // Default: null (code only, not read from appsettings)

    // ── Retry ────────────────────────────────────────────────────────────────
    // Exception types no job should retry; the job goes straight to Failed. Combined with
    // [Retry(IgnoreRetryAttemptExceptions = ...)] per job type. Code only, not read from appsettings.
    options.IgnoreRetryAttemptExceptions = [typeof(ArgumentException), typeof(JsonException)]; // Default: empty

    // ── Queues ───────────────────────────────────────────────────────────────
    // Ordered list of queues this host polls. Queues drain in this order.
    options.Queues = new[] { "default", "emails", "reports" }; // Default: ["default"]

    // Prefix of the implicit "default" queue (see Queues concept page). Default: null = lowercase entry assembly name.
    options.QueuePrefix = "billing"; // jobs without a queue go to "billing.default"

    // ── Health checks ────────────────────────────────────────────────────────
    // Storage probe timeout before reporting Unhealthy.
    options.HealthCheckTimeout = TimeSpan.FromSeconds(5); // Default: 5s

    // Dead-letter job count above which the check reports Degraded.
    options.HealthCheckFailedThreshold = 100; // Default: 100

    // ── Retention ────────────────────────────────────────────────────────────
    // How long terminal jobs are kept before automatic purge.
    // Set to TimeSpan.Zero to disable purging for a specific status.
    options.RetentionSucceeded = TimeSpan.FromDays(7);   // Default: 7 days
    options.RetentionFailed    = TimeSpan.FromDays(30);  // Default: 30 days
    options.RetentionExpired   = TimeSpan.FromDays(7);   // Default: 7 days
    options.RetentionDeadLetter = TimeSpan.FromDays(60); // Default: 60 days

    // How often the retention service runs.
    options.RetentionInterval = TimeSpan.FromHours(1); // Default: 1h

    // Rows deleted per batch during purge (prevents DB lock escalation).
    options.RetentionBatchSize = 1000; // Default: 1000

    // ── Throughput ───────────────────────────────────────────────────────────
    // Aggregate completed acknowledgments to cut DB write roundtrips by 90%+.
    options.EnableBatchAcknowledgment = true; // Default: false

    // ── Logging ──────────────────────────────────────────────────────────────
    // Maximum structured log lines captured per job execution.
    options.MaxJobLogLines = 200; // Default: 200

    // ── Distributed throttle (NexJob.Redis) ──────────────────────────────────
    // Maximum time a live job may hold a Redis distributed throttle slot.
    // Slots from crashed nodes are reclaimed after 3 × HeartbeatInterval
    // regardless. Must exceed your longest expected job execution time.
    options.DistributedThrottleTtl = TimeSpan.FromHours(1); // Default: 1h
});
```

## appsettings.json Support

Not every option can be set from `appsettings.json`. The table below lists everything that the configuration binder reads from the `NexJob` section. Options not listed here are silently ignored — a typo in the key has no effect.

!!! note
    Retention periods (`RetentionSucceeded`, `RetentionFailed`, `RetentionExpired`, `RetentionDeadLetter`), `RetentionInterval`, `RetentionBatchSize`, `EnableBatchAcknowledgment`, `ForeignJobRetryDelay`, `DistributedThrottleTtl`, `RetryDelayFactory`, `DefaultExecutionTimeout`, and `IgnoreRetryAttemptExceptions` are **code-only**. Set them in `AddNexJob(options => ...)`, or adjust retention at runtime from the dashboard Settings page.


| Option | Key in `appsettings.json` | Notes |
|---|---|---|
| `Workers` | `Workers` | Integer |
| `MaxAttempts` | `MaxAttempts` | Integer |
| `MaxJobLogLines` | `MaxJobLogLines` | Integer |
| `ServerId` | `ServerId` | String |
| `DefaultQueue` | `DefaultQueue` | String. **Ignored**: the default queue of the application is named by `QueuePrefix`; a value other than `default` only logs a startup warning |
| `PollingInterval` | `PollingInterval` | `TimeSpan` string, e.g. `"00:00:10"` |
| `HeartbeatInterval` | `HeartbeatInterval` | `TimeSpan` string |
| `ServerHeartbeatInterval` | `ServerHeartbeatInterval` | `TimeSpan` string |
| `HeartbeatTimeout` | `HeartbeatTimeout` | `TimeSpan` string |
| `ShutdownTimeout` | `ShutdownTimeoutSeconds` | **Number of seconds**, not a `TimeSpan` string |
| `HealthCheckTimeout` | `HealthCheckTimeout` | `TimeSpan` string |
| `HealthCheckFailedThreshold` | `HealthCheckFailedThreshold` | Integer |
| `Queues` | `Queues` | JSON array of strings |
| `QueuePrefix` | `QueuePrefix` | String |
| `QueueSettings` | `QueueSettings` | Array — see Queue Settings section |
| `RecurringJobs` | `RecurringJobs` | Array of recurring job descriptors |
| Dashboard `Path` | `Dashboard.Path` | String |
| Dashboard `Title` | `Dashboard.Title` | String |
| Dashboard `PollIntervalSeconds` | `Dashboard.PollIntervalSeconds` | Integer |
| Dashboard `Port` | `Dashboard.Port` | Integer |
| Dashboard `LocalhostOnly` | `Dashboard.LocalhostOnly` | Boolean |
| `ForeignJobRetryDelay`, all retention options, `RetentionInterval`, `RetentionBatchSize`, `EnableBatchAcknowledgment`, `RetryDelayFactory`, `DistributedThrottleTtl`, `DefaultExecutionTimeout`, `IgnoreRetryAttemptExceptions` | — | **Code only** |

!!! note
    Keys that do not appear in the table above are silently ignored by the binder. A typo such as `"Wokers"` instead of `"Workers"` produces no error and applies the default value instead. Always double-check key names when configuration changes have no visible effect.


### Full appsettings.json Example

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

### Registering with IConfiguration

```csharp
// Configuration only
builder.Services.AddNexJob(builder.Configuration);

// Configuration with code overrides — code wins
builder.Services.AddNexJob(builder.Configuration, options =>
{
    options.Workers = 30;
});
```

## Environment Variables

NexJob is designed for cloud-native deployments. You can supply any `appsettings.json`-compatible option through environment variables without modifying configuration files.

### Standard .NET Double-Underscore Mapping

Use the `NexJob__` prefix with `__` (double underscore) as the hierarchy separator. The standard .NET configuration system maps these directly to the same settings as `appsettings.json`, so only keys from the table above are honoured.

```bash
# Docker, docker-compose, or Kubernetes Deployment manifest
export NexJob__Workers=20
export NexJob__PollingInterval="00:00:10"
export NexJob__MaxAttempts=5
export NexJob__ShutdownTimeoutSeconds=45
export NexJob__Dashboard__Port=5005
```

Register via `IConfiguration` to pick them up automatically:

```csharp
builder.Services.AddNexJob(builder.Configuration);
```

### Direct Environment Variable Retrieval

For options that cannot come from `appsettings.json` (such as `RetryDelayFactory`) or when you prefer not to use configuration providers, read environment variables directly inside the options delegate:

```csharp
builder.Services.AddNexJob(options =>
{
    if (int.TryParse(Environment.GetEnvironmentVariable("NEXJOB_WORKERS"), out var workers))
    {
        options.Workers = workers;
    }
});
```

### Broker Integration Environment Variables

NexJob does not define any `KAFKA_*` or `RABBITMQ_*` convention. Read whatever variables your deployment sets and assign them to the trigger options:

```csharp
builder.Services.AddNexJob()
    .AddKafkaTrigger(options =>
    {
        options.BootstrapServers =
            Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS") ?? "localhost:9092";
        options.Topic  = Environment.GetEnvironmentVariable("KAFKA_TOPIC") ?? "orders";
        options.GroupId = "orders-consumer";
    })
    .AddRabbitMqTrigger(options =>
    {
        options.HostName  = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
        options.QueueName = "orders";
    });
```

## Queue-Specific Settings

Use `ConfigureQueue` to apply an execution time window or a circuit breaker to a named queue. Call it multiple times with the same name to edit the same settings object — the name match is case-insensitive.

### ExecutionWindowSettings

Restrict a queue to a specific time range. Jobs enqueued outside the window stay `Enqueued` and are fetched when the window opens again.

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
            EndTime   = new TimeOnly(18, 0),
            TimeZone  = "America/Sao_Paulo", // Default: "UTC"
        };
    });
});
```

| Parameter | Type | Description |
|---|---|---|
| `StartTime` | `TimeOnly` | Start of the processing window |
| `EndTime` | `TimeOnly` | End of the processing window |
| `TimeZone` | `string` | IANA time zone ID. Defaults to `"UTC"` |
| `DaysOfWeek` | `DayOfWeek[]?` | Days on which the window opens, read from the local time in `TimeZone`. `null` or empty (the default) means every day |

Windows can cross midnight: set `StartTime` later than `EndTime` (for example `22:00` to `06:00`). An overnight window belongs to the day it starts. See [Execution Windows](../guides/execution-windows.md#only-on-some-days).

## Runtime Settings

The following settings can be changed from the dashboard Settings page or the `IRuntimeSettingsStore` API at runtime, without restarting the host. Changes are persisted to storage and take effect on the next dispatcher, scheduler, or retention cycle.

| Setting | Description |
|---|---|
| `PausedQueues` | Pause or resume specific queues |
| `RecurringJobsPaused` | Pause all recurring job scheduling cluster-wide |
| `PollingInterval` | Change the poll frequency without restarting |
| `RetentionSucceeded` | Retention period for succeeded jobs |
| `RetentionFailed` | Retention period for failed jobs |
| `RetentionExpired` | Retention period for expired jobs |
| `RetentionDeadLetter` | Retention period for dead-letter jobs |
| `RetentionBatchSize` | Rows deleted per purge batch (set via `IRuntimeSettingsStore`; no dashboard card) |

!!! warning
    **Worker count is not a runtime setting.** The dispatcher sizes its worker pool once at startup from `NexJobOptions.Workers`. There is no dashboard control for it, and any `Workers` value previously saved to storage by an older version is ignored. To change the worker count, update the configuration and redeploy.


## Dashboard Options

### ASP.NET Core (`DashboardOptions`)

Mount the dashboard inside your existing ASP.NET Core pipeline using the `NexJob.Dashboard` package:

```csharp
// Install: dotnet add package NexJob.Dashboard
using NexJob.Dashboard;

app.UseNexJobDashboard("/dashboard", options =>
{
    options.Title          = "My Jobs";                         // Default: "NexJob"
    options.MetricsCacheTtl = TimeSpan.FromSeconds(3);          // Default: 3s
    options.Queues         = ["default", "emails"];             // Optional: scope to these queues
});
```

### Standalone Worker Service (`StandaloneDashboardOptions`)

Embed a small web server inside a Worker Service or console app using the `NexJob.Dashboard.Standalone` package:

```csharp
// Install: dotnet add package NexJob.Dashboard.Standalone
using NexJob.Dashboard.Standalone;

builder.Services.AddNexJobStandaloneDashboard(options =>
{
    options.Port             = 5005;               // Default: 5005
    options.Path             = "/dashboard";       // Default: "/dashboard"
    options.Title            = "NexJob Dashboard"; // Default: "NexJob"
    options.LocalhostOnly    = true;               // Default: true (loopback only)
    options.PollIntervalSeconds = 3;               // Default: 3
    options.DisableWorkers   = false;              // true = ops-only host, no jobs executed
    options.Queues           = null;               // Optional queue scope
});
```

!!! warning
    The standalone dashboard binds to **loopback only** by default (`LocalhostOnly = true`). In a container, this makes the dashboard unreachable through a published port. Set `LocalhostOnly = false` **and** register an `IDashboardAuthorizationHandler` — without a handler, NexJob logs a startup warning. The embedded server has no authentication middleware, so your handler must authenticate from `context.Request` directly.


Set `DisableWorkers = true` to run a dashboard-only process that serves the UI without executing any jobs. This sets `Workers = 0` for that host before any service starts.

`Workers = 0` is valid on its own (`options.Workers = 0` or `"Workers": 0`) and means that the host does not fetch or execute jobs. The host still starts and registers as a node with 0 workers and **no polled queues**, so it is not counted as listening to a queue (the dashboard still flags a queue that only such hosts could read). The recurring scheduler, the orphan watcher and the retention keep running: they maintain the whole database and run on any node. A negative value throws `ArgumentOutOfRangeException`.
