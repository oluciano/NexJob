# Throttling

Limit concurrent execution of jobs by named resource. Prevent overload of external systems.

---

## Basic Usage

Apply the `[Throttle]` attribute to any job class.

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

---

## Multiple Throttle Resources

A single job can be limited by multiple resources. All semaphores must be acquired before execution.

```csharp
[Throttle("payment-gateway", maxConcurrent: 5)]
[Throttle("email-service", maxConcurrent: 10)]
public sealed class ProcessOrderJob : IJob<OrderInput>
{
    public async Task ExecuteAsync(OrderInput input, CancellationToken ct)
    {
        // Requires both a payment-gateway slot AND an email-service slot
    }
}
```

---

## How It Works

Throttling is implemented via `SemaphoreSlim` per named resource.

- Semaphores are created on-demand in `ThrottleRegistry`
- The dispatcher waits for all required semaphores before executing the job
- Semaphores are released after execution (success or failure)
- **Per-process:** Throttling is local to each worker instance. In a multi-node deployment, the effective limit is `maxConcurrent * numberOfNodes`

---

## When to Use Throttling

| Scenario | Throttle Resource |
|---|---|
| External API with rate limits | `"api-name"` |
| Database connection pool | `"database"` |
| File system I/O | `"file-writes"` |
| Memory-intensive operations | `"heavy-compute"` |
| Third-party webhook delivery | `"webhook-sender"` |

## Scope: per-process vs. cluster-wide

By default, `[Throttle]` is enforced **per worker process**.
In a 3-node deployment with `[Throttle("api", maxConcurrent: 5)]`,
the effective limit is 15 concurrent jobs across the cluster.

### Opt-in: cluster-wide throttling via Redis

Install `NexJob.Redis` and enable distributed throttling:

```csharp
services.AddNexJob(opt => opt.UseRedis("localhost:6379"))
        .UseDistributedThrottle();
```

With distributed throttling enabled, `[Throttle("api", maxConcurrent: 5)]`
enforces a **global limit of 5** across all nodes — regardless of how many
workers are running.

Configure the slot TTL (default: 1 hour — should exceed your longest job):

```csharp
services.AddNexJob(opt =>
{
    opt.UseRedis("localhost:6379");
    opt.DistributedThrottleTtl = TimeSpan.FromHours(4);
})
.UseDistributedThrottle();
```

**Note:** `UseDistributedThrottle()` requires `NexJob.Redis`. If Redis is
unavailable, the system degrades to per-process throttling automatically.

---

## Next Steps

- [Retry & Dead Letter](06-Retry-And-Dead-Letter.md) — Handle failures when throttled jobs timeout
- [Configuration Reference](11-Configuration-Reference.md) — `DistributedThrottleTtl` option
- [Best Practices](13-Best-Practices.md) — Production throttling guidelines
