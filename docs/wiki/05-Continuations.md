# Continuations

Chain jobs together: a child job waits for its parent to complete successfully before executing.

---

## Basic Usage

```csharp
// 1. Enqueue the parent job
var parentJobId = await scheduler.EnqueueAsync<ProcessPaymentJob, PaymentInput>(
    new PaymentInput(orderId, amount),
    cancellationToken: ct);

// 2. Create the continuation — child waits for parent
await scheduler.ContinueWithAsync<SendConfirmationJob, ConfirmationInput>(
    parentJobId: parentJobId,
    input: new ConfirmationInput(orderId),
    cancellationToken: ct);
```

The child job is stored as `AwaitingContinuation`. When the parent completes with `Succeeded`, the child transitions to `Enqueued` automatically.

---

## Multiple Continuations

One parent can have multiple children. They all execute in parallel after the parent succeeds.

```csharp
var parentJobId = await scheduler.EnqueueAsync<ImportDataJob, ImportInput>(input, cancellationToken: ct);

// These all run after the import succeeds
await scheduler.ContinueWithAsync<NotifyUsersJob>(parentJobId, cancellationToken: ct);
await scheduler.ContinueWithAsync<UpdateMetricsJob>(parentJobId, cancellationToken: ct);
await scheduler.ContinueWithAsync<CleanupTempFilesJob>(parentJobId, cancellationToken: ct);
```

---

## Chaining

Chain multiple jobs in sequence:

```csharp
var step1 = await scheduler.EnqueueAsync<FetchDataJob>(cancellationToken: ct);
var step2 = await scheduler.ContinueWithAsync<ProcessDataJob, DataInput>(step1, new DataInput(), cancellationToken: ct);
var step3 = await scheduler.ContinueWithAsync<PublishResultJob>(step2, cancellationToken: ct);
```

Execution order: `FetchDataJob` → `ProcessDataJob` → `PublishResultJob`

---

## What Happens on Parent Failure?

If the parent job fails (exhausts retries → `Failed`), the child job remains in `AwaitingContinuation` indefinitely. It will never execute.

To handle this, either:

1. **Ensure the parent has sufficient retries** — see [Retry & Dead Letter](06-Retry-And-Dead-Letter.md)
2. **Handle in dead-letter** — manually enqueue the child in the parent's dead-letter handler if needed

---

## Trace Context

The W3C `traceparent` context is propagated from parent to child. This means:

- OpenTelemetry traces show the full job chain as a single trace
- You can follow the complete execution path in your APM tool

See [OpenTelemetry](12-OpenTelemetry.md) for details.

---

## When to Use Continuations

**Use continuations when:**
- Job B depends on Job A completing successfully
- You need a guaranteed execution order
- You want trace correlation across the chain

**Don't use continuations when:**
- Jobs are independent — enqueue them separately
- You need Job B to run even if Job A fails — enqueue both independently
- You need conditional branching — use a single job with internal branching logic

---

## Next Steps

- [Job Types](02-Job-Types.md) — Define parent and child jobs
- [Retry & Dead Letter](06-Retry-And-Dead-Letter.md) — Ensure parent jobs don't fail
- [Dashboard](10-Dashboard.md) — Monitor job chains
