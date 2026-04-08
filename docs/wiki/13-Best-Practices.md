# Best Practices

Production guidelines for NexJob.

---

## Job Design

### Keep Jobs Small

Each job should do one thing. If your job fetches data, transforms it, calls three APIs, and sends two emails, split it into multiple jobs with [continuations](05-Continuations.md).

```csharp
// Bad: does everything
public sealed class ProcessOrderJob : IJob<OrderInput> { ... }

// Good: focused responsibility
public sealed class ValidateOrderJob : IJob<OrderInput> { ... }
public sealed class ChargePaymentJob : IJob<ChargeInput> { ... }
public sealed class SendConfirmationJob : IJob<ConfirmationInput> { ... }
```

### Make Jobs Idempotent

Jobs can be retried or re-executed due to failures, orphan recovery, or manual requeue. Design them to handle duplicate execution safely.

```csharp
public sealed class ChargeCardJob : IJob<ChargeInput>
{
    public async Task ExecuteAsync(ChargeInput input, CancellationToken ct)
    {
        // Check if already charged before charging
        var exists = await _payments.ExistsAsync(input.OrderId, ct);
        if (exists) return;

        await _payments.ChargeAsync(input.OrderId, input.Amount, ct);
    }
}
```

See [Idempotency](17-Idempotency.md) for strategies.

### Use Minimal Input

Only include what the job needs to execute. Don't pass entire entities — pass IDs and fetch inside the job.

```csharp
// Bad: passes entire order
public sealed record ProcessOrderInput(Order FullOrder);

// Good: passes only the ID
public sealed record ProcessOrderInput(Guid OrderId);
```

---

## Retry Configuration

### Match Retry Policy to Failure Mode

| Failure Type | Retries | Delay | Reason |
|---|---|---|---|
| Transient network error | 3-5 | Exponential, 30s-5min | Usually resolves quickly |
| External API rate limit | 5-10 | Exponential, 1min-1hr | May need to wait for window reset |
| Database deadlock | 2-3 | Short, 1-5s | Resolves on next transaction |
| Validation error | 0 | N/A | Retry won't fix bad input |

### Set Deadlines for Time-Sensitive Jobs

```csharp
// Promotional email — useless if delayed by 30 minutes
await scheduler.EnqueueAsync<SendPromoEmailJob>(
    deadlineAfter: TimeSpan.FromMinutes(10));
```

---

## Concurrency

### Size Workers for Your Workload

```csharp
options.Workers = 20;
```

- Low (5-10): Few concurrent jobs, low resource usage
- Medium (10-30): Typical workloads, good throughput
- High (30-100): Heavy throughput, ensure external services can handle it

### Use Throttling for External Services

```csharp
[Throttle("stripe-api", maxConcurrent: 5)]
public sealed class ChargeCardJob : IJob<ChargeInput> { ... }
```

### Use Queues for Workload Isolation

```csharp
options.Queues = new[] { "default", "emails", "heavy-compute" };
```

Deploy separate worker instances with different queue configurations:

- Worker A: `["default", "emails"]` with 20 workers
- Worker B: `["heavy-compute"]` with 5 workers

---

## Monitoring

### Enable OpenTelemetry

Collect traces and metrics from day one. See [OpenTelemetry](12-OpenTelemetry.md).

### Set Up Alerts

Alert on:

- `nexjob.jobs.failed` increases — jobs hitting dead-letter
- `nexjob.jobs.expired` increases — deadlines too tight or workers insufficient
- `nexjob.job.duration` p99 spikes — jobs getting slower

### Use the Dashboard

Check the dashboard regularly for:

- Queue depth trends
- Failed job error patterns
- Orphaned jobs (indicates worker crashes)

---

## Production Checklist

- [ ] Persistent storage (not InMemory)
- [ ] Adequate `MaxAttempts` for your failure modes
- [ ] Dead-letter handlers for critical jobs
- [ ] `[Throttle]` on jobs calling external services
- [ ] `deadlineAfter` for time-sensitive jobs
- [ ] OpenTelemetry configured and exporting
- [ ] Dashboard enabled with authorization
- [ ] Retention policies set (prevent storage growth)
- [ ] Idempotent jobs (see [Idempotency](17-Idempotency.md))
- [ ] Health check configured (`NexJobHealthCheck`)

---

## Next Steps

- [Writing Tests](14-Writing-Tests.md) — Test your jobs
- [Common Scenarios](15-Common-Scenarios.md) — Real-world patterns
- [Troubleshooting](16-Troubleshooting.md) — Debug production issues
