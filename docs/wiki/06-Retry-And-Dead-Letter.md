# Retry & Dead Letter

Handle transient failures automatically. When retries are exhausted, dead-letter handlers provide a fallback.

---

## Global Retry Policy

Configure default retries for all jobs.

```csharp
builder.Services.AddNexJob(options =>
{
    options.MaxAttempts = 5; // Initial attempt + 4 retries
});
```

Default is 10 attempts.

### Default delay between attempts

Unless a job sets its own delay with `[Retry(InitialDelay = ...)]`, the wait before the next attempt comes from `options.RetryDelayFactory`. Its default is `attempt^4 + 15` seconds plus a random 0-29 s multiplied by `(attempt + 1)`, where `attempt` is the number of attempts already made (1 after the first failure). That is a polynomial curve with jitter, not an exponential one, and it grows quickly: between 16 and 74 seconds after the first failure and between 4.5 and 7 minutes after the fourth. See [Custom Retry Delay](#custom-retry-delay) to change it.

---

## Per-Job Retry Override

Use the `[Retry]` attribute to override the global policy for specific jobs.

```csharp
[Retry(5, InitialDelay = "00:00:30", Multiplier = 2.0, MaxDelay = "01:00:00")]
public sealed class ProcessPaymentJob : IJob<PaymentInput>
{
    public async Task ExecuteAsync(PaymentInput input, CancellationToken ct)
    {
        // 5 attempts in total = the first run + 4 retries, with exponential backoff
        // (each delay gets ±10% jitter): 30s → 60s → 120s → 240s, never above 1h
    }
}
```

| Parameter | Default | Description |
|---|---|---|
| `attempts` | Required | Maximum number of attempts (including the first) |
| `InitialDelay` | None | Delay before the first retry, as a `TimeSpan` string (`"00:00:30"`). When it is not set, the global `options.RetryDelayFactory` computes the delay and `Multiplier`/`MaxDelay` are not used |
| `Multiplier` | 2.0 | Exponential backoff multiplier (delay = `InitialDelay` × `Multiplier`^(retry − 1)) |
| `MaxDelay` | No cap | Maximum delay between retries, as a `TimeSpan` string |

A random ±10% jitter is added to every delay computed from `InitialDelay`.

### Immediate Dead-Letter

```csharp
[Retry(0)] // No retries — go straight to dead-letter on failure
public sealed class WebhookNotificationJob : IJob<WebhookInput>
{
    public async Task ExecuteAsync(WebhookInput input, CancellationToken ct)
    {
        // If this fails, it's immediately sent to dead-letter
    }
}
```

---

## Custom Retry Delay

For full control over the timing of jobs that do not set `[Retry(InitialDelay = ...)]`, assign a delegate to `options.RetryDelayFactory`. It receives the number of attempts already made (1 after the first failure) and returns how long to wait before the next one.

```csharp
builder.Services.AddNexJob(options =>
{
    // Exponential backoff with jitter: 2s, 4s, 8s, ... plus up to 1s
    options.RetryDelayFactory = attempt =>
        TimeSpan.FromSeconds(Math.Pow(2, attempt))
        + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
});
```

---

## Dead-Letter Handlers

When all retries are exhausted, the job is marked as `Failed` and its dead-letter handler is invoked (if registered).

```csharp
public sealed class PaymentDeadLetterHandler : IDeadLetterHandler<ProcessPaymentJob>
{
    private readonly IAlertService _alerts;
    private readonly IRefundService _refunds;

    public PaymentDeadLetterHandler(IAlertService alerts, IRefundService refunds)
    {
        _alerts = alerts;
        _refunds = refunds;
    }

    public async Task HandleAsync(
        JobRecord failedJob,
        Exception lastException,
        CancellationToken cancellationToken)
    {
        // Alert the team
        await _alerts.SendAsync(
            $"Payment processing failed for job {failedJob.Id} after {failedJob.Attempts} attempts",
            cancellationToken);

        // Optionally trigger compensation
        // var input = JsonSerializer.Deserialize<PaymentInput>(failedJob.InputJson);
        // await _refunds.InitiateAsync(input!.OrderId, cancellationToken);
    }
}

builder.Services.AddTransient<IDeadLetterHandler<ProcessPaymentJob>, PaymentDeadLetterHandler>();
```

**Safety guarantees:**

- Dead-letter handlers run in an isolated DI scope
- Exceptions are logged and swallowed — they never crash the dispatcher
- Handlers are optional — jobs without handlers are simply marked as `Failed`

---

## Failure Data Available

The `JobRecord` passed to dead-letter handlers contains:

- `Id` — the failed job's ID
- `Attempts` — how many times it was attempted
- `MaxAttempts` — configured maximum
- `LastErrorMessage` — the error message from the last failure
- `LastErrorStackTrace` — full stack trace
- `InputJson` — the serialized input (deserialize it with `System.Text.Json`)
- `Queue`, `Tags`, `CreatedAt`, `CompletedAt` — full execution context

---

## When to Use Retries vs Dead-Letter

| Scenario | Approach |
|---|---|
| Transient network error (timeout, connection reset) | Retries with exponential backoff |
| External API rate limiting | Retries with longer delays |
| Data validation error (bad input) | Dead-letter immediately — `[Retry(0)]` |
| Business rule violation | Dead-letter — retry won't fix it |
| Database deadlock | Retries with short delays (2-3 attempts) |
| Foreign job type (worker lacks job assembly) | Safe deferral (resets attempt, defers without dead-lettering) |

---

## Multi-Service & Foreign Job Safe Deferral

When multiple microservices or processes share the same database cluster or storage queue, a worker may dequeue a job whose CLR `JobType` or `InputType` belongs to another service and cannot be loaded in the current runtime.

In earlier versions, this resulted in an unhandled type loading exception that consumed retry attempts and eventually poisoned the job into Dead-Letter.

### How Safe Deferral Works:
1. When `DefaultJobInvokerFactory` cannot resolve `JobType` or `InputType`, it raises a `ForeignJobTypeException`.
2. `JobExecutor` intercepts this exception and treats it as a non-fatal, foreign job event:
   - **Rolls back the attempt increment:** Restores `job.Attempts` so the owning service has its full attempt budget.
   - **Defers the job:** Reschedules the job at `DateTimeOffset.UtcNow + options.ForeignJobRetryDelay` (default 5s) so the worker does not spin in a tight polling loop, giving the owning node an opportunity to pick it up.
   - **Never Dead-Letters:** `IDeadLetterDispatcher` is never called for foreign jobs.

```csharp
builder.Services.AddNexJob(options =>
{
    // Deferral delay before foreign jobs become visible again for other workers
    options.ForeignJobRetryDelay = TimeSpan.FromSeconds(5); // Default: 5s
});
```

---

## Failed Job Retention & Chunked Purging

A job that exhausts its attempts is stored as `Failed` (its dead-letter handler runs at that moment). Failed jobs stay in storage for troubleshooting and manual re-queuing until `JobRetentionService` purges them.

Configure retention thresholds and batch sizing in `NexJobOptions`:

```csharp
builder.Services.AddNexJob(options =>
{
    // How long Failed (dead-lettered) jobs are kept — default 30 days
    options.RetentionFailed = TimeSpan.FromDays(30);

    // Run the retention purge loop every hour (default)
    options.RetentionInterval = TimeSpan.FromHours(1);

    // Delete in chunks of 1000 to avoid table locks and WAL / log bloat (default)
    options.RetentionBatchSize = 1000;
});
```

- `RetentionFailed` (default 30 days) decides how long `Failed` jobs are kept.
- `RetentionDeadLetter` (default 60 days) applies to `Failed` jobs **only when `RetentionFailed` is `TimeSpan.Zero`**; it also covers rows in the legacy `DeadLetter` state written by older versions.
- To keep failed jobs indefinitely, set both `RetentionFailed` and `RetentionDeadLetter` to `TimeSpan.Zero`.

Chunked purging runs across all persistent storage providers (PostgreSQL, SQL Server, Redis, MongoDB).

---

## Anti-Bloat Retention Strategies (`[Retention]`)

For ultra high-throughput workloads (for example streaming message triggers) where retaining millions of succeeded jobs until the retention interval causes table and index bloat:

1. **Immediate purge (`PurgeOnSuccess = true`):**
   ```csharp
   [Retention(PurgeOnSuccess = true)]
   public sealed class FastIngestionJob : IJob<DataChunk> { ... }
   ```
   Deletes the job as soon as it succeeds. Succeeded jobs will not bloat the `/jobs` tables, but lifetime execution statistics are preserved in `/catalog`.

2. **Payload stripping (`TrimPayloadOnSuccess = true`):**
   ```csharp
   [Retention(TrimPayloadOnSuccess = true)]
   public sealed class LargeDocumentJob : IJob<DocumentPayload> { ... }
   ```
   Clears `InputJson` when the job succeeds. Metadata (execution duration, completion timestamp, tags, logs) is kept for auditability while cutting the storage footprint.

*Note: if a job fails or is dead-lettered, the purge/trim is skipped, so the full diagnostics and input data stay available for retry and debugging.*

---

## Next Steps

- [Throttling](07-Throttling.md) — Limit concurrent executions
- [Idempotency](17-Idempotency.md) — Handle retries safely with idempotent jobs
- [Common Scenarios](15-Common-Scenarios.md) — Real-world retry patterns
