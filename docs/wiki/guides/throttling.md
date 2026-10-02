---
title: "Throttle Concurrent Jobs with the [Throttle] Attribute"
sidebarTitle: "Throttling"
description: "Use NexJob's [Throttle] attribute to limit concurrent job execution per resource, and enable cluster-wide limits with Redis distributed throttling."
---

NexJob's `[Throttle]` attribute lets you cap how many instances of a job run at the same time against a named resource — protecting downstream APIs, connection pools, and memory-intensive operations from being overwhelmed. Apply it to any job class and NexJob enforces the limit automatically, without any changes to your job logic.

## Basic Usage

Decorate your job class with `[Throttle]`, passing a resource name and the maximum number of concurrent executions you want to allow.

```csharp
[Throttle("payment-gateway", maxConcurrent: 5)]
public sealed class ChargeCardJob : IJob<ChargeInput>
{
    public async Task ExecuteAsync(ChargeInput input, CancellationToken ct)
    {
        // At most 5 instances of this job run simultaneously
    }
}
```

The resource name is a logical key — any job that shares the same name competes for the same pool of slots. This lets unrelated job types share a single limit against the same downstream system.

## Multiple Throttle Attributes on One Job

You can stack multiple `[Throttle]` attributes on a single job. Every declared semaphore must be acquired before the job begins executing.

```csharp
[Throttle("payment-gateway", maxConcurrent: 5)]
[Throttle("email-service", maxConcurrent: 10)]
public sealed class ProcessOrderJob : IJob<OrderInput>
{
    public async Task ExecuteAsync(OrderInput input, CancellationToken ct)
    {
        // Requires one payment-gateway slot AND one email-service slot
    }
}
```

!!! warning
    Slots are acquired **one at a time, in declaration order**. If `ProcessOrderJob` holds a `payment-gateway` slot and waits for an `email-service` slot, while another job type holds an `email-service` slot and waits for `payment-gateway`, the two jobs deadlock. Prevent this by declaring the same resources in the same order on every job type.


## Per-Process vs. Cluster-Wide Scope

By default, `[Throttle]` is enforced **per worker process** using an in-memory semaphore. In a multi-node deployment the effective cluster-wide limit is `maxConcurrent × numberOfNodes`.

| Deployment | `maxConcurrent` | Effective limit |
|---|---|---|
| 1 node | 5 | 5 |
| 3 nodes | 5 | 15 |
| 10 nodes | 5 | 50 |

If you need a true global cap regardless of how many nodes are running, enable distributed throttling.

### Distributed Throttling with Redis

Install the `NexJob.Redis` package and call `AddNexJobDistributedThrottle()` when registering NexJob services.

```csharp
using NexJob.Redis;

services.AddNexJobRedis("localhost:6379");    // registers Redis storage and IDatabase
services.AddNexJobDistributedThrottle();      // enforces [Throttle] limits cluster-wide
services.AddNexJob();
```

With distributed throttling enabled, `[Throttle("api", maxConcurrent: 5)]` allows exactly **5 concurrent executions across the entire cluster**, not 5 per node.

Each running job holds a slot and its node continuously refreshes that slot while the job runs. If a node crashes, its slots are reclaimed automatically after three `HeartbeatInterval` periods — 90 seconds with the default settings — rather than being lost for an hour.

#### Configuring Slot TTL

`DistributedThrottleTtl` is the maximum time a single job may hold a slot. Set it higher than your longest-running job to avoid premature slot release.

```csharp
services.AddNexJobRedis("localhost:6379");
services.AddNexJobDistributedThrottle();
services.AddNexJob(opt => opt.DistributedThrottleTtl = TimeSpan.FromHours(4));
```

!!! note
    `AddNexJobDistributedThrottle()` requires an `IDatabase` instance in the DI container. `AddNexJobRedis()` registers one automatically. If you use a different storage provider, register the Redis `IDatabase` yourself. If Redis becomes unavailable at runtime, NexJob degrades gracefully to per-process throttling.


## How Waiting Works

A throttled job that cannot acquire a slot does **not** return to the queue. It stays in `Processing` state, keeps its worker slot, and keeps sending heartbeats while it waits. NexJob retries the slot acquisition approximately every 500 ms until a slot frees up or the job is cancelled — for example during a graceful shutdown.

!!! warning
    **Worker starvation risk.** Because waiting jobs occupy worker slots, a burst of throttled jobs can fill every slot on a node and starve other queues and resources. Mitigate this by one of the following approaches:

    - Keep `Workers` comfortably above the sum of all `maxConcurrent` values on resources your jobs use.
    - Isolate throttled jobs in a dedicated queue and deploy a separate worker pool with a matching `Workers` count for just that queue.


## When to Use Throttling

| Scenario | Suggested resource name |
|---|---|
| External API with rate limits | `"api-name"` |
| Database connection pool | `"database"` |
| File system I/O | `"file-writes"` |
| Memory-intensive operations | `"heavy-compute"` |
| Third-party webhook delivery | `"webhook-sender"` |

## Queue Circuit Breaker

`[Throttle]` governs steady-state concurrency. The **queue circuit breaker** handles severe downstream outages — it pauses an entire queue when consecutive failures reach a threshold, lets a canary job probe recovery, and gradually ramps concurrency back up to prevent thundering herds.

Configure it per queue with `ConfigureQueue`:

```csharp
builder.Services.AddNexJob(options =>
{
    options.ConfigureQueue("payments", queue =>
    {
        queue.EnableCircuitBreaker(cb =>
        {
            cb.ConsecutiveFailuresThreshold = 5;
            cb.OpenDuration = TimeSpan.FromSeconds(30);   // first cooldown
            cb.BackoffMultiplier = 2.0;                   // doubles on each repeated failure
            cb.MaxOpenDuration = TimeSpan.FromMinutes(10);
            cb.RecoveryDuration = TimeSpan.FromMinutes(2);
            cb.RecoveryConcurrency = 2;                   // anti-thundering-herd ramp-up

            // Trip on 5xx, timeouts, 429, 401, and network drops
            cb.BreakOnTransientHttpErrors(includeAuthErrors: true);
            cb.BreakOn<TimeoutException>();
        });
    });
});
```

### Circuit States

<div class="grid cards" markdown>
  -   **Closed**

    Normal processing. All jobs execute at full worker concurrency.

  -   **Open**

    Consecutive failures exceeded the threshold. The queue is paused and jobs accumulate in storage without burning retries. Exponential backoff multiplies the cooldown on repeated probe failures.

  -   **Half-Open**

    Cooldown elapsed. One canary job is dispatched to probe downstream health.

  -   **Recovering**

    Canary succeeded. Concurrency is capped at `RecoveryConcurrency` for `RecoveryDuration` to let the downstream service stabilise before full throughput resumes.

</div>

### Programmatic Control

You can reset the circuit breaker from code — useful when you receive a recovery webhook from the downstream provider:

```csharp
await jobControlService.ResetQueueCircuitAsync("payments");
```

The dashboard at `/queues` also shows circuit state visually and exposes a **Reset Circuit** button for operators.

## Execution Window Settings

For scenarios where you need to restrict a queue to specific time windows — for example, batch imports that should only run during off-peak hours — configure `ExecutionWindow` on the queue. Workers skip that queue outside the window; jobs accumulate safely in storage and execute as soon as the window opens.

```csharp
builder.Services.AddNexJob(options =>
{
    options.ConfigureQueue("batch-imports", queue =>
    {
        queue.ExecutionWindow = new ExecutionWindowSettings
        {
            StartTime = new TimeOnly(22, 0),   // 10 PM
            EndTime   = new TimeOnly(6, 0),    // 6 AM (crosses midnight)
            TimeZone  = "America/New_York",    // IANA or Windows timezone ID
        };
    });
});
```

`ExecutionWindowSettings` handles windows that cross midnight automatically — set `StartTime` after `EndTime` to define an overnight window (e.g. 22:00 – 06:00).

!!! note
    `ExecutionWindow` only controls when a queue's workers fetch jobs. Throttling with `[Throttle]` and circuit breakers apply independently within those windows.


## Common Throttle Patterns

???+ "Stripe / payment gateway"
    ```csharp
    [Throttle("stripe", maxConcurrent: 10)]
    public sealed class ChargeCardJob : IJob<ChargeInput>
    {
        public async Task ExecuteAsync(ChargeInput input, CancellationToken ct)
        {
            await _stripe.ChargeAsync(input.CustomerId, input.Amount, ct);
        }
    }
    ```
    Cap concurrent charges to stay within Stripe's API concurrency limits without rate-limiting errors.


???+ "Memory-intensive report generation"
    ```csharp
    [Throttle("heavy-compute", maxConcurrent: 2)]
    public sealed class GenerateReportJob : IJob<ReportInput>
    {
        public async Task ExecuteAsync(ReportInput input, CancellationToken ct)
        {
            // Each report uses ~500 MB RAM; cap at 2 to avoid OOM on a 1 GB pod
            await _reports.BuildAsync(input.ReportId, ct);
        }
    }
    ```
    Deploy this job to a dedicated queue with `Workers = 2` to match the throttle limit and avoid any waiting overhead.


???+ "Shared database connection pool"
    ```csharp
    [Throttle("legacy-db", maxConcurrent: 5)]
    public sealed class LegacyDataSyncJob : IJob<SyncInput>
    {
        public async Task ExecuteAsync(SyncInput input, CancellationToken ct)
        {
            await _legacyDb.SyncRecordsAsync(input.TableName, ct);
        }
    }
    ```
    Protect a legacy database whose connection pool is limited to a small number of connections.

