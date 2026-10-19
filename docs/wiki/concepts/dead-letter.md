---
title: "NexJob Dead-Letter Handling: Handlers, Crashed Jobs and Retention"
sidebarTitle: "Dead-letter handling"
description: "Register an IDeadLetterHandler in NexJob to react when a job exhausts its attempts, including jobs whose node died, and know how long failed jobs are kept."
---

When a job exhausts its retry budget it is marked `Failed` and its dead-letter handler runs, giving you a chance to alert, compensate, or forward the failure before it goes silent. Handlers are optional: a job without one is simply `Failed` and stays visible in the dashboard.

## `IDeadLetterHandler<TJob>`

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

### What the `JobRecord` contains

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

### When the node dies

A job can also run out of attempts without ever reporting an error: the process running it died (out of memory, `kill -9`, a lost machine) and each recovery used up an attempt. When the orphan watcher finds such a job with no attempts left, it marks it `Failed` and runs the same dead-letter handling: the typed `IDeadLetterHandler<TJob>` and every `IDeadLetterForwarder`.

When several services share one database, the watcher of every node scans every job, but a node fails a job without attempts left only if it can load the job type. The job stays `Processing` until a node of the service that owns it finds it, so its typed handler runs there (before v6.3.0 any node could fail it, and a node of another service skipped the handler). See [Multi-service](../guides/multi-service.md).

The exception you receive is an `OrphanedJobException`, because the job never threw one. It carries the job id and how many attempts were used, so a handler can tell a crash from an ordinary failure:

```csharp
public Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken ct)
{
    if (lastException is OrphanedJobException)
    {
        // The node died while running this job: likely a poison input (for example, out of memory).
    }

    return Task.CompletedTask;
}
```

- **Once per job.** The storage reports a job to the node whose scan moved it, so several nodes running the watcher never dead-letter the same job twice.
- **After the move.** The job is already `Failed` in storage when the handler runs. If the handler throws, or the node stops right then, the job stays `Failed` and the handler is not called again.
- **Custom storage providers.** Dead-letter handling for crashed jobs needs the storage to implement `IOrphanedJobReporter`. A provider that does not still fails those jobs, without calling any handler.

### Safety guarantees

!!! note
    - Dead-letter handlers are **optional**. Jobs without a registered handler are simply marked `Failed` and remain visible in the dashboard for manual requeue.
    - Handlers run in an isolated DI scope. An exception thrown inside a handler is logged and swallowed — it never crashes the dispatcher.
    - The `IDeadLetterHandler<TJob>` generic parameter is the **job type**, not the input type.
    - Handlers work for both `IJob` and `IJob<TInput>` implementations.

## How long failed jobs are kept

!!! warning
    Jobs in the `Failed` state are retained for 30 days by default (`RetentionFailed`), and dead-letter jobs for 60 days (`RetentionDeadLetter`), then automatically purged. Once a job is purged, its history and input are gone permanently. If you need longer retention for audit purposes, increase `options.RetentionFailed` — or set it to `TimeSpan.Zero` to keep failed jobs indefinitely. All retention options are in [Configuration](../reference/configuration.md).

## Next

- [Dead-letter forwarding](dead-letter-forwarding.md): copy an exhausted job to another topic or queue, or to any destination.
- [Alerts](../guides/alerts.md): be notified when a job fails for good.
- [Retries](retries.md): the policy that decides when a job is exhausted.
