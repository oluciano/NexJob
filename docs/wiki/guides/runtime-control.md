---
title: "Runtime Control: Pause, Requeue and Delete from Code"
sidebarTitle: "Runtime Control"
description: "Use IJobControlService to pause and resume queues, requeue failed jobs, delete jobs and reset circuit breakers without the dashboard."
---

`IJobControlService` gives you programmatic access to pause/resume queues, requeue or delete specific jobs, and reset a queue's circuit breaker — all without going through the dashboard UI. It is registered automatically as a singleton when you call `AddNexJob()`.

```csharp
public sealed class MaintenanceService(IJobControlService control)
{
    public async Task PerformMaintenanceAsync(JobId jobId, CancellationToken ct)
    {
        // Pause a queue to prevent workers from dequeuing new jobs
        await control.PauseQueueAsync("reports", ct);

        // Remove a specific job
        await control.DeleteJobAsync(jobId, ct);

        // Resume processing when ready
        await control.ResumeQueueAsync("reports", ct);
    }
}
```

<Note title="What pausing and deleting guarantee">
  - **Pausing takes effect on the next polling cycle**, not at the instant of the call. Each worker node reads the paused queues at the start of every cycle, so a job fetched by a cycle that was already in flight when you paused can still run. Jobs that are already running are never interrupted. If you must be sure that nothing starts, pause the queue and wait one polling interval.
  - **Pausing is shared by every node** that uses the same storage, because it is stored with the runtime settings.
  - **Deleting a running job does not stop it.** The job finishes, its result is discarded, and the deleted job never comes back. Deleting a job that is still waiting means it will never be fetched.
  - **Requeueing** a failed job resets its attempts to zero, so it gets a full set of attempts again.
</Note>

Other available methods: `RequeueJobAsync`, `ResetQueueCircuitAsync`.

## What each operation does

| Operation | Effect | The dashboard does the same in |
| --- | --- | --- |
| `PauseQueueAsync(queue)` | Workers skip the queue from their next polling cycle. Running jobs are not interrupted. | **Queues** |
| `ResumeQueueAsync(queue)` | Workers fetch from the queue again. | **Queues** |
| `RequeueJobAsync(id)` | A failed (dead-letter) job goes back to `Enqueued` with its attempts reset to 0. | **Failed / DLQ** |
| `DeleteJobAsync(id)` | The job and its logs are removed. A job that is running finishes, and its result is discarded. | **Jobs**, **Failed / DLQ** |
| `ResetQueueCircuitAsync(queue)` | The circuit breaker of the queue closes and its counters reset. | **Queues**, **Reset Circuit** |

There is **no operation to cancel a job that is already running**. To stop work in flight, pass the `CancellationToken` your job receives to everything it awaits and stop the host (a clean shutdown puts interrupted jobs back in the queue without using an attempt).

## Try it

The `NexJob.Sample.Reliability` sample exposes each operation as an endpoint: `POST /queues/{queue}/pause`, `POST /queues/{queue}/resume`, `POST /queues/{queue}/reset-circuit`, `POST /jobs/{id}/requeue` and `DELETE /jobs/{id}`.

## See also

- [Circuit Breaker](../guides/circuit-breaker.md)
- [Delivery Guarantees](../concepts/delivery-guarantees.md): what pausing, deleting and requeueing cost.
- [Dashboard](../integrations/dashboard.md): the same actions with a UI and authorization.
