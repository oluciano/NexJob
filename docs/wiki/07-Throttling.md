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

---

## Not Cluster-Wide

**Important:** `[Throttle]` is enforced per process, not cluster-wide. If you have 3 worker instances with `[Throttle("api", maxConcurrent: 5)]`, the effective concurrent limit is 15.

For true distributed throttling, use a storage-based solution (e.g., Redis semaphore) inside your job:

```csharp
public sealed class DistributedThrottledJob : IJob
{
    private readonly IConnectionMultiplexer _redis;

    public async Task ExecuteAsync(CancellationToken ct)
    {
        using var semaphore = await _redis.GetDistributedSemaphoreAsync("my-resource", 5, ct);
        await semaphore.WaitAsync(ct);
        try
        {
            // Execute throttled work
        }
        finally
        {
            semaphore.Release();
        }
    }
}
```

---

## Next Steps

- [Retry & Dead Letter](06-Retry-And-Dead-Letter.md) — Handle failures when throttled jobs timeout
- [Configuration Reference](11-Configuration-Reference.md) — Worker concurrency settings
- [Best Practices](13-Best-Practices.md) — Production throttling guidelines
