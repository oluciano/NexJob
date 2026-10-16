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
- **Deduplication is atomic.** `EnqueueAsync` applies the [`DuplicatePolicy`](../concepts/idempotency.md) for an `IdempotencyKey` without a window where two jobs with the same key can both be created. It returns an `EnqueueResult(JobId, WasRejected)`: when an active job already holds the key it returns that job's id with `WasRejected = false`; when the policy rejects the new job it returns the existing id with `WasRejected = true`, and the scheduler turns that into a `DuplicateJobException`.
- **Orphans are recoverable.** A job in `Processing` whose heartbeat is older than `HeartbeatTimeout` can be found and requeued.

Every method is abstract except the four that have a default, marked below: `FetchBatchAsync`, `AcknowledgeBatchAsync` and `SaveCheckpointAsync` on `IJobStorage`, and `GetJobCatalogAsync` on `IDashboardStorage`. A provider that implements only the abstract ones runs, but without batch fetching, batch acknowledgment, checkpoints or the job catalog.

## `IJobStorage`

| Method | What it does |
|---|---|
| `EnqueueAsync` | Stores a new job and applies the duplicate policy. |
| `FetchNextAsync` | Takes the next available job from the given queues (the list is in order of preference), by queue position, then priority, then creation time, and returns `null` when there is none. See [fetch order](../concepts/queues.md#fetch-order-queue-first-then-priority). |
| `FetchBatchAsync` | Takes up to `maxBatchSize` jobs. **Has a default**: it calls `FetchNextAsync` repeatedly. The dispatcher calls this one, so override it to claim a batch in fewer round trips. |
| `AcknowledgeAsync` | Marks a job as completed. |
| `AcknowledgeBatchAsync` | Marks several jobs as completed. **Has a default**: it calls `AcknowledgeAsync` for each one. Used when `EnableBatchAcknowledgment` is on; override it to do the batch in one statement. |
| `SetFailedAsync` | Marks a job as failed, possibly scheduling a retry. |
| `SetExpiredAsync` | Marks a job as expired by its deadline. |
| `UpdateHeartbeatAsync` | Refreshes the heartbeat of a running job. |
| `CommitJobResultAsync` | Atomically commits the final result of an execution. |
| `RequeueOrphanedJobsAsync` | Requeues jobs whose heartbeat is stale. |
| `EnqueueContinuationsAsync` | Releases the children that wait for a parent. |
| `ReportProgressAsync` | Stores progress for a running job. |
| `SaveCheckpointAsync` | Stores a checkpoint and the progress that goes with it. **The default does not store the checkpoint**: it only forwards the percentage to `ReportProgressAsync`. A provider that does not override it loses every checkpoint, and `GetCheckpoint` returns `null` after a retry. |
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
| `ForceDeleteRecurringJobAsync`, `RestoreRecurringJobAsync` | Soft-deletes a definition so configuration cannot recreate it, and deletes the jobs it created; restore undoes the flag, not the deleted jobs. |
| `TryAcquireRecurringJobLockAsync`, `ReleaseRecurringJobLockAsync` | The lock that keeps two nodes from firing the same schedule. |

## `IDashboardStorage`

| Method | What it does |
|---|---|
| `GetMetricsAsync`, `GetQueueMetricsAsync` | Global and per-queue counts and throughput. |
| `GetJobsAsync`, `GetJobByIdAsync`, `GetJobsByTagAsync` | Reads jobs, with filters and paging. |
| `GetJobCatalogAsync` | Aggregated job definitions and their execution metrics. **The default returns an empty list**, so the dashboard's job catalog stays empty until you implement it. |
| `RequeueJobAsync` | Puts a `Failed`, `Expired` or `Scheduled` job back in `Enqueued` and resets its attempts. Any other status must be left untouched, so a running or succeeded job is never run again. |
| `DeleteJobAsync` | Deletes a job and its logs. |
| `SaveExecutionLogsAsync` | Stores the logs captured during an execution. |

## Reporting crashed jobs to dead-letter

A provider can also implement `IOrphanedJobReporter`. Its one method, `RequeueOrphanedJobsAndReportAsync`, does what `RequeueOrphanedJobsAsync` does and returns the jobs it moved to `Failed`, so the dispatcher can run their dead-letter handlers with an `OrphanedJobException`. A job must be reported by the call that moved it and by no other call, so several nodes running the watcher never report it twice. The interface has a second overload that also takes a `Func<string, bool>` telling whether this node can load a job type; a provider overrides it so that a job with no attempts left is moved to `Failed` only when the predicate accepts its type, and a job with attempts left is requeued whatever its type. Its default implementation ignores the predicate, so a provider that does not override it keeps today's behaviour, and in a database shared by several services a node of another service can fail the job. A provider that does not implement the interface still fails those jobs, without calling any handler. See [Dead-letter handling](../concepts/dead-letter.md#when-the-node-dies).

## Testing it

The built-in providers run the same tests: `StorageProviderTestsBase` in the repository's `NexJob.IntegrationTests` project is the executable form of the rules above, and the reliability suite runs several nodes against real databases. These test projects are not distributed as packages, so a provider outside the repository has to bring its own tests. Start by copying the scenarios of that base class.
