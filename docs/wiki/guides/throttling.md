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
| --- | --- | --- |
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

A throttled job that cannot acquire a slot first waits briefly. During that wait it stays in `Processing`, keeps its worker slot and keeps sending heartbeats, and NexJob retries the slot acquisition about every 500 ms. This keeps short contention cheap: no storage writes and no change in ordering.

If no slot frees up within about **5 seconds**, the job is **returned to the queue** and its worker slot is released. It is scheduled again roughly a second later (with a little jitter) and tries again. Being returned this way does **not** count as an attempt: it never consumes `MaxAttempts`, never triggers a retry policy and never sends the job to dead-letter. It is also not a failure, so no failure metric is recorded.

You can see it happen in two places:

- An information log: `Job ... got no slot for throttled resource '...' ... Returning it to the queue`.
- The `nexjob.jobs.throttle_deferred` counter (tags `nexjob.job_type` and `nexjob.resource`).

!!! note
    Because a saturated resource can no longer keep every worker busy for long, other queues and resources keep running. Jobs waiting on a saturated resource may still be delayed, and they can run in a different order than they were enqueued. If strict ordering matters for a resource, keep that work in a dedicated queue.


!!! tip
    Isolating heavily throttled jobs in their own queue, with a worker pool sized for it, is still the best way to keep a slow downstream system from competing with the rest of your workload. The 5 second limit is not configurable.


## When to Use Throttling

| Scenario | Suggested resource name |
| --- | --- |
| External API with rate limits | `"api-name"` |
| Database connection pool | `"database"` |
| File system I/O | `"file-writes"` |
| Memory-intensive operations | `"heavy-compute"` |
| Third-party webhook delivery | `"webhook-sender"` |

## Related: Circuit Breaker and Execution Windows

Two queue-level controls used to live on this page and now have their own pages:

- [Circuit Breaker](../guides/circuit-breaker.md): pauses a queue automatically during a downstream outage.
- [Execution Windows](../guides/execution-windows.md): restricts a queue to a time window, such as nights only.

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

