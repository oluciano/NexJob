---
title: "NexJob Queues: Routing, Ordering, Isolation and Control"
sidebarTitle: "Queues"
description: "How NexJob queues work: route jobs to a queue, choose which queues each host polls and in what order, and control them with throttles, windows and pauses."
---

A NexJob queue is a named logical partition of the jobs stored in your database. Every job belongs to exactly one queue, and each host decides which queues it polls. Almost every operational control in NexJob (pausing, circuit breaking, execution windows, dashboard scoping, service isolation) works at the queue level, so this page is the starting point for all of them.

## The default queue

When you enqueue without a `queue` argument, the job goes to the **default queue of your application**. A host polls it unless you configure something else, so a single-service app works without ever naming a queue.

```csharp
// Both jobs land in the default queue of this application
await scheduler.EnqueueAsync<SendEmailJob, SendEmailInput>(input, cancellationToken: ct);
await scheduler.ScheduleAsync<CleanupJob>(TimeSpan.FromHours(1), cancellationToken: ct);
```

The stored name is `{prefix}.default`, so applications that share a database do not share a queue. Set `NexJobOptions.QueuePrefix` yourself in production. [The default queue and the queue prefix](default-queue.md) has the prefix rules, how the legacy `default` queue is drained, and what to check on a database shared between services.

!!! danger "Did you share `default` between services on purpose?"
    If service A enqueues without a queue and service B was meant to run those jobs by polling `default`, that stops working: A stores them in `a.default`, which B never reads, and they stay `Enqueued` with no error. Name the queue on both sides. The [details and the fix](default-queue.md) are on the next page.

## Route a job to a queue

Pass `queue` on `EnqueueAsync`, `ScheduleAsync` or `ScheduleAtAsync`:

```csharp
await scheduler.EnqueueAsync<HeavyComputationJob>(
    queue: "compute",
    cancellationToken: ct);

await scheduler.EnqueueAsync<SendEmailJob, SendEmailInput>(
    input,
    queue: "notifications",
    cancellationToken: ct);
```

A queue does not need to be created first. It exists as soon as a job uses its name.

## Choose which queues a host polls

`NexJobOptions.Queues` lists the queues the workers on this host fetch from. A host never fetches a job from a queue that is not in its list.

=== "Program.cs"

    ```csharp
    builder.Services.AddNexJob(options =>
    {
        options.Queues = ["critical", "default", "reports"];
    });
    ```

=== "appsettings.json"

    ```json
    {
      "NexJob": {
        "Queues": ["critical", "default", "reports"]
      }
    }
    ```


!!! warning
    A job enqueued to a queue that no running host polls stays `Enqueued` forever. It is not an error and it is not retried. Check the dashboard **Queues** page when jobs pile up without being processed.


## Fetch order: queue first, then priority

The order of `Queues` matters. When a worker fetches work, it sorts the waiting jobs by:

1. **Position of the queue in `Queues`.** Jobs from the first queue come before jobs from the second.
2. **[Priority](../concepts/scheduling.md#priority)** (`Critical` before `Low`) within the same queue.
3. **Creation time** (oldest first).

So a `Low` priority job in `critical` is fetched before a `Critical` priority job in `reports`. Use queue order to rank whole workloads and priority to rank jobs inside one workload.

!!! note
    All queues on a host share one worker pool, sized by `NexJobOptions.Workers`. There is no per-queue worker count: a `Workers` value inside `QueueSettings` is ignored and reported at startup. To dedicate capacity to a queue, run a separate host (or deployment) that polls only that queue.


## Per-queue settings

Use `ConfigureQueue` to attach behavior to one queue. Calling it again with the same name edits the same settings.

!!! note "Queue names are case-sensitive"
    Queue names are matched exactly, with the same rule as the storage: `Payments` and `payments` are two different queues, in settings, execution windows, pause and circuit breakers alike. Write the name with the same case in `ConfigureQueue` and where you enqueue. A startup warning names a configured queue that differs only by case from one the host polls.

    SQL Server is the exception on the storage side: its default collation ignores case, so the database itself (the fetch and the dashboard filter) does not tell `Payments` from `payments` unless the database uses a case-sensitive collation.

```csharp
builder.Services.AddNexJob(options =>
{
    options.ConfigureQueue("payments", queue =>
    {
        queue.EnableCircuitBreaker(cb => { /* thresholds */ });
    });

    options.ConfigureQueue("imports", queue =>
    {
        queue.ExecutionWindow = new ExecutionWindowSettings
        {
            StartTime = new TimeOnly(22, 0),
            EndTime   = new TimeOnly(6, 0),
            TimeZone  = "America/Sao_Paulo",
        };
    });
});
```

<div class="grid cards" markdown>
  -   [**Circuit Breaker**](../guides/circuit-breaker.md)

    Pause a queue automatically when a downstream dependency keeps failing, then probe and ramp back up.

  -   [**Execution Windows**](../guides/execution-windows.md)

    Restrict a queue to a time range, including windows that cross midnight.

</div>

Concurrency limits are set per **resource** with `[Throttle]`, not per queue. They often go together: keep heavily throttled jobs in their own queue so they do not compete with the rest of your workload. See [Throttling](../guides/throttling.md).

## Pause, resume and reset at runtime

Queues can be controlled without a redeploy, from code with `IJobControlService` or from the dashboard **Queues** page:

```csharp
await control.PauseQueueAsync("reports", ct);
await control.ResumeQueueAsync("reports", ct);
await control.ResetQueueCircuitAsync("payments", ct);
```

- The pause is stored in the database, so it applies to every node of every service that polls that queue.
- It takes effect on each node's next polling cycle. Running jobs are never interrupted.

See [Runtime Control](../guides/runtime-control.md) for every operation and its guarantees.

## Isolate services and workloads

Queues are the main isolation tool in NexJob:

- **Between workloads:** put slow or heavy jobs in their own queue and poll it from a dedicated host, so they never take worker slots from user-facing jobs.
- **Between services:** when several services share one database, give each service its own queues. A service then never fetches a job type it cannot load. See [Multi-Service](../guides/multi-service.md).
- **In the dashboard:** limit a dashboard to a team's queues with `DashboardOptions.Queues`. See [Queue Scoping](../integrations/dashboard.md#queue-scoping-for-multi-service-architectures).

## Broker queues vs NexJob queues

A NexJob queue is not a message-broker queue (RabbitMQ, Kafka, AWS SQS). A broker queue or topic transports messages between services. A NexJob queue is a persistent partition in your storage database that governs execution: concurrency, `[Throttle]`, execution windows and [queue circuit breaking](../guides/circuit-breaker.md).

A broker trigger (for example `NexJob.RabbitMQ`) consumes from the broker and enqueues into a NexJob queue, set with `TargetQueue`. If an external API fails, the circuit breaker pauses that NexJob queue instead of dropping messages or overloading the broker.

## Monitor queues

- The dashboard **Queues** page shows each queue's depth, active workers, pause controls and circuit state, and flags queues that have jobs but no active workers.
- The OpenTelemetry metric `nexjob.queue.depth` lets you alert on a queue that is not draining. See [OpenTelemetry](../integrations/opentelemetry.md) and [Alerts](../guides/alerts.md).
