---
title: "NexJob Default Queue and Queue Prefix: Where Jobs Without a Queue Go"
sidebarTitle: "Default queue and prefix"
description: "How NexJob names the default queue ({prefix}.default), how the prefix is derived and validated, how the legacy default queue is drained, and what to check when upgrading from a shared default."
---

A job enqueued without a queue goes to the **default queue of your application**, and that name carries a prefix so that applications sharing a database do not share a queue. This page has the rules and the upgrade traps. For routing jobs to named queues, see [Queues](queues.md).

The stored name is `{prefix}.default`, so applications that share a database do not share a queue. The prefix is `NexJobOptions.QueuePrefix` (or `NexJob:QueuePrefix` in `appsettings.json`). When it is not set, NexJob uses the full lowercase name of the entry assembly: `Acme.Billing.Worker` becomes `acme.billing.worker.default`. The name is never shortened, so `Acme.Billing.Worker` and `Acme.Logistics.Worker` cannot collide. An explicit prefix is stored exactly as you type it (trimmed): `QueuePrefix = "Billing"` gives `Billing.default`, so write the same case wherever you refer to that queue, because most databases tell `Billing.default` and `billing.default` apart. It may have at most 100 characters, no whitespace, and must not start or end with a dot; anything else throws `ArgumentException` when you set it.

```csharp
builder.Services.AddNexJob(options =>
{
    options.QueuePrefix = "billing"; // jobs without a queue go to "billing.default"
});
```

These rules apply everywhere a queue name is accepted: `EnqueueAsync`, `ScheduleAsync`, recurring jobs (in code and in `appsettings.json`), broker triggers (`TargetQueue`) and `NexJobOptions.Queues`.

- **Only the implicit default is prefixed.** `"default"` (or no queue) becomes `{prefix}.default`. A queue you name on purpose, such as `"emails"`, is used as it is.
- **A name with a dot is already qualified** and is never prefixed again. A producer in one service targets another service's default queue with `queue: "billing.default"`.
- **Configuration keyed by `"default"` follows the prefix.** `ConfigureQueue("default", ...)`, a circuit breaker, an execution window, pausing `default` and `ResetQueueCircuitAsync("default")` apply to `{prefix}.default` too. For pause and resume `default` and `{prefix}.default` are one queue: resuming either name resumes both, so the **Resume** button of the dashboard works whichever name paused the queue.
- **The legacy `default` queue is still drained.** Every host also polls `default`, so jobs stored before the upgrade still run. Nothing is renamed. A job of another application that sits in `default` is deferred back (see [Multi-Service](../guides/multi-service.md)). The execution window and the circuit breaker you configured for `"default"` also govern the legacy `default` (it keeps a circuit of its own), so old jobs are held back and protected like new ones. An explicit `queue: "default"` now means the prefixed queue: there is no way to enqueue into the legacy one.

!!! danger "Did you share `default` between services on purpose?"
    If service A enqueues without a queue and service B was meant to run those jobs by polling `default`, that stops working: A now stores them in `a.default`, which B never reads, and they stay `Enqueued` with no error. Name the queue on both sides (`queue: "orders"` in A, `Queues = ["orders"]` in B) or target B's default queue explicitly with `queue: "b.default"`. Check the dashboard for queues that have jobs and no node polling them.

!!! warning "Set the prefix yourself in production"
    A derived prefix changes when the entry assembly is renamed, and jobs already stored stay in the old queue, which the drain does not cover (it only reads `default`). Set `QueuePrefix` explicitly so the name survives renames and refactors. The host logs a warning at startup while the prefix is derived. If jobs are stranded anyway, the dashboard shows the queue as unattended and tells you which `QueuePrefix` brings it back.

!!! warning "No entry assembly, or a shared host"
    Some hosts have no entry assembly, and others run many applications under one entry assembly (for example a shared runner). In the first case no prefix is derived and the shared `default` comes back: the host logs a warning saying so. In the second, every application derives the same prefix and shares a queue again. In both cases set `QueuePrefix` explicitly.

!!! note "Queue scope in the dashboard"
    In `DashboardOptions.Queues`, `default` covers the application's `{prefix}.default` and the legacy `default` (the legacy row appears only while it holds jobs), so a scope written before the prefix existed keeps working. Any other entry is matched literally, so list another application's queue by its full name. Scopes of other clusters are literal too. Jobs the dashboard creates itself (Scenarios, **Trigger now**) without a queue go to `{prefix}.default`.

    A standalone dashboard is a separate process with its own entry assembly: set the same `NexJob:QueuePrefix` as the workers, or it derives a different prefix and shows and enqueues into the wrong queue. See [Queue Scoping](../integrations/dashboard.md).

Recurring job ids and `[Throttle]` resources are not prefixed; they are still global to the database.

## Next

- [Queues](queues.md): routing, which queues a host polls, fetch order and per-queue settings.
- [Multi-Service](../guides/multi-service.md): several services on one database.
- [Migration guide](../reference/migration.md): the change that introduced the prefix.
