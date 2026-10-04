---
title: "Job Continuations: Chain Jobs with Parent-Child Links"
sidebarTitle: "Continuations"
description: "Use NexJob continuations to chain background jobs so child jobs run automatically when their parent succeeds, enabling multi-step workflows."
---

Continuations let you express sequential dependencies between jobs without writing orchestration logic inside a single large job. You enqueue a parent job, then register one or more child jobs that should run after the parent completes successfully. NexJob handles the transition automatically — when the parent reaches `Succeeded`, every waiting child moves to `Enqueued` and is picked up by the dispatcher.

## Basic Usage

Enqueue the parent job, capture the returned `JobId`, and pass it to `ContinueWithAsync`:

```csharp
// 1. Enqueue the parent
var parentJobId = await scheduler.EnqueueAsync<ProcessPaymentJob, PaymentInput>(
    new PaymentInput(orderId, amount),
    cancellationToken: ct);

// 2. Register the continuation — child waits for parent to succeed
await scheduler.ContinueWithAsync<SendConfirmationJob, ConfirmationInput>(
    parentJobId: parentJobId,
    input: new ConfirmationInput(orderId),
    cancellationToken: ct);
```

The child job is stored with status `AwaitingContinuation`. As soon as the parent transitions to `Succeeded`, NexJob automatically changes the child's status to `Enqueued`.

## Multiple Continuations from One Parent

One parent can have any number of children. All of them are released in parallel when the parent succeeds:

```csharp
var parentJobId = await scheduler.EnqueueAsync<ImportDataJob, ImportInput>(
    new ImportInput(fileId),
    cancellationToken: ct);

// All three run in parallel after the import succeeds
await scheduler.ContinueWithAsync<NotifyUsersJob>(parentJobId, cancellationToken: ct);
await scheduler.ContinueWithAsync<UpdateMetricsJob>(parentJobId, cancellationToken: ct);
await scheduler.ContinueWithAsync<CleanupTempFilesJob>(parentJobId, cancellationToken: ct);
```

## Sequential Chains

Build a strict pipeline by using the `JobId` returned by each `ContinueWithAsync` call as the parent for the next step:

```csharp
var step1 = await scheduler.EnqueueAsync<FetchDataJob>(cancellationToken: ct);

var step2 = await scheduler.ContinueWithAsync<ProcessDataJob, DataInput>(
    parentJobId: step1,
    input: new DataInput(sourceId),
    cancellationToken: ct);

var step3 = await scheduler.ContinueWithAsync<PublishResultJob>(
    parentJobId: step2,
    cancellationToken: ct);
```

Execution order: `FetchDataJob` → `ProcessDataJob` → `PublishResultJob`. Each step waits for the previous one to succeed before running.

## What Happens When a Parent Fails

If the parent job exhausts all its retries and transitions to `Failed`, the child jobs remain in `AwaitingContinuation` indefinitely. They will **never execute automatically**.

!!! warning
    A child job stuck in `AwaitingContinuation` does not expire, retry, or dead-letter on its own. If you need to handle parent failure, either configure sufficient retries on the parent or register a dead-letter handler that manually enqueues the child or triggers compensation.


To handle this safely:

### 1. Give the parent sufficient retries

Use the `[Retry]` attribute or the global `MaxAttempts` setting to give the parent job enough attempts to overcome transient failures. See [Retries & Dead Letter](../concepts/retries-and-dead-letter.md).

### 2. Register a dead-letter handler on the parent

If the parent ultimately fails, its dead-letter handler can manually enqueue the child or trigger an alternative workflow.

```csharp
public sealed class PaymentDeadLetterHandler : IDeadLetterHandler<ProcessPaymentJob>
{
    private readonly IScheduler _scheduler;

    public PaymentDeadLetterHandler(IScheduler scheduler) => _scheduler = scheduler;

    public async Task HandleAsync(
        JobRecord failedJob,
        Exception lastException,
        CancellationToken ct)
    {
        // Enqueue a compensation job instead of the original continuation
        await _scheduler.EnqueueAsync<RefundOrderJob, RefundInput>(
            new RefundInput(failedJob.Id),
            cancellationToken: ct);
    }
}
```



## Trace Correlation

NexJob captures the W3C `traceparent` that is active **at the time you call `ContinueWithAsync`** — that is, the ambient `Activity` in your calling code, not the parent job's execution span. When you enqueue the parent and its continuations in the same request handler, all jobs in the chain share the same trace ID, making the complete multi-step workflow visible as a single trace in your APM tool.

## When to Use Continuations

<div class="grid cards" markdown>
  -   **Use continuations when**

    - Job B must wait for Job A to complete successfully
    - You need a guaranteed, traceable execution order
    - You want fan-out: one parent triggering multiple parallel children

  -   **Don't use continuations when**

    - Jobs are independent — enqueue them separately
    - Job B should run even if Job A fails — enqueue both independently
    - You need conditional branching — use a single job with internal `if` logic

</div>

## Common Use Cases

- **Order fulfillment pipeline:** `ProcessPaymentJob` → `ReserveInventoryJob` → `SendConfirmationEmailJob`
- **Data import workflow:** `ImportDataJob` → (`ValidateDataJob`, `UpdateMetricsJob`, `NotifyUsersJob`) in parallel
- **Report generation:** `FetchDataJob` → `AggregateDataJob` → `RenderReportJob` → `EmailReportJob`
- **User onboarding sequence:** `CreateAccountJob` → `SendWelcomeEmailJob` → `ProvisionDefaultResourcesJob`
