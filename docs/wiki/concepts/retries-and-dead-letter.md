---
title: "NexJob Retries, Dead-Letter Handling, and Retry Policy"
sidebarTitle: "Retries & Dead Letter"
description: "Configure global and per-job retry policies with exponential backoff in NexJob, and register dead-letter handlers for jobs that exhaust all attempts."
---

NexJob automatically retries failed jobs using a configurable delay strategy. When a job exhausts its retry budget, it is marked as `Failed` and its dead-letter handler runs — giving you a chance to alert, compensate, or forward the failure before it goes silent. You control both layers: a global policy that applies to every job, and per-job overrides for jobs that need different behavior.

## Global Retry Policy

Set the default maximum number of attempts for all jobs in `AddNexJob`:

```csharp
builder.Services.AddNexJob(options =>
{
    options.MaxAttempts = 5; // 1 initial run + 4 retries
});
```

The default is **10 attempts** if you do not configure `MaxAttempts`.

### Default Delay Between Attempts

Unless a job sets its own `InitialDelay` with `[Retry]`, NexJob computes the wait time using `options.RetryDelayFactory`. The built-in factory uses a polynomial curve with jitter: `attempt⁴ + 15` seconds plus a random 0–29 seconds multiplied by `(attempt + 1)`. This grows quickly — roughly 16 to 74 seconds after the first failure and 4.5 to 7 minutes after the fourth.

## Per-Job Retry Override

Apply the `[Retry]` attribute to a job class to override the global policy for that job only. When you set `InitialDelay`, NexJob uses exponential backoff with ±10% jitter instead of the global factory.

```csharp
[Retry(5, InitialDelay = "00:00:30", Multiplier = 2.0, MaxDelay = "01:00:00")]
public sealed class ProcessPaymentJob : IJob<PaymentInput>
{
    public async Task ExecuteAsync(PaymentInput input, CancellationToken ct)
    {
        // 5 total attempts: first run + 4 retries
        // Delays (with ±10% jitter): ~30s → ~60s → ~120s → ~240s, capped at 1 hour
    }
}
```

### Attribute Parameters

| Parameter | Default | Description |
|---|---|---|
| `attempts` | Required | Total executions including the first run. `1` and `0` both mean no retry — the job is dead-lettered immediately on failure. |
| `InitialDelay` | `null` | Delay before the first retry as a `TimeSpan` string, e.g. `"00:00:30"`. When omitted, the global `RetryDelayFactory` is used and `Multiplier`/`MaxDelay` are ignored. |
| `Multiplier` | `2.0` | Exponential backoff multiplier. Delay = `InitialDelay × Multiplier^(retry − 1)`. |
| `MaxDelay` | No cap | Upper bound on any single delay as a `TimeSpan` string, e.g. `"01:00:00"`. |

A random ±10% jitter is applied to every delay computed from `InitialDelay` to spread retry spikes across workers.

### Immediate Dead-Letter on Failure

Set `attempts` to `0` (or `1`) to skip all retries and send the job straight to dead-letter on its first failure. Use this for jobs where retrying cannot fix the problem — such as those that validate input data:

```csharp
[Retry(0)] // No retries — dead-letter immediately on any failure
public sealed class WebhookNotificationJob : IJob<WebhookInput>
{
    public async Task ExecuteAsync(WebhookInput input, CancellationToken ct)
    {
        // A failure here goes straight to the dead-letter handler
    }
}
```

## Custom Retry Delay Factory

Replace the built-in delay curve entirely by assigning a delegate to `options.RetryDelayFactory`. The delegate receives the number of attempts already made (1 after the first failure) and returns the `TimeSpan` to wait before the next attempt. This affects all jobs that do not set their own `InitialDelay`.

```csharp
builder.Services.AddNexJob(options =>
{
    // Exponential backoff with up to 1 second of random jitter: 2s, 4s, 8s, ...
    options.RetryDelayFactory = attempt =>
        TimeSpan.FromSeconds(Math.Pow(2, attempt))
        + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
});
```

## IDeadLetterHandler&lt;TJob&gt;

Register a dead-letter handler to react when a job permanently fails. The handler runs in an isolated DI scope — exceptions are logged and swallowed, so a failing handler never crashes the dispatcher or prevents other jobs from running.

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
        await _alerts.SendAsync(
            $"Payment job {failedJob.Id} failed after {failedJob.Attempts} attempts: {lastException.Message}",
            cancellationToken);

        // Optionally trigger compensation logic
        // var input = JsonSerializer.Deserialize<PaymentInput>(failedJob.InputJson);
        // await _refunds.InitiateAsync(input!.OrderId, cancellationToken);
    }
}

// Register in Program.cs
builder.Services.AddTransient<IDeadLetterHandler<ProcessPaymentJob>, PaymentDeadLetterHandler>();
```

### What the JobRecord Contains

The `JobRecord` passed to your handler gives you everything you need to diagnose the failure or trigger compensation:

<div class="grid cards" markdown>
  -   **Id**

    The failed job's unique identifier.

  -   **Attempts / MaxAttempts**

    How many times the job ran and what the configured limit was.

  -   **LastErrorMessage**

    The exception message from the final failure.

  -   **LastErrorStackTrace**

    Full stack trace from the final failure.

  -   **InputJson**

    Serialized input — deserialize with `System.Text.Json` to recover the original payload.

  -   **Queue / Tags / CreatedAt**

    Full execution context for routing, filtering, and auditing.

</div>

### Safety Guarantees

!!! note
    - Dead-letter handlers are **optional**. Jobs without a registered handler are simply marked `Failed` and remain visible in the dashboard for manual requeue.
      - Handlers run in an isolated DI scope. An exception thrown inside a handler is logged and swallowed — it never crashes the dispatcher.
      - The `IDeadLetterHandler<TJob>` generic parameter is the **job type**, not the input type.
      - Handlers work for both `IJob` and `IJob<TInput>` implementations.


## Choosing the Right Strategy

Use the table below to pick the right approach for common failure scenarios:

| Scenario | Recommended Approach |
|---|---|
| Transient network error (timeout, connection reset) | Retries with exponential backoff |
| External API rate limiting | Retries with longer `InitialDelay` and higher `MaxDelay` |
| Invalid input data | `[Retry(0)]` — dead-letter immediately |
| Business rule violation | `[Retry(0)]` — retry won't fix it |
| Database deadlock | Retries with a short `InitialDelay`, 2–3 attempts |
| Payment or legal operation failure | Dead-letter handler with alerting and compensation |

!!! warning
    Jobs in the `Failed` state are retained for 30 days by default, then automatically purged. Once a job is purged, its history and input are gone permanently. If you need longer retention for audit purposes, increase `options.RetentionFailed` — or set it to `TimeSpan.Zero` to keep failed jobs indefinitely.

