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

The stored name is `{prefix}.default`, so applications that share a database do not share a queue. The prefix is `NexJobOptions.QueuePrefix` (or `NexJob:QueuePrefix` in `appsettings.json`). When it is not set, NexJob uses the full lowercase name of the entry assembly: `Acme.Billing.Worker` becomes `acme.billing.worker.default`. The name is never shortened, so `Acme.Billing.Worker` and `Acme.Logistics.Worker` cannot collide. An explicit prefix is stored lowercase too, may have at most 100 characters, no whitespace, and must not start or end with a dot; anything else throws `ArgumentException` when you set it.

```csharp
builder.Services.AddNexJob(options =>
{
    options.QueuePrefix = "billing"; // jobs without a queue go to "billing.default"
});
```

These rules apply everywhere a queue name is accepted: `EnqueueAsync`, `ScheduleAsync`, recurring jobs (in code and in `appsettings.json`), broker triggers (`TargetQueue`) and `NexJobOptions.Queues`.

- **Only the implicit default is prefixed.** `"default"` (or no queue) becomes `{prefix}.default`. A queue you name on purpose, such as `"emails"`, is used as it is.
- **A name with a dot is already qualified** and is never prefixed again. A producer in one service targets another service's default queue with `queue: "billing.default"`.
- **Configuration keyed by `"default"` follows the prefix.** `ConfigureQueue("default", ...)`, a circuit breaker, an execution window, pausing `default` and `ResetQueueCircuitAsync("default")` apply to `{prefix}.default` too.
- **The legacy `default` queue is still drained.** Every host also polls `default`, so jobs stored before the upgrade still run. Nothing is renamed. A job of another application that sits in `default` is deferred back (see [Multi-Service](../guides/multi-service.md)). The drain is not covered by the execution window or circuit breaker you configured for `"default"` (they apply to the prefixed queue), and an explicit `queue: "default"` now means the prefixed queue: there is no way to enqueue into the legacy one.

!!! danger "Did you share `default` between services on purpose?"
    If service A enqueues without a queue and service B was meant to run those jobs by polling `default`, that stops working: A now stores them in `a.default`, which B never reads, and they stay `Enqueued` with no error. Name the queue on both sides (`queue: "orders"` in A, `Queues = ["orders"]` in B) or target B's default queue explicitly with `queue: "b.default"`. Check the dashboard for queues that have jobs and no node polling them.

!!! warning "Set the prefix yourself in production"
    A derived prefix changes when the entry assembly is renamed, and jobs already stored stay in the old queue, which the drain does not cover (it only reads `default`). Set `QueuePrefix` explicitly so the name survives renames and refactors. The host logs a warning at startup while the prefix is derived.

!!! warning "No entry assembly, or a shared host"
    Some hosts have no entry assembly, and others run many applications under one entry assembly (for example a shared runner). In the first case no prefix is derived and the shared `default` comes back: the host logs a warning saying so. In the second, every application derives the same prefix and shares a queue again. In both cases set `QueuePrefix` explicitly.

!!! note "Queue scope in the dashboard"
    `DashboardOptions.Queues` lists stored names. To show the default queue, list `{prefix}.default` (and `default` while old jobs remain). See [Queue Scoping](../integrations/dashboard.md).

Recurring job ids and `[Throttle]` resources are not prefixed; they are still global to the database.

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

Use `ConfigureQueue` to attach behavior to one queue. The name match is case-insensitive, and calling it again with the same name edits the same settings.

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

Do not confuse an external message broker queue (RabbitMQ, Kafka, AWS SQS) with a NexJob queue:

- **Message Broker Queue (Transport):** A broker queue or topic transports messages across network boundaries between services. Messages are transient and consumed off the network buffer.
- **NexJob Queue (Governance & Execution):** A NexJob queue is a persistent logical partition inside your storage database (PostgreSQL, MongoDB, SQL Server, Redis). It governs execution concurrency, rate throttling (`[Throttle]`), execution windows (for example, 22:00 to 06:00), and automated [queue circuit breaking](../guides/circuit-breaker.md).

When using broker triggers (for example, `NexJob.RabbitMQ`), the trigger consumes off the RabbitMQ transport queue and enqueues into a NexJob storage queue (set with `TargetQueue`). This protects your downstream services: if an external API fails, NexJob's circuit breaker automatically pauses the logical queue without dropping messages or overwhelming your message broker.

## Monitor queues

- The dashboard **Queues** page shows each queue's depth, active workers, pause controls and circuit state, and flags queues that have jobs but no active workers.
- The OpenTelemetry metric `nexjob.queue.depth` lets you alert on a queue that is not draining. See [OpenTelemetry](../integrations/opentelemetry.md) and [Alerts](../guides/alerts.md).
