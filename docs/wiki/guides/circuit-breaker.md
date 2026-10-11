---
title: "Queue Circuit Breaker: Stop Hammering a Failing Dependency"
sidebarTitle: "Circuit Breaker"
description: "Pause a NexJob queue automatically when a downstream service is down, probe it with a canary job, and ramp back up without a thundering herd."
---

`[Throttle]` governs steady-state concurrency. The **queue circuit breaker** handles severe downstream outages — it pauses an entire queue when consecutive failures reach a threshold, lets a canary job probe recovery, and gradually ramps concurrency back up to prevent thundering herds.

Configure it per queue with `ConfigureQueue`. The values below are an example; the defaults are listed after it.

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

            // Trip on network failures, 5xx, 408, 429 and 401
            cb.BreakOnTransientHttpErrors(includeAuthErrors: true);
            cb.BreakOn<TimeoutException>();
        });
    });
});
```

| Option | Default | Meaning |
|---|---|---|
| `ConsecutiveFailuresThreshold` | `5` | Failures in a row that open the circuit. |
| `OpenDuration` | 1 minute | The first cooldown before a canary job is tried. |
| `BackoffMultiplier` | `2.0` | Multiplies the cooldown each time the canary fails. |
| `MaxOpenDuration` | 15 minutes | Upper bound for the cooldown. |
| `RecoveryConcurrency` | `2` | Concurrency allowed while recovering. |
| `RecoveryDuration` | 2 minutes | How long the recovery ramp lasts before the circuit closes. |

## Which failures count

- **Without any rule, every exception counts.** `BreakOn<T>()` and `BreakOnTransientHttpErrors()` narrow that down. A failure counts if **any** rule matches it. A failure that matches none is ignored: it does not count towards the threshold.
- **A success resets the count.** The threshold is about failures in a row.
- **`BreakOnTransientHttpErrors()` matches `HttpRequestException` only**, and counts: a network-level failure (no status code, such as DNS, a refused connection or a TLS failure), any `5xx`, `408` and `429`, and `401` unless you pass `includeAuthErrors: false`. Other `4xx` codes (`400`, `403`, `404`, `422` and so on) do not trip the circuit.
- **Timeouts that do not arrive as `HttpRequestException`** (for example a `TimeoutException`) need their own rule, such as `cb.BreakOn<TimeoutException>()`.

## Circuit states

<div class="grid cards" markdown>
  -   **Closed**

    Normal processing. All jobs execute at full worker concurrency.


  -   **Open**

    Consecutive failures exceeded the threshold. The queue is paused and jobs accumulate in storage without burning retries. If the canary fails, the circuit opens again with a longer cooldown: `OpenDuration × BackoffMultiplier^(failed canaries)`, never above `MaxOpenDuration`.


  -   **Half-Open**

    Cooldown elapsed. One canary job is dispatched to probe downstream health.


  -   **Recovering**

    Canary succeeded. Concurrency is capped at `RecoveryConcurrency` for `RecoveryDuration`, then the circuit closes and full throughput resumes.

</div>

## Programmatic control

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
- Each transition is logged at `Information` level, for example `circuit transitioned Open -> HalfOpen`.
- There is **no metric** for the circuit state yet. To be told when it opens, alert on jobs piling up in the queue (`nexjob.queue.depth`); see [Alerts](../guides/alerts.md).

## Try it

The `NexJob.Sample.Reliability` sample trips a circuit with `POST /circuit/trip`, shows a probe job waiting with `POST /circuit/probe`, and closes the circuit with `POST /queues/fragile/reset-circuit`.

## See also

- [Throttling](../guides/throttling.md): steady-state concurrency limits, which work together with the breaker.
- [Retries](../concepts/retries.md): what happens to one job that keeps failing.
- [Runtime Control](../guides/runtime-control.md): reset a circuit from code.
