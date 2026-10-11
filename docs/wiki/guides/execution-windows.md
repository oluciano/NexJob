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
            TimeZone  = "America/New_York",    // IANA or Windows time zone id (default: "UTC")
        };
    });
});
```

`ExecutionWindowSettings` handles windows that cross midnight automatically — set `StartTime` after `EndTime` to define an overnight window (e.g. 22:00 – 06:00).

!!! note
    `ExecutionWindow` only controls when a queue's workers fetch jobs. A job that is already running when the window closes is not interrupted. Throttling with `[Throttle]` and circuit breakers apply independently within the window.

The window is checked on every polling cycle. The start and the end time are both inside the window, and `TimeZone` defaults to `UTC`.

!!! warning "Check the time zone id"
    An unknown `TimeZone` is not validated when you configure it. It throws a `TimeZoneNotFoundException` on the first polling cycle and, because nothing handles it, the host stops, **including the queues that have no window**. On Linux the zone ids come from the image, so make sure it has time zone data (the sample `Dockerfile` installs `tzdata`). This is tracked in [#472](https://github.com/oluciano/NexJob/issues/472).


## Only on some days

Add `DaysOfWeek` to limit the window to certain days, for example partner syncs and reports on business days:

```csharp
queue.ExecutionWindow = new ExecutionWindowSettings
{
    StartTime  = new TimeOnly(8, 0),
    EndTime    = new TimeOnly(18, 0),
    TimeZone   = "America/Sao_Paulo",
    DaysOfWeek = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
};
```

It is also available in `appsettings.json`, by day name: `"DaysOfWeek": ["Monday", "Friday"]`.

- **The default is every day.** `null` or an empty array means no day restriction, so existing windows behave as before. Duplicates are ignored.
- **The day is the local day** in `TimeZone`, not the UTC day. Sunday 23:30 UTC is still Sunday evening in New York.
- **An overnight window belongs to the day it starts.** With `22:00`–`06:00` and only Friday selected, Saturday 03:00 is inside the window and Saturday 23:00 is not.
- **`StartTime` equal to `EndTime` is a 24-hour window.** With `DaysOfWeek` it means "any hour, only on these days", for example `00:00`–`00:00` on Monday to Friday.

The dashboard shows the window under the queue name (`08:00–18:00 America/Sao_Paulo · Mon–Fri`), so an `Outside Window` badge on a weekend is easy to explain. Calendar exceptions such as holidays are not supported.

## When to use it

- Batch imports, reports or clean-ups that should not compete with daytime traffic.
- Work that a partner system only accepts in a maintenance window.

Jobs enqueued outside the window are not lost: they wait in the queue and start when the window opens.

!!! warning
    A job with `deadlineAfter` keeps its clock running while it waits for the window. If the deadline is shorter than the wait, the job expires instead of running. See [Delivery Guarantees](../concepts/delivery-guarantees.md).


## See also

- [Configuration](../reference/configuration.md#executionwindowsettings): every setting of the window.
- [Circuit Breaker](../guides/circuit-breaker.md) and [Throttling](../guides/throttling.md): the other queue-level controls.
