# NexJob — Project Context for Claude Code

This file is automatically loaded by Claude Code.
It defines architecture, constraints, and behavioral guarantees.

---

## Project Status

NexJob is a production-oriented background job processing library for .NET 8+.
Active development branch: **develop**.
The current version is defined dynamically in `Directory.Build.props` (`<VersionPrefix>`) and official NuGet/git tags.

### Current Architecture & Capabilities
- `IJob` / `IJob<T>` — simple and structured jobs
- Wake-up channel — near-zero latency local dispatch via internal channel
- `deadlineAfter` — deadline enforcement before execution
- `IDeadLetterHandler<TJob>` & `IDeadLetterForwarder` — permanent failure fallback
- Retry policies — global + per-job `[Retry]` attribute + `IJobRetryPolicy`
- `[Throttle]` — resource-based concurrency limits + distributed via Redis
- `IJobContext` — injectable runtime context
- Recurring jobs — via code + via `appsettings.json`
- Schema migrations — auto-applied at startup across persistent providers
- Graceful shutdown
- Dashboard — light/dark UI, timeline, live updates, standalone HTTP server mode
- OpenTelemetry — `NexJobActivitySource` + `NexJobMetrics`
- 5 storage providers: InMemory, PostgreSQL (SKIP LOCKED), SQL Server (UPDLOCK, READPAST), Redis, MongoDB
- `DuplicatePolicy` — atomic deduplication across all providers
- `CommitJobResultAsync` — idempotent atomic result commit
- `IJobExecutionFilter` — middleware pipeline
- Job retention + auto-cleanup
- `IStorageProvider` split: `IJobStorage`, `IRecurringStorage`, `IDashboardStorage`
- `JobExecutor` — extracted execution pipeline orchestrator
- `IJobInvokerFactory`, `IJobRetryPolicy`, `IDeadLetterDispatcher`, `IJobControlService`
- `NexJobBuilder` — fluent builder returned by `AddNexJob()`
- `UseDashboardReadReplica()` — opt-in read replica (PostgreSQL, SQL Server)
- `AddNexJobDistributedThrottle()` (NexJob.Redis) — opt-in global Redis throttle enforcement
- Triggers & Outbox: AzureServiceBus, AwsSqs, RabbitMQ, Kafka, GooglePubSub, Salesforce (gRPC), SalesforceStreaming (CometD)

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
They call `IScheduler.EnqueueAsync`. They NEVER reference or call `JobWakeUpChannel` directly (the scheduler handles signaling internally).
They never modify `IStorageProvider`, `JobRecord`, or any core internal.

---

## Core Principles

1. Simplicity first
2. Advanced scenarios supported
3. Predictability over magic
4. Developer experience matters
5. Reliability by design

---

## Non-Goals (decided by the owner)

Check this list at the start of grooming, before designing anything.

- **No notification channels.** NexJob does not ship Slack, Teams, Discord, e-mail or PagerDuty integrations (issue #205). It exposes the hooks (`IDeadLetterHandler<TJob>`, `IDeadLetterForwarder`, the OpenTelemetry metrics) and documents a recipe in `docs/wiki/guides/alerts.md`.
- **No dashboard controls over shared resources.** The dashboard does not change the worker count or connection pools at runtime; those are deployment decisions and the database is shared.
- **No new satellite package without the owner's decision.** A package is a long-term commitment (formats, secrets, versions). Grooming starts by asking whether it should exist, and whether a documented recipe on an existing extension point is enough.

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
IScheduler.EnqueueAsync() ← persists job and signals wake-up channel internally
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

---

## Token & Context Governance (Universal Economy Rules)

- **Zero Preamble & Direct Responses:** Never repeat user prompts or provide conversational filler. Go straight to the diff, error, or solution.
- **Surgical File Reading:** Grep/locate symbol line ranges first; read with targeted line slicing (`StartLine`/`EndLine`). Never load 500+ lines into context unnecessarily.
- **Never Re-read:** Trust existing context; never re-read files that were not modified.
- **Surgical Logs & Truncation:** When running `dotnet test` or `dotnet build`, truncate output to the failure message and stack trace. Never flood the context with hundreds of lines of passing logs.
- **Anti-Loop Safety:** If a command or test fails twice with the identical error, stop immediately and diagnose root cause rather than blindly retrying.
