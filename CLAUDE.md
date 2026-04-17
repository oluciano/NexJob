# NexJob — Project Context for Claude Code

This file is automatically loaded by Claude Code.
It defines architecture, constraints, and behavioral guarantees.

---

## Project Status

NexJob is a production-oriented background job processing library.
Current published version: **v3.0.0**
Active development: **v4.0.0**

### Implemented (v3.0.0)
- `IJob` / `IJob<T>` — simple and structured jobs
- Wake-up channel — near-zero latency local dispatch
- `deadlineAfter` — deadline enforcement before execution
- `IDeadLetterHandler<TJob>` — permanent failure fallback
- Retry policies — global + per-job `[Retry]` attribute
- `[Throttle]` — resource-based concurrency limits + distributed via Redis
- `IJobContext` — injectable runtime context
- Recurring jobs — via code + via `appsettings.json`
- Schema migrations — auto-applied at startup
- Graceful shutdown
- Dashboard — light/dark UI, timeline, live updates, standalone mode
- OpenTelemetry — `NexJobActivitySource` + `NexJobMetrics`
- 5 storage providers: InMemory, PostgreSQL, SQL Server, Redis, MongoDB
- `DuplicatePolicy` — atomic deduplication across all providers
- `CommitJobResultAsync` — idempotent result commit
- `IJobExecutionFilter` — middleware pipeline
- Job retention + auto-cleanup
- `IStorageProvider` split: `IJobStorage`, `IRecurringStorage`, `IDashboardStorage`
- `JobExecutor` — extracted from `JobDispatcherService`
- `IJobInvokerFactory`, `IJobRetryPolicy`, `IDeadLetterDispatcher`, `IJobControlService`
- `NexJobBuilder` — fluent builder returned by `AddNexJob()`
- `UseDashboardReadReplica()` — opt-in read replica (PostgreSQL, SQL Server)
- `UseDistributedThrottle()` — opt-in global Redis throttle enforcement
- Triggers: AzureServiceBus, AwsSqs, RabbitMQ, Kafka, GooglePubSub

---

## Squad Lanes

**Bruxo (Claude Code — Sonnet)** owns:
- All core (`src/NexJob`) changes — only agent allowed to touch core
- High-risk features: storage contracts, dispatcher logic, execution pipeline
- Complex multi-file refactors
- Any task where a mistake causes data loss or behavioral regression

**Gemini** owns:
- Trigger package implementation (medium complexity)
- Dashboard features and redesigns
- Backend tasks and refactors outside core
- Documentation, wiki, CHANGELOG

**Hard rule:** Trigger packages are external consumers of core.
They call `IScheduler.EnqueueAsync` and `JobWakeUpChannel.Signal()`.
They never modify `IStorageProvider`, `JobRecord`, or any core internal.

---

## Core Principles

1. Simplicity first
2. Advanced scenarios supported
3. Predictability over magic
4. Developer experience matters
5. Reliability by design

---

## Architecture — Current State (v3)

### Storage interfaces (segregated)
```
IJobStorage       → hot-path execution (FetchNext, CommitResult, SetExpired, heartbeat)
IRecurringStorage → recurring job scheduling
IDashboardStorage → read-heavy dashboard queries
IStorageProvider  → IJobStorage + IRecurringStorage + IDashboardStorage (composed)
```

### Internal execution pipeline
```
JobDispatcherService  → polling loop + worker slots (~180 lines)
JobExecutor           → single job execution pipeline (~260 lines)
  IJobInvokerFactory  → type resolution + scope creation
  IJobRetryPolicy     → retry delay calculation
  IDeadLetterDispatcher → handler resolution and invocation
  IJobFilterPipeline  → middleware pipeline
```

### Trigger architecture
```
[Broker message]
      ↓
NexJob.Trigger.{Broker}   ← external package, depends only on NexJob core
      ↓
JobRecordFactory.Build()  ← shared factory
      ↓
IScheduler.EnqueueAsync() ← existing contract, unchanged
      ↓
JobWakeUpChannel.Signal() ← existing mechanism, unchanged
```

---

## Non-Negotiable Invariants

- Storage is the single source of truth — no in-memory state overrides it
- Dispatcher is stateless — all state transitions must be persisted
- Deadline must be enforced before execution begins — expired jobs never execute
- Dead-letter handlers must never crash the dispatcher (exceptions swallowed, log only)
- Wake-up signaling must never block (Channel bounded capacity=1, DropWrite)
- Zero warnings in Release builds (TreatWarningsAsErrors=true)

---

## Test Integrity (Universal — All Squad Members)

### 3N Mandatory Matrix
Every feature or bug fix must produce minimum 3 tests:
- **N1 — Positive:** happy path works as expected
- **N2 — Negative:** failure path fails as expected
- **N3 — Invalid Input:** null, empty, boundary — handled gracefully

### Existing Tests Are Immutable Contracts
NEVER rewrite, rename, or delete a passing test to make new code pass.
When a test breaks after a change: fix the production code, not the test.
Only valid reason to change a test: behavior was explicitly changed by the architect.
If changed: add comment `// Behavior changed in vX.Y: <reason>`.

800 tests that can be rewritten on demand are worth less than 10 that cannot.
