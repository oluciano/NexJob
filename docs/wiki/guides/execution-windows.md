---
title: "Execution Windows: Run a Queue Only at Certain Hours"
sidebarTitle: "Execution Windows"
description: "Restrict a NexJob queue to a time window, for example batch imports at night, with time zones and windows that cross midnight."
---

For scenarios where you need to restrict a queue to specific time windows — for example, batch imports that should only run during off-peak hours — configure `ExecutionWindow` on the queue. Workers skip that queue outside the window; jobs accumulate safely in storage and execute as soon as the window opens.

```csharp
builder.Services.AddNexJob(options =>
{
    options.ConfigureQueue("batch-imports", queue =>
    {
        queue.ExecutionWindow = new ExecutionWindowSettings
        {
            StartTime = new TimeOnly(22, 0),   // 10 PM
            EndTime   = new TimeOnly(6, 0),    // 6 AM (crosses midnight)
            TimeZone  = "America/New_York",    // IANA or Windows timezone ID
        };
    });
});
```

`ExecutionWindowSettings` handles windows that cross midnight automatically — set `StartTime` after `EndTime` to define an overnight window (e.g. 22:00 – 06:00).

!!! note
    `ExecutionWindow` only controls when a queue's workers fetch jobs. Throttling with `[Throttle]` and circuit breakers apply independently within those windows.

## When to use it

- Batch imports, reports or clean-ups that should not compete with daytime traffic.
- Work that a partner system only accepts in a maintenance window.

Jobs enqueued outside the window are not lost: they wait in the queue and start when the window opens.

!!! warning
    A job with `deadlineAfter` keeps its clock running while it waits for the window. If the deadline is shorter than the wait, the job expires instead of running. See [Delivery Guarantees](../concepts/delivery-guarantees.md).

## See also

- [Configuration](../reference/configuration.md#executionwindowsettings): every setting of the window.
- [Circuit Breaker](circuit-breaker.md) and [Throttling](throttling.md): the other queue-level controls.
