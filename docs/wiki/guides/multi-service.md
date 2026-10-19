---
title: "Multi-Service: Queues and Foreign Jobs Across Microservices"
sidebarTitle: "Multi-Service"
description: "Run several microservices on the same NexJob storage safely: queue isolation, how jobs of another service are deferred, dashboards per service and mixed versions during an upgrade."
---

Several services can share one NexJob database. Each service enqueues jobs of its own types, and a service must only run what it can load. NexJob gives you two layers for that.

## Layer 1: give each service its own queues

A host fetches only from the queues in `NexJobOptions.Queues`. If every service lists its own queue names, a service never even sees another service's jobs.

```csharp
// Billing service
builder.Services.AddNexJob(options => options.Queues = ["billing", "invoices"]);

// Inventory service
builder.Services.AddNexJob(options => options.Queues = ["inventory"]);
```

Enqueue with the queue of the service that owns the job type (`queue: "billing"`). This is the recommended pattern.

Jobs enqueued without a queue also stay apart: the default queue is `{prefix}.default`, one per application. See [the default queue and prefix](../concepts/queues.md#the-default-queue).

## Layer 2: foreign jobs are deferred, not failed

If a service fetches a job whose type (or input type) it cannot load, the job is a **foreign job**. NexJob does not run it, does not use an attempt and does not dead-letter it. It returns the job to the queue after `ForeignJobRetryDelay` (default 5 seconds) so the owning service can take it. The host logs a warning (`references foreign type ... Deferring`) and the trace of the attempt carries the tag `nexjob.foreign_job = true`.

The executor catches `ForeignJobTypeException` for this case and defers the job by `ForeignJobRetryDelay` (`NexJobOptions`, 5 seconds by default).

This is a safety net, not a design. If two services poll the **same** queue they keep handing each other's jobs back: nothing breaks, but fetches are wasted and execution is delayed. A foreign job that no running service owns waits in the queue, visible in the dashboard, until one of them is deployed.

| Setup | Result |
| --- | --- |
| One queue per service | Clean. No foreign fetches. |
| Several services on the same queue | Works, with wasted fetches and delays while jobs bounce. |
| A job type that no running service has | The job waits as `Enqueued` and is deferred each time a service fetches it. Deploy the owner, or delete the job. |

## Dashboards per service

Run one dashboard per team, limited to that team's queues, so the counters and lists show only relevant jobs. A dashboard container can also run with `DisableWorkers = true` so it never takes worker slots from the services. See [Queue Scoping](../integrations/dashboard.md#queue-scoping-for-multi-service-architectures).

## Things that are shared

- **Pausing a queue** is stored in the database, so it applies to every node of every service that polls that queue. See [Runtime Control](../guides/runtime-control.md).
- **Recurring jobs** are stored in the database. Give each recurring job an id and a queue that belong to one service; see [recurring job ids](../concepts/recurring-jobs.md#recurring-job-ids-are-global-to-the-database).
- **Retention and the orphan watcher** run in every node. They act on the whole database, not only on the jobs of their own service.
- **A crashed job is failed by a node of its own service.** The orphan watcher scans the whole database, but a node fails a job that has no attempts left only when it can load the job type, so the typed `IDeadLetterHandler<TJob>` runs in the service that owns the job. A node that cannot load the type logs a `Warning` (`... this node cannot load that type`) and leaves the job `Processing` for a node that can. A job with attempts left is still given back to its queue by any node. If every node of the owning service is down, the job stays `Processing` until one of them starts.

## Upgrading some services before others

Services on different NexJob versions can share a database during a rolling upgrade. The limits of each step are in the [migration guide](../reference/migration.md). Two that matter here:

- **v5.7 to v5.8.** On MongoDB, do not use `deadlineAfter` until every node runs v5.8, because a v5.7 node cannot read a job that carries a deadline. See [the rolling upgrade table](../reference/migration.md#rolling-upgrade-v57-and-v58-nodes-on-the-same-storage).
- **v5 to v6.** Nodes of both versions can run together and every job runs once, but jobs enqueued by a v6 node wait in `{prefix}.default` until a v6 node reads them. See [the v6.0.0 migration](../reference/migration.md#v5100-v600).

## See also

- [Configuration](../reference/configuration.md): `Queues` and `ForeignJobRetryDelay`.
- [FAQ: can several microservices share the same database?](../reference/faq.md)
- [Delivery Guarantees](../concepts/delivery-guarantees.md)
