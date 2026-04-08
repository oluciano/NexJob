# Idempotency

Prevent duplicate job execution and safely re-enqueue after failures.

---

## Why Duplicates Happen

NexJob delivers jobs at-least-once. A job may execute more than once due to:

- **Retries** — a job throws after partially completing external work
- **Orphan recovery** — a worker crashes, the job is re-enqueued, and the external action already completed
- **Manual requeue** — re-enqueueing a job from the dashboard
- **Duplicate enqueue calls** — your code calls `EnqueueAsync` multiple times with the same intent

Idempotency ensures the external effect happens exactly once, even if the job executes multiple times.

---

## Idempotency Key

Provide a unique key when enqueuing. NexJob deduplicates based on this key.

```csharp
await scheduler.EnqueueAsync<ProcessPaymentJob, PaymentInput>(
    new PaymentInput(orderId),
    idempotencyKey: $"payment-{orderId}",
    cancellationToken: ct);
```

### Deduplication Rules

Jobs with the same `idempotencyKey` in **active states** (`Enqueued`, `Processing`, `Scheduled`, `AwaitingContinuation`) are **always** deduplicated — the second enqueue returns the existing job ID.

For **terminal states** (`Succeeded`, `Failed`, `Expired`), behavior is governed by `DuplicatePolicy`.

---

## DuplicatePolicy

Controls what happens when you try to enqueue a job with the same `idempotencyKey` as a job in a terminal state.

```csharp
await scheduler.EnqueueAsync<ProcessPaymentJob, PaymentInput>(
    new PaymentInput(orderId),
    idempotencyKey: $"payment-{orderId}",
    duplicatePolicy: DuplicatePolicy.AllowAfterFailed, // Default
    cancellationToken: ct);
```

### AllowAfterFailed (Default)

Re-enqueue is allowed if the existing job is `Failed`. Rejected if `Succeeded` or `Expired`.

| Existing State | Behavior |
|---|---|
| `Succeeded` | Rejected — throws `DuplicateJobException` |
| `Failed` | **Allowed** — creates new job (assumes failure needs retry) |
| `Expired` | Rejected — throws `DuplicateJobException` |

**When to use:** Payment processing, order fulfillment — cases where a failed job should be retryable but a succeeded job should not repeat.

### RejectIfFailed

Re-enqueue is rejected if the existing job is `Failed`. Allowed if `Succeeded` or `Expired`.

| Existing State | Behavior |
|---|---|
| `Succeeded` | Allowed — creates new job |
| `Failed` | **Rejected** — throws `DuplicateJobException` |
| `Expired` | Allowed — creates new job |

**When to use:** Rarely needed. Useful when a failed job must be investigated manually before re-running.

### RejectAlways

Re-enqueue is rejected if the existing job is in any terminal state.

| Existing State | Behavior |
|---|---|
| `Succeeded` | **Rejected** — throws `DuplicateJobException` |
| `Failed` | **Rejected** — throws `DuplicateJobException` |
| `Expired` | **Rejected** — throws `DuplicateJobException` |

**When to use:** One-time operations like sending a legal notice, where neither success nor failure should be retried automatically.

---

## DuplicateJobException

Thrown when enqueue is rejected by the duplicate policy.

```csharp
try
{
    await scheduler.EnqueueAsync<MyJob>(
        idempotencyKey: "unique-key",
        duplicatePolicy: DuplicatePolicy.RejectAlways,
        cancellationToken: ct);
}
catch (DuplicateJobException ex)
{
    // Existing job is still active or was completed with this key
    var existingJobId = ex.ExistingJobId;
    var policy = ex.Policy;
}
```

---

## Real-World Usage

### Payment Processing

```csharp
// Only one payment per order, but retry if the previous attempt failed
await scheduler.EnqueueAsync<ProcessPaymentJob, PaymentInput>(
    new PaymentInput(orderId, amount),
    idempotencyKey: $"payment-{orderId}",
    duplicatePolicy: DuplicatePolicy.AllowAfterFailed,
    cancellationToken: ct);
```

### Email Notifications

```csharp
// Never send the same email twice, even if the job failed
await scheduler.EnqueueAsync<SendWelcomeEmailJob, EmailInput>(
    new EmailInput(user.Email),
    idempotencyKey: $"welcome-{user.Id}",
    duplicatePolicy: DuplicatePolicy.RejectAlways,
    cancellationToken: ct);
```

### Webhook Delivery

```csharp
// Retry webhook if previous delivery failed
await scheduler.EnqueueAsync<DeliverWebhookJob, WebhookInput>(
    new WebhookInput(url, payload),
    idempotencyKey: $"webhook-{webhookEvent.Id}",
    duplicatePolicy: DuplicatePolicy.AllowAfterFailed,
    cancellationToken: ct);
```

---

## Making Jobs Idempotent

Idempotency keys prevent duplicate enqueue, but jobs must also be idempotent internally.

```csharp
public sealed class ChargeCardJob : IJob<PaymentInput>
{
    public async Task ExecuteAsync(PaymentInput input, CancellationToken ct)
    {
        // Check before acting — safe to call multiple times
        var exists = await _payments.FindByOrderIdAsync(input.OrderId, ct);
        if (exists is not null) return;

        await _payments.ChargeAsync(input.OrderId, input.Amount, ct);
    }
}
```

---

## Next Steps

- [Scheduling](03-Scheduling.md) — Enqueue with idempotency keys
- [Common Scenarios](15-Common-Scenarios.md) — Real-world idempotent patterns
- [Troubleshooting](16-Troubleshooting.md) — Debug duplicate execution
