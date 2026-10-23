---
title: "Delivery Guarantees: Failure Modes and Job Recovery"
sidebarTitle: "Delivery Guarantees"
description: "What NexJob guarantees when jobs fail: attempts, state, dead-letter handling and where to see it. Covers crashes, shutdown, throttling, pauses and deadlines."
---

NexJob keeps all state in storage and is built around one promise: **a job that was accepted is run at least once, or ends in a state you can see**. This page lists what each event does to a job, so you can predict the outcome instead of discovering it in production.

## The guarantees

- **At-least-once.** After a crash, or when a result cannot be saved, a job can run again. It can also have run _partly_ before the crash. Design jobs to be [idempotent](idempotency.md).
- **Accepted means stored.** A job is accepted when `EnqueueAsync` returns: it is one storage write, and no overload joins your database transaction. So rolling back your transaction does not remove the job, a crash between your commit and the enqueue loses it, and enqueuing before the commit lets the job run against uncommitted data. The existing workaround (mark the row and let a recurring job enqueue for unmarked rows) is in the notes of [common scenarios](../guides/common-scenarios.md).
- **Storage is the source of truth.** Every state change is stored. A dispatcher keeps nothing that would be lost if the process died.
- **A job is always visible.** It is waiting, running, or ended as `Succeeded`, `Failed` (dead-letter) or `Expired`, in the [dashboard](../integrations/dashboard.md#dashboard-pages). Nothing is dropped silently; jobs only disappear through [retention](../guides/best-practices.md#control-storage-growth-with-retention-policies) or `DeleteJobAsync`, which you control.
- **An expired job never runs.** The [deadline](scheduling.md#deadlines) is checked before execution starts.
- **Your failure code cannot stop the system.** An exception in a dead-letter handler or forwarder is logged and swallowed.

NexJob does **not** promise exactly-once execution, and it does not promise ordering across workers or nodes.

## What each event does to a job

The columns are the same in all three tables: what happens to the job, whether the event uses one of its attempts, and whether the dead-letter handler and forwarders are called.

### When the job fails or its node dies

| Event | The job | Attempt | Dead-letter handler and forwarders |
| --- | --- | --- | --- |
| The job throws and has attempts left | Rescheduled at `RetryAt`, per the [retry policy](retries.md) | Used | No |
| The job throws on its last attempt | `Failed` | Used | **Called** with the exception |
| The job throws an exception listed in [`IgnoreRetryAttemptExceptions`](retries.md#failures-that-should-not-be-retried) (v5.9.0) | `Failed` at once, even with attempts left | Used | **Called** with the exception |
| It runs longer than its [`[ExecutionTimeout]`](retries.md#execution-timeout) (v5.9.0) | Its token is cancelled and the run fails with a `TimeoutException`; then rescheduled like any failure. A job that ignores its token keeps its worker slot | Used | **Called** on the last attempt |
| The node running it dies (crash, kill, lost machine) | Stays `Processing` until its heartbeat is older than `HeartbeatTimeout` (default 5 minutes; the check runs every `HeartbeatTimeout`, so recovery takes between 5 and about 10 minutes), then goes back to `Enqueued` | **Used**: the attempt that was running may have partly run | No |
| The node dies on the job's last attempt | `Failed`, by a node that can load the job type; until one does it stays `Processing` (v6.3.0) | Used | **Called** with an `OrphanedJobException` (v5.8.0) |
| The job succeeds but its result cannot be saved | The save is retried a few times. If it still fails the job stays `Processing` and is treated like a crashed node: run again by the orphan watcher, or `Failed` (and dead-lettered) if it had no attempts left | Used | Only if no attempts were left |
| The job fails (or is deferred) and that outcome cannot be saved | The save is retried like a result, three times. If it still fails the job stays `Processing` and is treated like a crashed node: the orphan watcher requeues it, or closes it as `Failed` if it had no attempts left. The executor does not call the dead-letter handler in that case | Used | Not called by the executor; the orphan watcher calls it with an `OrphanedJobException` when no attempts were left (v6.7.0) |
| The storage fails when the job is marked expired | The exception leaves the executor, the job stays `Processing` and the orphan watcher gives it back after `HeartbeatTimeout`; the next fetch marks it `Expired`. It never runs. Tested on InMemory, PostgreSQL, SQL Server, Redis and MongoDB | n/a | n/a |
| The storage fails when the dispatcher fetches jobs | The error is logged and the dispatcher polls again after `PollingInterval`. A failed fetch claims nothing, so no job is lost or run twice. Tested with 1, 3 and 8 consecutive failures on the five providers | n/a | n/a |
| A dead-letter handler or forwarder throws | The job stays `Failed`; the error is logged | n/a | Swallowed, other forwarders still run |

The failed save, whether it records a success or a failure, is retried three times, about 2.6 seconds in all (four calls). Run on PostgreSQL, SQL Server, Redis and MongoDB with the save failing on purpose: up to three failures the job ran once and ended `Succeeded`; with four, the job stayed `Processing`, was run a second time by the orphan watcher and then ended `Succeeded`. Keep `HeartbeatTimeout` above those 2.6 seconds, or the orphan watcher can give the job back while the executor is still retrying. This is the at-least-once guarantee at work: a job that succeeded can run twice, so make it [idempotent](idempotency.md).

On PostgreSQL, SQL Server and Redis the save is one transaction (or one script), so it lands whole or not at all. MongoDB writes the job, its continuations and the recurring job's last status as separate writes; if the connection drops between them, the retried save finds the job already finished and, if it carries the same outcome, completes the remaining writes, so the continuations are released and the recurring status updated. A save that is never retried successfully is the case above: the job stays `Processing` and the orphan watcher decides. The recurring job's last status is only written when no newer run of that job exists, so a late retry never puts an older run's status back. A job removed by `PurgeOnSuccess` leaves nothing to compare, so that one case is not guarded.

### When the job waits or is skipped

| Event | The job | Attempt | Dead-letter handler and forwarders |
| --- | --- | --- | --- |
| The host shuts down cleanly while it runs | Put back in the queue before `StopAsync` returns (up to 5 s after the drain). A job that ignores its cancellation token stays `Processing` and is recovered by the orphan watcher instead | **Not used** when it is put back; used if the watcher recovers it | No |
| The job type is not available in this process (another service's job) | Deferred by `ForeignJobRetryDelay` (default 5 s) so the owning service can take it | **Not used** | No |
| A `[Throttle]` slot is not free within about 5 seconds | Returned to the queue; it can run in a different order than it was enqueued | **Not used** | No |
| Its queue is paused | Waits in `Enqueued`. Pausing takes effect on each node's next polling cycle, and a job already fetched by a cycle in flight still runs | Not used | No |
| Its queue's circuit breaker is open | Waits in `Enqueued`; retries are not burned | Not used | No |
| Its [execution window](../guides/execution-windows.md) is closed | Waits in `Enqueued` until the window opens | Not used | No |
| Its deadline passes before it starts | `Expired`, never executed. The clock keeps running while the queue is paused, outside its window or its circuit is open | n/a | No |

### When you or the host step in

| Event | The job | Attempt | Dead-letter handler and forwarders |
| --- | --- | --- | --- |
| `DeleteJobAsync` while it runs | The job finishes; its result, heartbeat and progress are discarded and the job does not come back | n/a | No |
| `RequeueJobAsync` on a failed job | Back to `Enqueued` | Reset to 0 | No |
| The same idempotency key is enqueued again | Decided by [`DuplicatePolicy`](idempotency.md#duplicatepolicy) | n/a | No |

!!! note
    A job that fails on its last attempt and a job whose node died on its last attempt both reach your `IDeadLetterHandler<TJob>` and every `IDeadLetterForwarder`. The second one receives an `OrphanedJobException`, because the job never reported an error of its own. See [When the node dies](dead-letter.md#when-the-node-dies).


## When storage is unavailable

Storage is the source of truth, so an outage stops progress. For the calls in the table, the component catches the storage error, logs it and tries again. Which log you see and what it costs the job depends on how long the outage lasts compared with `HeartbeatTimeout` (default 5 minutes).

| Component | What happens | Log level and text | The job's attempt |
| --- | --- | --- | --- |
| **Short outage (shorter than `HeartbeatTimeout`)** | | | |
| Dispatcher fetch and settings read | The error is caught, the dispatcher waits `PollingInterval` (default 15 s, no exponential backoff) and polls again. It resumes by itself when storage is back | `Error`, `Error fetching next jobs from storage` or `Error reading runtime settings from storage`, once per poll for the whole outage | None used: nothing was fetched |
| Job heartbeat (a running job) | The job keeps running. The update is tried again every `HeartbeatInterval` (default 30 s) | `Warning`, `Heartbeat update for job {JobId} failed; will retry on the next interval` | Not affected |
| Node heartbeat | The timer continues. Each beat has a 10 s timeout | `Warning`, `Failed to update global heartbeat for server {ServerId}.` | Not affected |
| Node registration (at host start) | If the registration fails, the host still runs and the jobs execute. The node retries the registration every `ServerHeartbeatInterval` (default 15 s) and appears in the dashboard once it succeeds. It sends no heartbeat before that | `Warning`, `Server node {ServerId} failed to register — running in degraded mode ... Retrying every {Interval}.`; `Information`, `registered after retrying` | Not affected |
| Recurring jobs from configuration (at host start) | A job the storage fails to store is registered again with exponential backoff (1 s, doubling up to 30 s) until it is stored or the host stops. A configuration error (invalid cron, unknown job, bad input) is not retried. The jobs not yet stored are not scheduled in the meantime (v6.7.0) | `Warning`, `Could not store recurring job '{Job}' from configuration; it will be retried.` and `The storage could not store {Count} recurring job(s) from configuration. Retrying in {Delay}s.` | Not affected |
| Saving a result | Retried three times, about 2.6 s, as described in [the table above](#when-the-job-fails-or-its-node-dies) | see above | Used |
| Orphan watcher, recurring scheduler, retention | The error is caught and the loop continues. The orphan watcher retries after the smaller of 5 s and `HeartbeatTimeout`; the recurring scheduler waits 5 s and the retention service continues its loop | `Error`, `Error in OrphanedJobWatcherService`, `Error in RecurringJobSchedulerService`, `Error in JobRetentionService.` | Not affected |
| Recurring job fired while storage fails between its two writes | The scheduler enqueues the job, then moves `NextExecution` forward. If the second write fails, the job exists and the same occurrence is due again at the next cycle. With `SkipIfRunning` the idempotency key `recurring:{id}` makes the storage reject the second one while the first is still `Enqueued` or `Processing`; with `AllowConcurrent`, or once the first has finished, a second job is created | `Error`, `Failed to enqueue recurring job '{Id}'` | None used: the first job has not run yet |
| **Long outage (longer than `HeartbeatTimeout`)** | | | |
| Job running during the outage | The job keeps running, but its heartbeat cannot be written, so it goes stale. When storage returns, the orphan watcher requeues any `Processing` job whose heartbeat is older than `HeartbeatTimeout`. It does not check whether the process that owns the job is alive, so a job that is still running can be run a second time | none for the requeue (a `Warning` only when the job had no attempts left, see [Where to see it](#where-to-see-it)) | Used: the requeued run counts as a new attempt |
| **Enqueue side** | | | |
| `IScheduler.EnqueueAsync` and `ScheduleAsync` | The storage exception reaches the caller unwrapped. There is no retry inside NexJob, and the dispatcher is not woken up | none from NexJob; your code sees the exception | No job exists |
| **Host start** | | | |
| Storage unreachable when the host starts | The start fails with the driver's exception and the host does not run: PostgreSQL in the schema initializer, SQL Server and MongoDB when the host starts, and Redis already in `AddNexJobRedis`, which connects when it is called. How long it takes is the driver's connection timeout. Measured with a 5 s timeout configured: PostgreSQL 0.3 s, SQL Server 4.9 s, MongoDB 5.5 s, Redis 10.2 s | the driver's exception from `Host.StartAsync` | No job involved |

Guidance derived from the requeue criterion: set `HeartbeatTimeout` above the longest outage you want to ride out, plus two `HeartbeatInterval`. A job that is requeued while still running is the at-least-once guarantee at work, so make it [idempotent](idempotency.md).

The caller owns the retry of an enqueue. When the failure leaves you unsure whether the job was stored (a timeout after the write), repeat the call with the same `idempotencyKey`: while the first job is still active the call returns its `JobId` instead of creating a second one. Once it has ended, [`DuplicatePolicy`](idempotency.md#duplicatepolicy) decides. `ScheduleAsync` and `ScheduleAtAsync` always use `AllowAfterFailed`.

Outage behavior beyond this table is not covered by tests: see [How NexJob is Tested](../reference/how-we-test.md#what-is-not-covered).

## Where to see it

| What you want to know | Look at |
| --- | --- |
| A job used all its attempts | Dashboard **Failed / DLQ**; your dead-letter handler or forwarder; see [Alerts](../guides/alerts.md) |
| A job was recovered after a crash | Dashboard Job Detail: `Attempts` higher than expected, and a `Warning` log from the orphan watcher when it was the last attempt |
| A job expired | Dashboard **Failed / DLQ**, _Expired_ tab; counter `nexjob.jobs.expired` |
| A job is waiting | Dashboard **Queues**: paused badge, `NO WORKERS` badge, circuit state |
| A job ignores its execution timeout | A warning log, `ignored cancellation ... still holds its worker slot`; counter `nexjob.jobs.cancellation_ignored` |
| A throttled job went back to the queue | An information log: `got no slot for throttled resource ... Returning it to the queue`; counter `nexjob.jobs.throttle_deferred` |

## What you should do about it

- Make jobs idempotent. A recovered job is a job that can run twice.
- Set `MaxAttempts` with the crash cost in mind: a job that keeps killing its node (for example an input that exhausts memory) spends one attempt per crash and then ends `Failed` instead of looping forever.
- Alert on the `Failed` state, not on retries: [Alerts](../guides/alerts.md).
- Give deadlines a margin for the time a queue can legitimately wait.

## See also

- [Retries and dead letter](retries-and-dead-letter.md): the retry policy, the handler and forwarding.
- [Retries](retries.md)
- [Dead-letter handling](dead-letter.md)
- [Mental Model](../mental-model.md#crash-recovery)
- [Troubleshooting](../reference/troubleshooting.md)
- [How NexJob is Tested](../reference/how-we-test.md): which of these rows are checked against real databases.
