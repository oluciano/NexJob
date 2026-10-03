---
title: "Several Services on One Database: Queues and Foreign Jobs"
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

## Layer 2: foreign jobs are deferred, not failed

If a service fetches a job whose type (or input type) it cannot load, the job is a **foreign job**. NexJob does not run it, does not use an attempt and does not dead-letter it. It returns the job to the queue after `ForeignJobRetryDelay` (default 5 seconds) so the owning service can take it. The host logs a warning (`references foreign type ... Deferring`) and the trace of the attempt carries `nexjob.foreign_job = true`.

This is a safety net, not a design. If two services poll the **same** queue they keep handing each other's jobs back: nothing breaks, but fetches are wasted and execution is delayed. A foreign job that no running service owns waits in the queue, visible in the dashboard, until one of them is deployed.

| Setup | Result |
|---|---|
| One queue per service | Clean. No foreign fetches. |
| Several services on the same queue | Works, with wasted fetches and delays while jobs bounce. |
| A job type that no running service has | The job waits as `Enqueued` and is deferred each time a service fetches it. Deploy the owner, or delete the job. |

## Dashboards per service

Run one dashboard per team, limited to that team's queues, so the counters and lists show only relevant jobs. A dashboard container can also run with `DisableWorkers = true` so it never takes worker slots from the services. See [Queue Scoping](../integrations/dashboard.md#queue-scoping-for-multi-service-architectures).

## Things that are shared

- **Pausing a queue** is stored in the database, so it applies to every node of every service that polls that queue. See [Runtime Control](runtime-control.md).
- **Recurring jobs** are stored in the database. Give each recurring job an id and a queue that belong to one service.
- **Retention and the orphan watcher** run in every node. They act on the whole database, not only on the jobs of their own service.

## Upgrading some services before others

Services on different NexJob versions can share a database during a rolling upgrade, with the limits listed in the [migration guide](../reference/migration.md#v570-v580) (for example, on MongoDB do not use `deadlineAfter` until every node runs v5.8).

## See also

- [Configuration](../reference/configuration.md): `Queues` and `ForeignJobRetryDelay`.
- [FAQ: can several microservices share the same database?](../reference/faq.md)
- [Delivery Guarantees](../concepts/delivery-guarantees.md)
