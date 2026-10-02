---
title: "Idempotency and Duplicate Job Prevention in NexJob"
sidebarTitle: "Idempotency"
description: "Use idempotency keys and DuplicatePolicy in NexJob to prevent duplicate job execution and control re-enqueue behavior in at-least-once delivery systems."
---

NexJob delivers jobs at-least-once. Under normal conditions a job runs exactly once, but several real-world events can cause it to execute more than once: a worker crash followed by orphan recovery, a retry after a partially-completed external call, a manual requeue from the dashboard, or simply calling `EnqueueAsync` twice with the same intent. Idempotency keys let NexJob deduplicate at the enqueue level, and `DuplicatePolicy` gives you precise control over what happens after a job has already reached a terminal state.

## Why Idempotency Matters

Consider an order payment job that charges a card. If the job throws after the charge succeeds but before it marks the order as paid, NexJob retries it. Without an idempotency mechanism, the card is charged twice. The same problem arises if your application calls `EnqueueAsync` in a retry loop or if a load balancer delivers the same HTTP request twice.

Idempotency keys address the **enqueue** side — preventing two separate `JobRecord` entries for the same logical operation while one is still active. For the **execution** side, your job must also be internally idempotent (check before acting, use the payment provider's own idempotency key, etc.).

## Providing an Idempotency Key

Pass `idempotencyKey` to any `EnqueueAsync` call. NexJob returns the existing `JobId` if a matching active job is found, and creates a new one otherwise.

```csharp
await scheduler.EnqueueAsync<ProcessPaymentJob, PaymentInput>(
    new PaymentInput(orderId, amount),
    idempotencyKey: $"payment-{orderId}",
    cancellationToken: ct);
```

### Deduplication Rules for Active Jobs

Jobs with the same `idempotencyKey` in any **active state** are always deduplicated — no matter which `DuplicatePolicy` you choose:

| Active State | Behavior |
|---|---|
| `Enqueued` | Returns the existing `JobId` — no new job created |
| `Processing` | Returns the existing `JobId` — no new job created |
| `Scheduled` | Returns the existing `JobId` — no new job created |
| `AwaitingContinuation` | Returns the existing `JobId` — no new job created |

For jobs in **terminal states** (`Succeeded`, `Failed`, `Expired`), the behavior is controlled by `DuplicatePolicy`.

## DuplicatePolicy

`DuplicatePolicy` controls what happens when you enqueue a job using an `idempotencyKey` that matches a job already in a terminal state.

```csharp
await scheduler.EnqueueAsync<ProcessPaymentJob, PaymentInput>(
    new PaymentInput(orderId, amount),
    idempotencyKey: $"payment-{orderId}",
    duplicatePolicy: DuplicatePolicy.AllowAfterFailed,
    cancellationToken: ct);
```

### AllowAfterFailed (Default)

Re-enqueue is allowed once an existing job with the same key has reached any terminal state. Only active jobs are deduplicated.

| Existing Terminal State | Behavior |
|---|---|
| `Succeeded` | ✅ Allowed — creates a new job |
| `Failed` | ✅ Allowed — creates a new job |
| `Expired` | ✅ Allowed — creates a new job |

!!! warning
    `AllowAfterFailed` does not prevent duplicate side effects. If the previous job already completed the external work before succeeding, a new job will repeat it. Ensure your job is idempotent internally, or choose `RejectAlways` when the work must happen only once.


**When to use:** Recurring work, webhook delivery, or any operation that is safe to repeat after its previous attempt has finished.

### RejectIfFailed

Re-enqueue is rejected if the existing job reached the `Failed` terminal state. Re-enqueue is allowed after `Succeeded` or `Expired`.

| Existing Terminal State | Behavior |
|---|---|
| `Succeeded` | ✅ Allowed — creates a new job |
| `Failed` | ❌ Rejected — throws `DuplicateJobException` |
| `Expired` | ✅ Allowed — creates a new job |

**When to use:** Jobs that must be manually reviewed and cleared after failure before a retry is permitted — for example, a financial reconciliation that needs human sign-off.

### RejectAlways

Re-enqueue is rejected if the existing job is in any terminal state. Use this for one-time operations where re-execution is never acceptable.

| Existing Terminal State | Behavior |
|---|---|
| `Succeeded` | ❌ Rejected — throws `DuplicateJobException` |
| `Failed` | ❌ Rejected — throws `DuplicateJobException` |
| `Expired` | ❌ Rejected — throws `DuplicateJobException` |

**When to use:** Legal notices, one-time account provisioning, or any operation where running it more than once would cause unacceptable consequences regardless of the previous outcome.

## Handling DuplicateJobException

`DuplicateJobException` is thrown (from `NexJob.Exceptions`) when an enqueue is rejected by policy. It exposes the existing job's ID and the policy that rejected the request:

```csharp
using NexJob.Exceptions;

try
{
    await scheduler.EnqueueAsync<SendLegalNoticeJob, NoticeInput>(
        new NoticeInput(userId),
        idempotencyKey: $"legal-notice-{userId}",
        duplicatePolicy: DuplicatePolicy.RejectAlways,
        cancellationToken: ct);
}
catch (DuplicateJobException ex)
{
    // The notice was already sent — surface this to the caller
    logger.LogWarning(
        "Legal notice for user {UserId} already exists as job {JobId} (policy: {Policy})",
        userId, ex.ExistingJobId, ex.Policy);
}
```

## Real-World Examples


  === "Payment Processing"
    ```csharp
        // Deduplicated while a payment job for this order is active.
        // A new attempt is allowed once it finishes.
        // The job must also pass the order ID to the payment provider
        // as its own idempotency key to prevent double charges.
        await scheduler.EnqueueAsync<ProcessPaymentJob, PaymentInput>(
            new PaymentInput(orderId, amount),
            idempotencyKey: $"payment-{orderId}",
            duplicatePolicy: DuplicatePolicy.AllowAfterFailed,
            cancellationToken: ct);
        ```

  === "Welcome Email"
    ```csharp
        // Never send the same welcome email twice,
        // even if the job previously failed.
        await scheduler.EnqueueAsync<SendWelcomeEmailJob, EmailInput>(
            new EmailInput(user.Email),
            idempotencyKey: $"welcome-{user.Id}",
            duplicatePolicy: DuplicatePolicy.RejectAlways,
            cancellationToken: ct);
        ```

  === "Webhook Delivery"
    ```csharp
        // Allow retry if the previous delivery attempt failed,
        // but deduplicate while a delivery is in progress.
        await scheduler.EnqueueAsync<DeliverWebhookJob, WebhookInput>(
            new WebhookInput(url, payload),
            idempotencyKey: $"webhook-{webhookEvent.Id}",
            duplicatePolicy: DuplicatePolicy.AllowAfterFailed,
            cancellationToken: ct);
        ```



## Making Jobs Idempotent Internally

Idempotency keys guard against duplicate enqueue, but the job itself must also handle being called more than once. The standard pattern is to check whether the external effect already happened before acting:

```csharp
public sealed class ChargeCardJob : IJob<PaymentInput>
{
    private readonly IPaymentService _payments;

    public ChargeCardJob(IPaymentService payments) => _payments = payments;

    public async Task ExecuteAsync(PaymentInput input, CancellationToken ct)
    {
        // Guard: exit early if the charge already exists
        var existing = await _payments.FindByOrderIdAsync(input.OrderId, ct);
        if (existing is not null)
            return;

        await _payments.ChargeAsync(input.OrderId, input.Amount, ct);
    }
}
```

## Best Practices for Choosing a Key

!!! note
    - **Use a natural business identifier** as your key: `$"payment-{orderId}"`, `$"welcome-{userId}"`, `$"invoice-{invoiceId}"`. This makes deduplication semantics obvious to future readers.
      - **Keep keys stable** — if the key changes between retries (e.g. it includes a timestamp), deduplication will not work.
      - **Scope keys to the operation** — a key that is too broad (e.g. just the user ID) may block legitimate duplicate jobs for different operations on the same entity.
      - **Remember key lifetime** — the guarantee holds only while the job record exists in storage. Once the record is purged by the retention policy, the same key can be reused.

