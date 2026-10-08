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

### Existing Tests Are Contracts, Not Files
NEVER weaken, invert, loosen, skip or delete an assertion to make new code pass.
When a test breaks after a change: fix the production code, not the test.
Only valid reason to change a test's expectation: behavior was explicitly changed by the architect.
If changed: add comment `// Behavior changed in vX.Y: <reason>`.

Allowed, as long as every scenario and assertion survives:
- Moving a test into its canonical file.
- Merging near-identical tests into a `[Theory]` / `[InlineData]`.
- Deleting a proven duplicate (same arrange, same assert, or a strict subset of another test). The PR must name the surviving test.

800 tests that can be rewritten on demand are worth less than 10 that cannot. Moving or merging is not rewriting.

### One Canonical Test File per Class
Production class `Foo` has one test file, `FooTests.cs`. Parallel or twin files (`FooHardeningTests.cs`, `FooExtraTests.cs`) are banned: edge cases and branch-coverage hardening go inside `FooTests.cs`. `TestSuiteConventionTests` fails the build of the test project when a `*HardeningTests.cs` file appears. Copy-pasting a test verbatim fails the build too (Sonar S4144).

### No Fixed Sleeps in Tests
A test never waits a fixed time for something to happen ("sleep 50 ms, then assert"): on a loaded machine the time is not enough and the test fails with no bug behind it. Wait for the observed state with `TestWait.UntilAsync` / `TestWait.SucceededAsync` / `TestWait.InvokedAsync` (`tests/NexJob.Tests/TestWait.cs`), a `TaskCompletionSource` signalled by the code under test, or the `PauseObservedSignal` helper in the reliability tests. A sleep is acceptable only to assert that something did NOT happen. Ports for hosts come from `TestPorts.Next()`, never from a bare `TcpListener(…, 0)`.

### Prefer Simplification Over Accumulation
Internal code (`Internal/`, non-public types) is not a contract. When adding behavior, change the existing internal method instead of adding a `*WithX` / `*V2` sibling. Public API (`IScheduler`, `IJobStorage`, public models) stays protected by SemVer. Leave the code with fewer lines than you found it when you touch it.

---

## Token & Context Governance (Universal Economy Rules)

- **Zero Preamble & Direct Responses:** Never repeat user prompts or provide conversational filler. Go straight to the diff, error, or solution.
- **Surgical File Reading:** Grep/locate symbol line ranges first; read with targeted line slicing (`StartLine`/`EndLine`). Never load 500+ lines into context unnecessarily.
- **Never Re-read:** Trust existing context; never re-read files that were not modified.
- **Surgical Logs & Truncation:** When running `dotnet test` or `dotnet build`, truncate output to the failure message and stack trace. Never flood the context with hundreds of lines of passing logs.
- **Anti-Loop Safety:** If a command or test fails twice with the identical error, stop immediately and diagnose root cause rather than blindly retrying.
