---
title: "Queue Circuit Breaker: Stop Hammering a Failing Dependency"
sidebarTitle: "Circuit Breaker"
description: "Pause a NexJob queue automatically when a downstream service is down, probe it with a canary job, and ramp back up without a thundering herd."
---

`[Throttle]` governs steady-state concurrency. The **queue circuit breaker** handles severe downstream outages — it pauses an entire queue when consecutive failures reach a threshold, lets a canary job probe recovery, and gradually ramps concurrency back up to prevent thundering herds.

Configure it per queue with `ConfigureQueue`:

```csharp
builder.Services.AddNexJob(options =>
{
    options.ConfigureQueue("payments", queue =>
    {
        queue.EnableCircuitBreaker(cb =>
        {
            cb.ConsecutiveFailuresThreshold = 5;
            cb.OpenDuration = TimeSpan.FromSeconds(30);   // first cooldown
            cb.BackoffMultiplier = 2.0;                   // doubles on each repeated failure
            cb.MaxOpenDuration = TimeSpan.FromMinutes(10);
            cb.RecoveryDuration = TimeSpan.FromMinutes(2);
            cb.RecoveryConcurrency = 2;                   // anti-thundering-herd ramp-up

            // Trip on 5xx, timeouts, 429, 401, and network drops
            cb.BreakOnTransientHttpErrors(includeAuthErrors: true);
            cb.BreakOn<TimeoutException>();
        });
    });
});
```

## Circuit States

<div class="grid cards" markdown>
  -   **Closed**

    Normal processing. All jobs execute at full worker concurrency.

  -   **Open**

    Consecutive failures exceeded the threshold. The queue is paused and jobs accumulate in storage without burning retries. Exponential backoff multiplies the cooldown on repeated probe failures.

  -   **Half-Open**

    Cooldown elapsed. One canary job is dispatched to probe downstream health.

  -   **Recovering**

    Canary succeeded. Concurrency is capped at `RecoveryConcurrency` for `RecoveryDuration` to let the downstream service stabilise before full throughput resumes.

</div>

## Programmatic Control

You can reset the circuit breaker from code — useful when you receive a recovery webhook from the downstream provider:

```csharp
await jobControlService.ResetQueueCircuitAsync("payments");
```

The dashboard at `/queues` also shows circuit state visually and exposes a **Reset Circuit** button for operators.

## What it does to your jobs

- **Retries are not burned.** While the circuit is open the queue is not fetched, so waiting jobs do not fail and do not use attempts.
- **Deadlines keep running.** A job with `deadlineAfter` that waits past its deadline while the circuit is open is marked `Expired` when a worker finally reaches it. See [Delivery Guarantees](../concepts/delivery-guarantees.md).
- **Per queue.** Only the queue that has `EnableCircuitBreaker` is affected; other queues keep running.

## Seeing it

- The dashboard **Queues** page shows the circuit state of each queue and has a **Reset Circuit** button. See [Dashboard](../integrations/dashboard.md#dashboard-pages).
- There is **no metric** for the circuit state yet. To be told when it opens, alert on jobs piling up in the queue (`nexjob.queue.depth`); see [Alerts](alerts.md).

## Try it

The `NexJob.Sample.Reliability` sample trips a circuit with `POST /circuit/trip`, shows a probe job waiting with `POST /circuit/probe`, and closes the circuit with `POST /queues/fragile/reset-circuit`.

## See also

- [Throttling](throttling.md): steady-state concurrency limits, which work together with the breaker.
- [Retries & Dead Letter](../concepts/retries-and-dead-letter.md): what happens to one job that keeps failing.
- [Runtime Control](runtime-control.md): reset a circuit from code.
