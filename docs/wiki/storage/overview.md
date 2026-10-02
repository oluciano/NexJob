---
title: "Storage Providers: Choosing the Right Backend for NexJob"
sidebarTitle: "Overview"
description: "NexJob supports five storage backends: InMemory, PostgreSQL, SQL Server, Redis, and MongoDB. Learn which to choose for development and production."
---

NexJob decouples job scheduling from persistence through a single `IStorageProvider` abstraction. Every backend — from the zero-config in-memory default to a production-grade PostgreSQL cluster — plugs in through the same registration pattern: call the provider's registration method before `AddNexJob()`, and NexJob picks it up automatically. This page explains what each backend offers so you can match the right one to your workload.

## Provider comparison

| Provider | NuGet package | Recommended for | Key notes |
|---|---|---|---|
| **InMemory** | *(built-in)* | Development, unit tests | Zero dependencies; data lost on restart |
| **PostgreSQL** | `NexJob.Postgres` | Production (relational) | Full ACID, `FOR UPDATE SKIP LOCKED`, read replica support, vectorized batch ack |
| **SQL Server** | `NexJob.SqlServer` | Production (relational) | Full ACID, `UPDLOCK READPAST`, read replica support, vectorized batch ack |
| **Redis** | `NexJob.Redis` | Production (high-throughput) | Microsecond dispatch, Lua-script batching, distributed throttle support |
| **MongoDB** | `NexJob.MongoDB` | Production (document) | `FindOneAndUpdate` atomics, `UpdateManyAsync` batch ack, auto index creation |

## Storage interfaces

`IStorageProvider` is a composed interface built from three focused contracts. All built-in providers implement all three — you never need to register them separately unless you want to customize a specific concern.

| Interface | Responsibility | Inject directly when you need… |
|---|---|---|
| `IJobStorage` | Job enqueue, dequeue, and worker coordination | Custom job execution or worker logic |
| `IRecurringStorage` | Recurring job scheduling and lease management | Custom recurring job scheduling logic |
| `IDashboardStorage` | Dashboard queries, job lists, and metrics | Custom reporting, admin tooling, or a read replica |

For most applications, inject `IStorageProvider` to access all three concerns through one dependency. When you only need one aspect — for example, pointing dashboard reads at a read replica — override just `IDashboardStorage` without touching the others.

## Programmatic job control

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

Other available methods: `RequeueJobAsync`, `ResetQueueCircuitAsync`.

## InMemory (default)

The InMemory provider is built into the `NexJob` core package and requires no additional dependencies. It is the default when you call `AddNexJob()` without any prior storage registration.

```csharp
// Implicit — InMemory is used when no storage provider is registered first
builder.Services.AddNexJob();

// Explicit, same result
builder.Services.AddNexJob(options => options.UseInMemory());
```

!!! warning
    InMemory is **not suitable for production**. All jobs are held in process memory and are permanently lost when the application restarts or crashes. Use it for local development and automated tests only.


## Choose your provider

<div class="grid cards" markdown>
  -   [**PostgreSQL**](../storage/postgresql.md)

    Full ACID transactions, `FOR UPDATE SKIP LOCKED` concurrency, dashboard read replica support, and vectorized batch acknowledgment.

  -   [**SQL Server**](../storage/sql-server.md)

    Full ACID transactions, `UPDLOCK READPAST` row locking, Azure SQL read replica support, and vectorized batch acknowledgment.

  -   [**Redis**](../storage/redis.md)

    Microsecond-latency dispatch, server-side Lua script batching, sorted-set priority queues, and cluster-wide distributed throttling.

  -   [**MongoDB**](../storage/mongodb.md)

    Document-model storage, `FindOneAndUpdate` atomic transitions, `UpdateManyAsync` batch acknowledgment, and automatic index creation.

</div>
