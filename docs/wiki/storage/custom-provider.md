---
title: "Writing a NexJob Storage Provider: The Interfaces and the Rules"
sidebarTitle: "Writing a provider"
description: "The interfaces a NexJob storage provider implements, how to register one, and the rules the built-in providers follow: atomic fetch, idempotent commit and a single source of truth."
---

NexJob ships providers for PostgreSQL, SQL Server, Redis, MongoDB and in-memory storage. This page is for the case where none of them fits and you need another backend. It lists the interfaces, how to register a provider and the rules it has to keep. A provider is a substantial piece of work, so check [Storage Providers](overview.md) first.

If you want a provider to ship as part of NexJob, the steps are in [Adding a storage provider](https://github.com/oluciano/NexJob/blob/develop/.github/CONTRIBUTING.md#adding-a-storage-provider).

## The interfaces

A provider implements `IStorageProvider`, which is the composition of three interfaces:

| Interface | Used for |
|---|---|
| `IJobStorage` | The hot path: enqueue, fetch, commit the result, heartbeats, orphans, continuations, progress, retention, server registry. |
| `IRecurringStorage` | Recurring job definitions, due schedules and the lock that stops two nodes from firing the same schedule. |
| `IDashboardStorage` | The read side and the operator actions of the dashboard. |

Alongside them there is `IRuntimeSettingsStore`, which keeps settings that operators change at runtime (such as paused queues), and an optional `IOrphanedJobReporter`.

## Registering a provider

Register the concrete class once and expose it through each interface. `AddNexJob()` only adds the in-memory provider when none is registered, so register yours before it:

```csharp
services.AddSingleton(_ => new MyStorageProvider(connectionString));
services.AddSingleton<IStorageProvider>(sp => sp.GetRequiredService<MyStorageProvider>());
services.AddSingleton<IJobStorage>(sp => sp.GetRequiredService<MyStorageProvider>());
services.AddSingleton<IRecurringStorage>(sp => sp.GetRequiredService<MyStorageProvider>());
services.AddSingleton<IDashboardStorage>(sp => sp.GetRequiredService<MyStorageProvider>());
services.AddSingleton<IRuntimeSettingsStore>(_ => new MyRuntimeSettingsStore(connectionString));

services.AddNexJob();
```

This is what the PostgreSQL provider's `AddNexJobPostgres` does.

## The rules

These are the invariants the built-in providers keep, and the ones the dispatcher relies on:

- **Storage is the source of truth.** The dispatcher is stateless: every state change is written to storage, and nothing in memory overrides it.
- **Fetching is atomic.** Two workers, on one node or on many, must never receive the same job. The built-in providers use `FOR UPDATE SKIP LOCKED` (PostgreSQL), `UPDLOCK, READPAST` (SQL Server), Lua scripts (Redis) and `FindOneAndUpdate` (MongoDB).
- **`CommitJobResultAsync` is atomic and idempotent.** It records the final result of an execution in one step, and committing the same result twice must not change the outcome.
- **Deduplication is atomic.** `EnqueueAsync` applies the [`DuplicatePolicy`](../concepts/idempotency.md) for an `IdempotencyKey` without a window where two jobs with the same key can both be created.
- **Orphans are recoverable.** A job in `Processing` whose heartbeat is older than `HeartbeatTimeout` can be found and requeued.

## `IJobStorage`

| Method | What it does |
|---|---|
| `EnqueueAsync` | Stores a new job and applies the duplicate policy. |
| `FetchNextAsync` | Takes the next available job from the given queues, by queue position, then priority, then creation time. See [fetch order](../concepts/queues.md#fetch-order-queue-first-then-priority). |
| `AcknowledgeAsync` | Marks a job as completed. |
| `SetFailedAsync` | Marks a job as failed, possibly scheduling a retry. |
| `SetExpiredAsync` | Marks a job as expired by its deadline. |
| `UpdateHeartbeatAsync` | Refreshes the heartbeat of a running job. |
| `CommitJobResultAsync` | Atomically commits the final result of an execution. |
| `RequeueOrphanedJobsAsync` | Requeues jobs whose heartbeat is stale. |
| `EnqueueContinuationsAsync` | Releases the children that wait for a parent. |
| `ReportProgressAsync` | Stores progress for a running job. |
| `SaveCheckpointAsync` | Stores a checkpoint and the progress that goes with it. |
| `PurgeJobsAsync` | Deletes terminal jobs according to the retention policy. |
| `RegisterServerAsync`, `HeartbeatServerAsync`, `DeregisterServerAsync`, `GetActiveServersAsync` | The registry of worker nodes. |

## `IRecurringStorage`

| Method | What it does |
|---|---|
| `UpsertRecurringJobAsync` | Creates or updates a definition. |
| `GetRecurringJobsAsync`, `GetRecurringJobAsync`, `GetRecurringJobByIdAsync` | Reads definitions (the last one includes deleted ones). |
| `GetDueRecurringJobsAsync` | Returns the schedules that are due. |
| `SetRecurringJobNextExecutionAsync`, `SetRecurringJobLastExecutionResultAsync` | Records the next and the last execution. |
| `UpdateRecurringJobConfigAsync` | Applies a change an operator made at runtime. |
| `DeleteRecurringJobAsync` | Removes a definition. |
| `ForceDeleteRecurringJobAsync`, `RestoreRecurringJobAsync` | Soft-deletes a definition so configuration cannot recreate it, and undoes that. |
| `TryAcquireRecurringJobLockAsync`, `ReleaseRecurringJobLockAsync` | The lock that keeps two nodes from firing the same schedule. |

## `IDashboardStorage`

| Method | What it does |
|---|---|
| `GetMetricsAsync`, `GetQueueMetricsAsync` | Global and per-queue counts and throughput. |
| `GetJobsAsync`, `GetJobByIdAsync`, `GetJobsByTagAsync` | Reads jobs, with filters and paging. |
| `GetJobCatalogAsync` | Aggregated job definitions and their execution metrics. |
| `RequeueJobAsync` | Puts a failed job back in `Enqueued` and resets its attempts. |
| `DeleteJobAsync` | Deletes a job and its logs. |
| `SaveExecutionLogsAsync` | Stores the logs captured during an execution. |

## Reporting crashed jobs to dead-letter

A provider can also implement `IOrphanedJobReporter`. Its one method, `RequeueOrphanedJobsAndReportAsync`, does what `RequeueOrphanedJobsAsync` does and returns the jobs it moved to `Failed`, so the dispatcher can run their dead-letter handlers with an `OrphanedJobException`. A job must be reported by the call that moved it and by no other call, so several nodes running the watcher never report it twice. A provider that does not implement it still fails those jobs, without calling any handler. See [Dead-letter handling](../concepts/dead-letter.md#when-the-node-dies).

## Testing it

The built-in providers run the same tests: `StorageProviderTestsBase` in the repository's `NexJob.IntegrationTests` project is the executable form of the rules above, and the reliability suite runs several nodes against real databases. These test projects are not distributed as packages, so a provider outside the repository has to bring its own tests. Start by copying the scenarios of that base class.
