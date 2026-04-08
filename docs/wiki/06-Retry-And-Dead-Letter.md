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

---

## Per-Job Retry Override

Use the `[Retry]` attribute to override the global policy for specific jobs.

```csharp
[Retry(5, InitialDelay = "00:00:30", Multiplier = 2.0, MaxDelay = "01:00:00")]
public sealed class ProcessPaymentJob : IJob<PaymentInput>
{
    public async Task ExecuteAsync(PaymentInput input, CancellationToken ct)
    {
        // Will retry up to 5 times with exponential backoff:
        // 30s → 60s → 120s → 240s → 480s (capped at 1h)
    }
}
```

| Parameter | Default | Description |
|---|---|---|
| `attempts` | Required | Maximum number of attempts (including the first) |
| `InitialDelay` | 1 minute | Delay before the first retry |
| `Multiplier` | 2.0 | Exponential backoff multiplier |
| `MaxDelay` | No cap | Maximum delay between retries |

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

## Custom Retry Delay Factory

For full control over retry timing, implement `IRetryDelayFactory`.

```csharp
public sealed class JitterRetryFactory : IRetryDelayFactory
{
    public TimeSpan GetDelay(int attempt, JobRecord job)
    {
        // Exponential backoff with jitter
        var baseDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
        return baseDelay + jitter;
    }
}

builder.Services.AddNexJob(options =>
{
    options.RetryDelayFactory = new JitterRetryFactory();
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
        // var input = failedJob.GetInput<PaymentInput>();
        // await _refunds.InitiateAsync(input.OrderId, cancellationToken);
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
- `InputJson` — the serialized input (deserialize with `GetInput<T>()`)
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

---

## Next Steps

- [Throttling](07-Throttling.md) — Limit concurrent executions
- [Idempotency](17-Idempotency.md) — Handle retries safely with idempotent jobs
- [Common Scenarios](15-Common-Scenarios.md) — Real-world retry patterns
