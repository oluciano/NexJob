# AI Mode: Validation (Phase 4 Subagent)

**Role:** Verify code compliance with architecture, invariants, testing integrity, and quality standards before opening a PR.
**Mode:** Read-only / Code Reviewer.

---

## Validation Gate

Execute this mode AFTER execution (Phase 4) to catch drift, hidden redesign, invariant violations, or compiler/analyzer regressions.

---

## Architecture Compliance Checks

### 1. Storage Authority & Dispatcher
- [ ] All state transitions persisted? (check before/after snapshots).
- [ ] In-memory caching overrides storage? (NO: storage is the single source of truth).
- [ ] Dispatcher remains completely stateless? (no instance variables holding state between cycles).
- [ ] Storage is consulted for every decision? (deadline, retry, scheduling).
- [ ] Execution scope isolated per job? (new DI scope per invocation, job instances never reused).

### 2. Job Model
- [ ] Uses `IJob` or `IJob<T>` only? (no new base types).
- [ ] Unnecessary DTOs introduced? (NO).
- [ ] Input is minimal and reproducible? (identity + intent only).
- [ ] Simple jobs remain simple? (`IJob` has no input bloat).

### 3. Deadline Enforcement
- [ ] Deadline checked BEFORE execution begins? (not after).
- [ ] Expired jobs never execute? (skipped gracefully).
- [ ] Deadline comparison logic correct? (use `DateTime.UtcNow`).
- [ ] Expired jobs logged or tracked? (observability).

### 4. Retry & Dead-Letter
- [ ] Retries persisted on failure? (not in-memory counting).
- [ ] Exhausted retries transition to dead-letter? (all paths covered).
- [ ] Dead-letter handler exceptions swallowed/logged? (never propagate, never crash dispatcher).
- [ ] Dead-letter handler invoked only when appropriate? (not on deadline expire).

### 5. Trigger Implementation Contract (if touching Triggers)
- [ ] **Never silently drop:** Dead-letter invoked if `IScheduler.EnqueueAsync` fails.
- [ ] **Idempotency:** Native broker message ID used as `idempotencyKey`.
- [ ] **Trace propagation:** Broker headers extract `traceparent` into `JobRecord.TraceParent`.
- [ ] **Signaling:** Relies on `IScheduler.EnqueueAsync` internal signaling (no manual wake-up channel calls).
- [ ] **Ack ordering:** Broker message acknowledged ONLY after storage enqueue completes successfully.

---

## Code Quality & Standards Checks

### 1. Compilation & Multi-Targeting
- [ ] Compiles with **0 warnings and 0 errors** on both `net8.0` and `net10.0` (`dotnet build -c Release`).
- [ ] `TreatWarningsAsErrors = true` strictly respected across the entire solution.
- [ ] Analyzers clean: NetAnalyzers 10.0, Sonar, Meziantou, StyleCop, BannedApiAnalyzers.
- [ ] Banned APIs check: NO `DateTime.Now` (use `DateTime.UtcNow`), NO `.Result`, NO `.Wait()`.

### 2. Async & Concurrency Integrity
- [ ] Pure `async/await` throughout (no `.Result` or `.Wait()`).
- [ ] `CancellationToken` propagated through all async calls without dropping or ignoring.
- [ ] `.ConfigureAwait(false)` present on all async calls in library projects (`src/NexJob*`), **EXCEPT in `src/NexJob.Dashboard` component rendering lifecycle** (`IComponent.SetParametersAsync` / `HtmlRenderer`) where the Blazor SynchronizationContext must be preserved.

### 3. StyleCop Compliance (SA Rules)
- [ ] **SA1202 / SA1204:** Member ordering (public members first, static helpers next, private instance methods last).
- [ ] **SA1413:** Multi-line initializers, parameter lists, and argument lists end with a trailing comma.
- [ ] **SA1508:** No redundant blank lines before closing braces `}`.
- [ ] **S1481 / Variable Usage:** No unused local variables; deliberately unused marked with `_`.
- [ ] **S2139 / Exception Handling:** All caught exceptions logged or rethrown; dead-letter exceptions swallowed and logged.

### 4. Class Design & Documentation
- [ ] Classes `sealed` by default (unless inheritance is explicitly required).
- [ ] Dependencies injected, not static. No global state introduced.
- [ ] String comparisons specify `StringComparison.Ordinal` or `StringComparison.OrdinalIgnoreCase`.
- [ ] All public types and members have complete XML documentation (`///`). Docs describe behavior, not just repeat the name.
- [ ] Internal code simplified rather than accumulated (avoid `*WithX` / `*V2` sibling methods).

---

## Feature-Specific Checks

### 1. Recurring Jobs
- [ ] Schedule definitions preserved and accurately parsed via `Cronos`.
- [ ] Each occurrence creates a new independent job instance.
- [ ] Concurrency and distributed recurring locks respected per schedule.

### 2. Wake-Up Channel
- [ ] Never blocks producers (bounded, capacity 1).
- [ ] Collapses multiple signals (overwrite/drop-oldest, not an unbounded queue).
- [ ] Polling interval acts as fallback for multi-node / distributed synchronization.

### 3. Observability & Telemetry
- [ ] Metrics reflect persisted state (not transient in-memory counters).
- [ ] No derived or fake state exposed.
- [ ] OpenTelemetry activities and tags populated without excessive allocations on hot-paths.

---

## Integration & DI Checks

### 1. Dependency Injection
- [ ] Scoped services behave correctly (execution scope isolated per job).
- [ ] No shared state across executions.
- [ ] Job instances never reused across multiple executions.

### 2. Type Resolution & Versioning
- [ ] Runtime type metadata used correctly.
- [ ] Type renaming won't silently break stored jobs.
- [ ] Versioning strategy respected (add overloads instead of modifying existing public parameters).

### 3. Provider Differences
- [ ] Tolerates variations across storage providers (Postgres, SqlServer, Mongo, Redis, InMemory).
- [ ] Doesn't assume database-specific locking guarantees outside the provider implementation.
- [ ] Handles timing differences and advisory lock mechanics.

---

## Hidden Redesign Red Flags

Watch for subtle architectural violations:
- **In-memory caches** disguised as "performance optimization".
- **Dispatcher state** (instance variables persisting state across dispatch cycles).
- **Deadline logic** moved to the wrong place or evaluated after execution begins.
- **Dead-letter exceptions** propagating uncaught and killing the host/dispatcher.
- **Storage bypasses** via application-level memoization.
- **Synchronous blocking** in async paths.
- **Untraced state transitions** (persisted only partially).
- **New DTOs** introduced silently.
- **Job model complexity** growing unnecessarily on `IJob`.
- **Magic behavior** making system execution non-deterministic.

---

## Test Integrity & Rigor (Lessons from Releases)

### 1. Mandatory 3N Matrix
- [ ] **N1 (Positive):** Happy path operating as expected under normal conditions.
- [ ] **N2 (Negative):** Explicit failure path tested (broker failure, storage exception, timeout).
- [ ] **N3 (Invalid Input / Boundary):** Null, empty, edge-case payload or extreme concurrency handled gracefully.

### 2. Test Execution Integrity Rules
- [ ] **NO FIXED SLEEPS:** Absolutely no `Task.Delay(50)` in tests to wait for state. Must use `TestWait.UntilAsync`, `TaskCompletionSource`, or `PauseObservedSignal`. (A sleep is acceptable ONLY to assert that something did NOT happen).
- [ ] **Search Across ALL 19 Test Projects:** When altering pinned values or behaviors, grep across *all* projects in `tests/` (unit, integration, reliability, samples), not just the local test project.
- [ ] **Existing Tests Are Contracts:** Existing assertions were NEVER weakened, inverted, or deleted to make new code pass. If behavior intentionally changed, a comment `// Behavior changed in vX.Y: <reason>` is required.
- [ ] **One Canonical Test File per Class:** Production class `Foo` has exactly one test file `FooTests.cs`. Parallel files (`FooHardeningTests.cs`, `FooExtraTests.cs`) are strictly banned.
- [ ] **Prove Guard Tests Fail:** Concurrency or deadlock guard tests must be proven by reverting the fix and watching the guard test fail.
- [ ] **Tie Concurrency Assertions to Specific Statement:** Avoid global counters that trip on unrelated rare events.
- [ ] **No Static State Leaks:** Test helpers with `static` state (e.g. recording dead-letter handlers) must not share state across parallel test fixtures.
- [ ] **Mongo Serializer Bootstrap:** Mongo integration tests must ensure `MongoSerializerBootstrap` runs before instantiating drivers.
- [ ] **Sample Configuration Drift Guard:** Extend `SampleConfigurationTests` whenever samples gain new settings.
- [ ] **Contract Test Self-Audit:** Run `git diff develop -- tests` and ensure no assertion was weakened.

---

## Documentation & Release Readiness

- [ ] `CHANGELOG.md` updated under `## [Unreleased]` with a clear, concise bullet point and linked issue/PR.
- [ ] **No Repo-Relative Links in CHANGELOG:** Use plain text for repo paths in `CHANGELOG.md` (it is published as `reference/changelog.md` and repo links break MkDocs strict builds).
- [ ] **Dependency Bumps (v6.2.0):** Always record library dependency bumps under `### Dependencies` in `CHANGELOG.md`.
- [ ] **Compile and Run Doc Code:** Any code snippet placed in Wiki or READMEs must be compiled and verified against real packages.
- [ ] **No Unverified Performance Claims:** Do not claim a speed gain without a full default-job benchmark run on both runtimes.
- [ ] DocsTruth verified: Package `README.md` and `docs/wiki` updated if new options, settings, or behaviors were added (`dotnet run --project tools/DocsTruth -c Release -- --strict`).

---

## Validation Output Format

The validation subagent must return a structured report:

```markdown
### Validation Verdict: [PASS | FAIL | CONDITIONAL]

#### 1. Invariants & Standards Audit
- [x] Zero warnings on net8.0 & net10.0
- [x] .ConfigureAwait(false) compliance
- [x] XML Docs & StyleCop rules
- [x] CancellationToken propagation
- [x] Storage Authority & Stateless Dispatcher

#### 2. Spec & 3N Matrix Audit
- **N1 Positive:** [Test name & status]
- **N2 Negative:** [Test name & status]
- **N3 Invalid Input:** [Test name & status]

#### 3. Release Lessons Verification
- [x] No fixed sleeps (`Task.Delay` banned)
- [x] No static state leaks across tests
- [x] Plain-text paths in CHANGELOG

#### 4. Issues / Risks Detected (if FAIL)
- [File:Line] Specific violation and recommended minimal remediation.

#### 5. Recommendation for PR
- [Ready to open PR | Blocking fixes required]
```
