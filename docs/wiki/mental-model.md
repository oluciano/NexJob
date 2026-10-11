---
title: "NexJob Mental Model: Storage, State Machine, and Recovery"
sidebarTitle: "Mental Model"
description: "Understand NexJob's storage-first design, job state machine, wake-up channel, and crash recovery before writing your first production job."
---

Before you write your first production job, spend five minutes here. NexJob's behavior is straightforward once you understand its storage-first design, but if you skip this page you will likely hit confusing bugs: jobs that expire unexpectedly, duplicate executions after a crash, or latency differences between single-node and multi-node deployments. This page explains the "why" behind all of those behaviors.

## Storage is the source of truth

Everything in NexJob persists to storage. Nothing lives in memory between dispatch cycles.

- Jobs are stored as `JobRecord` rows, documents, or keys — depending on your storage provider.
- The dispatcher reads from storage, writes to storage, and never caches state between cycles.
- If a worker crashes mid-execution, the job record is still in storage and will be requeued automatically.
- The dashboard reads from the same storage — what you see in the UI is exactly what exists.

**Practical implication:** You can stop and restart your worker service at any time without losing jobs. Jobs survive process restarts because storage survives restarts.

### The dispatcher is stateless

The dispatcher has no memory of what it processed in a previous cycle. Each cycle follows the same steps:

1. Fetch available jobs from storage (up to the number of free worker slots)
2. Execute each job
3. Write the result back to storage atomically
4. Repeat

There is no in-memory queue, no cached running-job list, and no local tracking across cycles. The only exception is the **wake-up channel** (described below), which is a transient signal — not persistent state.

**Practical implication:** You can run multiple dispatcher instances pointing at the same storage. Each instance independently fetches jobs using atomic fetch-and-update operations, so two workers never execute the same job.

## Job state machine

Every job moves through a well-defined set of states. All transitions are persisted to storage immediately.

```text
  EnqueueAsync ─────────────────────► Enqueued ◄──────────── Scheduled ◄── ScheduleAsync / ScheduleAtAsync
                                          │  ▲                    ▲
                    ContinueWithAsync     │  │ orphaned or        │ failure with attempts left
                           │              │  │ interrupted by     │ (RetryAt = next attempt)
                           ▼              ▼  │ shutdown           │
                 AwaitingContinuation   Processing ───────────────┘
                           │              │
                           │ parent       ├──► Succeeded
                           │ succeeded    │
                           └──► Enqueued  └──► Failed   (attempts exhausted → dead-letter handler runs)

  Enqueued / Scheduled ──► Expired   (deadline passed before execution began)
```

### State definitions

| State | Meaning |
|---|---|
| `Enqueued` | Ready for immediate execution — the dispatcher will pick this up on its next cycle |
| `Scheduled` | Waiting for a future time: `ScheduledAt` for a job you scheduled, `RetryAt` for a job waiting for its next attempt |
| `Processing` | Currently running on a worker; tracked with a `HeartbeatAt` timestamp |
| `Succeeded` | Completed successfully — terminal |
| `Failed` | All retry attempts exhausted — terminal; dead-letter handler runs at this point |
| `Expired` | Deadline passed before execution began — terminal |
| `Deleted` | Explicitly removed — terminal |
| `AwaitingContinuation` | Waiting for a parent job to complete; transitions to `Enqueued` when the parent succeeds |

### Terminal states

`Succeeded`, `Failed`, `Expired`, and `Deleted` are terminal. A job in a terminal state will not execute again unless you explicitly re-enqueue it (subject to your configured `DuplicatePolicy`).

There is no separate "retried" or "dead-letter" status in the state machine. A retry is simply the job back in `Scheduled`, with `RetryAt` set to the next attempt. A job whose attempts are exhausted moves directly to `Failed`.

## Wake-up channel vs polling

The dispatcher uses two mechanisms to discover new jobs. Understanding which one applies to your deployment explains why latency differs across configurations.

### Wake-up channel — the fast path

When you call `EnqueueAsync` **from the same process** where the dispatcher is running, NexJob sends a signal through a bounded in-memory channel (capacity = 1). The dispatcher detects this signal and immediately fetches the newly enqueued job without waiting for the next polling interval.

- **Latency:** Near-zero in practice, because the dispatcher is signalled instead of waiting for the next poll. The test suite does not assert a number (see [How We Test](reference/how-we-test.md))
- **Scope:** Local process only
- **Behavior:** Multiple rapid signals collapse into one; the channel never blocks the caller

### Polling — the slow path

If no wake-up signal arrives within the configured `PollingInterval` (default: 15 seconds), the dispatcher polls storage for any available jobs. This path catches jobs that were enqueued by a different process or service.

- **Latency:** Up to `PollingInterval` (15 seconds by default)
- **Scope:** Works across all nodes and processes
- **Behavior:** Standard fetch against the storage backend

**Practical implication:** On a single-node deployment, jobs execute almost instantly because the wake-up channel fires. On multi-node deployments, a job enqueued by Node A will not wake up Node B's dispatcher — Node B will pick it up on its next polling cycle unless it also has a dispatcher watching the same storage.

## Deadline behavior

`deadlineAfter` stores an absolute `ExpiresAt` on the job. It is checked **before execution begins**, never during: a job that is fetched after `ExpiresAt` is marked `Expired` and does not run, even by a millisecond. A tight deadline on a busy queue expires jobs silently, so set it from a real business requirement, or omit it. See [Scheduling](concepts/scheduling.md#deadlines).

## Execution pipeline

When the dispatcher picks up a job, it runs through the following steps in order:

1. **Expiration check**: if `UtcNow > ExpiresAt`, mark the job `Expired` and stop. Nothing else runs.
2. **Preparation**: create a scoped DI container, migrate the payload if the job carries a `[SchemaVersion]` attribute and the stored version differs (see [Job payloads](concepts/job-payloads.md)), and resolve the job class with its dependencies.
3. **Throttle acquisition**: if the job carries `[Throttle]`, wait for a slot. If the resource stays saturated for a few seconds, the job goes back to the queue about a second later and the attempt is **refunded**, so it never counts toward `MaxAttempts`.
4. **Filters and execution**: pass through the registered [`IJobExecutionFilter`](guides/job-filters.md) chain, then call `ExecuteAsync` with a `CancellationToken` wired to the shutdown signal (and to `[ExecutionTimeout]`, if set).
5. **Success path**: persist the `Succeeded` result atomically, then enqueue any registered continuations.
6. **Failure path**: record the attempt, evaluate the retry policy, and either reschedule the job (back to `Scheduled` with a future `RetryAt`) or mark it `Failed` and invoke the dead-letter handler.

Steps 5 and 6 are committed atomically. You will never see a job stuck in `Processing` after a clean shutdown.

## Crash recovery

A worker crash leaves jobs in `Processing` with a stale `HeartbeatAt`. NexJob finds them by that heartbeat (`HeartbeatTimeout`, 5 minutes by default) and requeues them. The attempt that was running **is spent**, so a job that keeps killing its node eventually runs out of attempts and goes to dead-letter. [Delivery Guarantees](concepts/delivery-guarantees.md) has the full table of what each failure costs a job.

**Graceful shutdown is not a crash.** The dispatcher stops fetching, waits up to `ShutdownTimeout` for running jobs to finish, and requeues the ones the shutdown token cancelled, **without consuming an attempt**.

!!! warning
    NexJob provides **at-least-once delivery**. A job can execute more than once if the worker crashes after `ExecuteAsync` completes but before the result is persisted to storage. Design your job logic to be idempotent — the same input should always produce the same observable outcome with no harmful side effects on repetition. See the [Idempotency](concepts/idempotency.md) page for patterns and `DuplicatePolicy` options.


## Broker queues vs NexJob queues

A NexJob queue is a partition in your storage database that governs execution, not a message-broker queue. Broker triggers consume from the broker and enqueue into a NexJob queue. See [Queues](concepts/queues.md#broker-queues-vs-nexjob-queues).

## Next steps

<div class="grid cards" markdown>
  -   [**Scheduling**](concepts/scheduling.md)

    Full `IScheduler` reference: enqueue, schedule with delay, set deadlines, and create job chains.

  -   [**Retries & Dead Letter**](concepts/retries-and-dead-letter.md)

    Configure exponential backoff per job and handle exhausted retries with dead-letter handlers.

  -   [**Idempotency**](concepts/idempotency.md)

    Use idempotency keys and `DuplicatePolicy` to make at-least-once delivery safe.

  -   [**Quick Start**](quickstart.md)

    Ready to write code? Run your first job in under 5 minutes.

</div>
