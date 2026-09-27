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

### Multi-Service Architecture & Dedicated Ops Host

In multi-service ecosystems sharing a database cluster:
1. **Dedicated Ops Host:** Run a dedicated dashboard container with `DisableWorkers = true` so operational monitoring does not consume worker threads or take lock slots from backend workers.
2. **Dashboard Queue Scoping:** Scope the UI via `options.Queues = ["serviceA-queue"]` so engineering teams only view jobs, metrics, and queues relevant to their bounded context.
3. **Foreign Job Safe Deferral:** While queue separation (`options.Queues`) is the recommended best practice, if workers encounter foreign job types, NexJob automatically rolls back attempt counts and defers the job via `options.ForeignJobRetryDelay` rather than failing or dead-lettering it.

### Anti-Bloat Retention Strategies (High-Throughput Workloads)

In high-throughput environments (e.g., event streaming via Kafka, SQS, or RabbitMQ processing millions of jobs daily), table growth and storage bloat can become problematic even with scheduled chunked purging.

Use the `[Retention]` attribute on job classes to enforce immediate pruning or payload stripping upon successful completion:

```csharp
// 1. Ephemeral Jobs: Delete record immediately upon success
[Retention(PurgeOnSuccess = true)]
public sealed class HighFrequencyTelemetryJob : IJob<TelemetryPayload>
{
    public async Task ExecuteAsync(TelemetryPayload payload, CancellationToken ct)
    {
        // Process telemetry...
        // On success, the job row is immediately deleted from storage.
        // Catalog lifetime statistics (/catalog) are preserved!
    }
}

// 2. Payload Stripping: Keep metadata & logs for auditing, strip heavy JSON payloads
[Retention(TrimPayloadOnSuccess = true)]
public sealed class HeavyReportGenerationJob : IJob<LargeReportInput>
{
    public async Task ExecuteAsync(LargeReportInput input, CancellationToken ct)
    {
        // Process heavy input...
        // On success, InputJson is stripped (''), saving massive storage space
        // while preserving duration, state, queue, and execution logs.
    }
}
```

| Strategy | Attribute Setting | Storage Impact | Auditability |
|---|---|---|---|
| **Default** | *(None)* | Job retained until `JobRetentionService` runs | Full job record and payload intact |
| **Immediate Purge** | `PurgeOnSuccess = true` | Zero row bloat on success; row is deleted atomically | Succeeded jobs vanish from `/jobs`; lifetime stats preserved in `/catalog` |
| **Payload Stripping** | `TrimPayloadOnSuccess = true` | Massive space savings (payload set to empty string) | Job record, state, duration, and logs preserved; payload stripped |

> [!NOTE]
> If a job with `PurgeOnSuccess = true` fails, it is **never** purged immediately: it follows normal retry policies and dead-letter retention so operators can diagnose and requeue errors.

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
