# Mental Model

**Read this before anything else.** This page explains how NexJob works at a conceptual level. Understanding this model will save you hours of debugging.

---

## Storage Is the Source of Truth

Everything persists to storage. Nothing lives in memory between dispatch cycles.

- Jobs are stored as `JobRecord` rows/documents/keys
- The dispatcher reads from storage, writes to storage, and never caches state
- If a worker crashes, the job is still in storage and will be requeued by the orphan watcher
- The dashboard reads from the same storage — what you see is what actually exists

**Implication:** You can stop and restart your worker service at any time without losing jobs. Storage survives restarts.

---

## The Dispatcher Is Stateless

The dispatcher has no memory of what it processed. Each polling cycle:

1. Fetch the available jobs from storage (as many as there are free worker slots)
2. Execute it
3. Write the result back to storage
4. Repeat

There is no in-memory queue, no cached state, no local tracking of running jobs. The only exception is the **wake-up channel** (see below), which is a transient signaling mechanism.

**Implication:** You can scale to multiple worker instances. Each dispatcher instance independently fetches and processes jobs. Storage coordinates everything through atomic fetch-and-update operations.

---

## Job State Machine

Every job moves through these states. All transitions are persisted.

```
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

There is no separate "retried" or "dead-letter" status: a retry is a job back in `Scheduled` with a `RetryAt`, and a job whose attempts are exhausted is `Failed` (its `IDeadLetterHandler<T>`, if registered, runs at that moment).

### State Definitions

| State | Meaning |
|---|---|
| `Enqueued` | Ready for immediate execution |
| `Scheduled` | Will execute at a future time (`ScheduledAt`) |
| `Processing` | Currently running on a worker |
| `Succeeded` | Completed successfully (terminal) |
| `Failed` | All retries exhausted (terminal) |
| `Expired` | Deadline passed before execution began (terminal) |
| `Deleted` | Explicitly removed (terminal) |
| `AwaitingContinuation` | Waiting for parent job to complete; becomes `Enqueued` when the parent succeeds |

### Terminal States

`Succeeded`, `Failed`, `Expired`, and `Deleted` are terminal. A job in a terminal state will not be executed again unless explicitly re-enqueued (governed by [idempotency policy](17-Idempotency.md)).

---

## Deadline Behavior

`deadlineAfter` is set at enqueue time and stored as `ExpiresAt`.

**Critical:** The deadline is checked **before execution begins**, not during. If a job's deadline has passed when the dispatcher fetches it, the job is marked as `Expired` and **never executes**.

```csharp
// This job will be marked as Expired if not picked up within 5 minutes
await scheduler.EnqueueAsync<SendEmailJob>(
    deadlineAfter: TimeSpan.FromMinutes(5));
```

**Why this matters:** A job with a tight deadline on a busy queue will expire silently. Set `deadlineAfter` based on your actual business requirement — not as a "nice to have" timeout.

---

## Wake-Up Channel vs Polling

The dispatcher uses two mechanisms to find jobs:

### Wake-Up Channel (Fast Path)

When you call `EnqueueAsync` on the **same process** where the dispatcher is running, a signal is sent through a bounded channel (capacity=1). The dispatcher detects this signal and immediately fetches the new job.

- **Latency:** Near-zero (< 1ms)
- **Scope:** Local process only
- **Behavior:** Multiple signals collapse into one (non-blocking)

### Polling (Slow Path)

If no wake-up signal arrives within the configured `PollingInterval` (default: 15 seconds), the dispatcher polls storage for any available jobs.

- **Latency:** Up to `PollingInterval`
- **Scope:** Works across all nodes
- **Behavior:** Standard database poll

**Implication:** On a single-node deployment, jobs execute almost instantly. On multi-node deployments, jobs enqueued from a different node will experience polling latency unless that node also has a dispatcher listening.

---

## How Jobs Flow Through the System

```
┌─────────────────────────────────────────────────────┐
│  Your Code                                          │
│  scheduler.EnqueueAsync<TJob>(input)                │
└────────────────────┬────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────┐
│  IScheduler                                         │
│  1. Creates JobRecord                               │
│  2. Checks idempotency (if key provided)            │
│  3. Persists to storage                             │
│  4. Signals wake-up channel (local only)            │
└────────────────────┬────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────┐
│  Storage Provider                                   │
│  PostgreSQL / SQL Server / Redis / MongoDB / Memory │
└────────────────────┬────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────┐
│  JobDispatcherService (BackgroundService)           │
│  1. Wake-up signal or poll → fetch next job         │
│  2. Check expiration (ExpiresAt)                    │
│  3. Deserialize input, resolve DI scope             │
│  4. Apply throttle semaphores                       │
│  5. Execute job                                     │
│  6. Commit result atomically                        │
│  7. If failed: retry or dead-letter                 │
└─────────────────────────────────────────────────────┘
```

---

## Execution Pipeline

When the dispatcher picks up a job:

1. **Expiration check** — If `UtcNow > ExpiresAt`, mark as `Expired` and stop
2. **Schema migration** — If `[SchemaVersion]` differs from stored version, migrate payload
3. **DI resolution** — Create a scope, resolve job instance and dependencies
4. **Throttle acquisition** — Wait for `[Throttle]` semaphore if configured
5. **Execution** — Call `ExecuteAsync` with cancellation token
6. **Success path** — `CommitJobResultAsync` with `Succeeded`, enqueue continuations
7. **Failure path** — Record attempt → evaluate retry policy → reschedule or dead-letter

All state transitions are persisted atomically in step 6-7 via `CommitJobResultAsync`.

---

## What Happens on Worker Crash?

1. Worker was processing job → status is `Processing` with a `HeartbeatAt` timestamp
2. `OrphanedJobWatcherService` scans for jobs where `UtcNow - HeartbeatAt > HeartbeatTimeout` (default: 5 minutes)
3. Orphaned jobs are re-enqueued automatically; if the job had already used all its attempts it is marked `Failed` instead of being requeued forever
4. A fresh dispatcher picks them up

A graceful shutdown is not a crash: the dispatcher stops fetching, waits up to `ShutdownTimeout` for running jobs, and a job cancelled by the shutdown is requeued right away **without consuming an attempt** (see [Best Practices](13-Best-Practices.md#graceful-shutdown)).

**Implication:** Jobs are at-least-once delivered. If your job is not idempotent, see [Idempotency](17-Idempotency.md).

---

## Next Steps

- [Getting Started](01-Getting-Started.md) — Run your first job
- [Scheduling](03-Scheduling.md) — Enqueue, schedule, set deadlines
- [Retry & Dead Letter](06-Retry-And-Dead-Letter.md) — Handle failures gracefully
