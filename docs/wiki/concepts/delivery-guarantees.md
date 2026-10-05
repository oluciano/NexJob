---
title: "Delivery Guarantees: Failure Modes and Job Recovery"
sidebarTitle: "Delivery Guarantees"
description: "What NexJob guarantees when jobs fail: attempts, state, dead-letter handling and where to see it. Covers crashes, shutdown, throttling, pauses and deadlines."
---

NexJob keeps all state in storage and is built around one promise: **a job that was accepted is run at least once, or ends in a state you can see**. This page lists what each event does to a job, so you can predict the outcome instead of discovering it in production.

## The guarantees

- **At-least-once.** After a crash, or when a result cannot be saved, a job can run again. It can also have run _partly_ before the crash. Design jobs to be [idempotent](../concepts/idempotency.md).
- **Storage is the source of truth.** Every state change is stored. A dispatcher keeps nothing that would be lost if the process died.
- **A job is always visible.** It is waiting, running, or ended as `Succeeded`, `Failed` (dead-letter) or `Expired`, in the [dashboard](../integrations/dashboard.md#dashboard-pages). Nothing is dropped silently; jobs only disappear through [retention](../guides/best-practices.md#control-storage-growth-with-retention-policies) or `DeleteJobAsync`, which you control.
- **An expired job never runs.** The [deadline](../concepts/scheduling.md#deadlines) is checked before execution starts.
- **Your failure code cannot stop the system.** An exception in a dead-letter handler or forwarder is logged and swallowed.

NexJob does **not** promise exactly-once execution, and it does not promise ordering across workers or nodes.

## What each event does to a job

| Event | The job | Attempt | Dead-letter handler and forwarders |
| --- | --- | --- | --- |
| The job throws and has attempts left | Rescheduled at `RetryAt`, per the [retry policy](../concepts/retries-and-dead-letter.md) | Used | No |
| The job throws on its last attempt | `Failed` | Used | **Called** with the exception |
| The job throws an exception listed in [`IgnoreRetryAttemptExceptions`](../concepts/retries-and-dead-letter.md#failures-that-should-not-be-retried) (v5.9.0) | `Failed` at once, even with attempts left | Used | **Called** with the exception |
| It runs longer than its [`[ExecutionTimeout]`](../concepts/retries-and-dead-letter.md#execution-timeout) (v5.9.0) | Its token is cancelled and the run fails with a `TimeoutException`; then rescheduled like any failure. A job that ignores its token keeps its worker slot | Used | **Called** on the last attempt |
| The node running it dies (crash, kill, lost machine) | Stays `Processing` until its heartbeat is older than `HeartbeatTimeout` (default 5 minutes; the check runs every `HeartbeatTimeout`, so recovery takes between 5 and about 10 minutes), then goes back to `Enqueued` | **Used**: the attempt that was running may have partly run | No |
| The node dies on the job's last attempt | `Failed` | Used | **Called** with an `OrphanedJobException` (v5.8.0) |
| The job succeeds but its result cannot be saved | The save is retried a few times. If it still fails the job stays `Processing` and is treated like a crashed node: run again by the orphan watcher, or `Failed` (and dead-lettered) if it had no attempts left | Used | Only if no attempts were left |
| The host shuts down cleanly while it runs | Put back in the queue | **Not used** | No |
| The job type is not available in this process (another service's job) | Deferred by `ForeignJobRetryDelay` (default 5 s) so the owning service can take it | **Not used** | No |
| A `[Throttle]` slot is not free within about 5 seconds | Returned to the queue; it can run in a different order than it was enqueued | **Not used** | No |
| Its queue is paused | Waits in `Enqueued`. Pausing takes effect on each node's next polling cycle, and a job already fetched by a cycle in flight still runs | Not used | No |
| Its queue's circuit breaker is open | Waits in `Enqueued`; retries are not burned | Not used | No |
| Its [execution window](../guides/execution-windows.md) is closed | Waits in `Enqueued` until the window opens | Not used | No |
| Its deadline passes before it starts | `Expired`, never executed. The clock keeps running while the queue is paused, outside its window or its circuit is open | n/a | No |
| `DeleteJobAsync` while it runs | The job finishes; its result, heartbeat and progress are discarded and the job does not come back | n/a | No |
| `RequeueJobAsync` on a failed job | Back to `Enqueued` | Reset to 0 | No |
| The same idempotency key is enqueued again | Decided by [`DuplicatePolicy`](../concepts/idempotency.md#duplicatepolicy) | n/a | No |
| A dead-letter handler or forwarder throws | The job stays `Failed`; the error is logged | n/a | Swallowed, other forwarders still run |

!!! note
    A job that fails on its last attempt and a job whose node died on its last attempt both reach your `IDeadLetterHandler<TJob>` and every `IDeadLetterForwarder`. The second one receives an `OrphanedJobException`, because the job never reported an error of its own. See [When the node dies](../concepts/retries-and-dead-letter.md#when-the-node-dies).


## Where to see it

| What you want to know | Look at |
| --- | --- |
| A job used all its attempts | Dashboard **Failed / DLQ**; your dead-letter handler or forwarder; see [Alerts](../guides/alerts.md) |
| A job was recovered after a crash | Dashboard Job Detail: `Attempts` higher than expected, and a `Warning` log from the orphan watcher when it was the last attempt |
| A job expired | Dashboard **Failed / DLQ**, _Expired_ tab; counter `nexjob.jobs.expired` |
| A job is waiting | Dashboard **Queues**: paused badge, `NO WORKERS` badge, circuit state |
| A throttled job went back to the queue | An information log: `got no slot for throttled resource ... Returning it to the queue`; counter `nexjob.jobs.throttle_deferred` |

## What you should do about it

- Make jobs idempotent. A recovered job is a job that can run twice.
- Set `MaxAttempts` with the crash cost in mind: a job that keeps killing its node (for example an input that exhausts memory) spends one attempt per crash and then ends `Failed` instead of looping forever.
- Alert on the `Failed` state, not on retries: [Alerts](../guides/alerts.md).
- Give deadlines a margin for the time a queue can legitimately wait.

## See also

- [Retries & Dead Letter](../concepts/retries-and-dead-letter.md)
- [Mental Model](../mental-model.md#crash-recovery)
- [Troubleshooting](../reference/troubleshooting.md)
- [How NexJob is Tested](../reference/how-we-test.md): which of these rows are checked against real databases.
