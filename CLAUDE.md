# NexJob — Project Context for Claude Code

This file is automatically loaded by Claude Code.
It defines architecture, constraints, and behavioral guarantees.

---

## Project Status

NexJob is a production-oriented background job processing library.
Current published version: **v1.0.0**
Active development: **v2.0.0**

### Implemented (v1.0.0)
- `IJob` / `IJob<T>` — simple and structured jobs
- Wake-up channel — near-zero latency local dispatch
- `deadlineAfter` — deadline enforcement before execution
- `IDeadLetterHandler<TJob>` — permanent failure fallback
- Retry policies — global + per-job `[Retry]` attribute
- `[Throttle]` — resource-based concurrency limits
- `IJobContext` — injectable runtime context
- Recurring jobs — via code + via `appsettings.json`
- Schema migrations — auto-applied at startup
- Graceful shutdown
- Dashboard — dark UI, timeline, live updates, standalone mode
- OpenTelemetry — `NexJobActivitySource` + `NexJobMetrics` already in core
- 5 storage providers: InMemory, PostgreSQL, SQL Server, Redis, MongoDB
- `DuplicatePolicy` — atomic deduplication across all providers
- `CommitJobResultAsync` — idempotent result commit
- `IJobExecutionFilter` — middleware pipeline
- Job retention + auto-cleanup

### In Development (v2.0.0)
- `JobRecordFactory` — extracted factory for building `JobRecord` (prerequisite for triggers)
- `NexJob.Trigger.*` — external trigger packages (Azure Service Bus, AWS SQS, RabbitMQ, Kafka, Google Pub/Sub)
- `NexJob.OpenTelemetry` — opt-in instrumentation package exposing existing `ActivitySource` + `Meter`

---

## Squad Lanes — v2

Claude Code owns:
- All core (`src/NexJob`) changes — the only agent allowed to touch core
- `JobRecordFactory` extraction
- All `NexJob.Trigger.*` implementations (trigger packages are external to core — they depend on core, never the reverse)
- `NexJob.OpenTelemetry` package
- Testcontainers integration tests for all triggers
- Benchmarks

Qwen owns:
- SDK client implementations within trigger packages (message lock renewal, visibility timeout, offset commit)
- Review of trigger contracts and broker-specific guarantees

Gemini owns:
- Docs and usage examples for each trigger package
- Dashboard updates for v2 metrics
- Wiki updates

**Hard rule:** trigger packages are external consumers of core. They call `IScheduler.EnqueueAsync` and `JobWakeUpChannel.Signal()`. They never modify `IStorageProvider`, `JobRecord`, or any core internal.

---

## Core Principles

1. Simplicity first
2. Advanced scenarios supported
3. Predictability over magic
4. Developer experience matters
5. Reliability by design

---

## Trigger Architecture (v2)

Triggers are adapters — they receive a broker message and translate it into a NexJob job.

```
[Broker message]
      ↓
NexJob.Trigger.{Broker}   ← external package, depends only on NexJob.Core
      ↓
JobRecordFactory.Build()  ← shared factory, no duplication
      ↓
IScheduler.EnqueueAsync() ← existing contract, unchanged
      ↓
JobWakeUpChannel.Signal() ← existing mechanism, unchanged
      ↓
[NexJob pipeline — unmodified]
```

Key invariants for trigger implementations:
- Dead-letter the broker message if `EnqueueAsync` throws — never silently drop
- Extract `traceparent` from broker message headers and pass via `JobRecord.TraceParent`
- Use idempotency key from broker `MessageId` / deduplication ID to prevent double-enqueue on redelivery
- Never hold a broker message lock longer than necessary — complete ack/nack before returning

---

## OTel — What Already Exists (do not recreate)

`NexJobActivitySource` and `NexJobMetrics` are already in `src/NexJob/Telemetry/`.

`NexJob.OpenTelemetry` package only needs to:
- Expose `AddNexJobInstrumentation()` extension that registers `NexJobActivitySource.Name` and `NexJobMetrics.MeterName`
- Add `nexjob.trigger_source` tag to existing spans when a trigger is the origin

Do not create new `ActivitySource` or `Meter` instances — use the existing ones.

---

## Job Model

- `IJob` → simple jobs (no input)
- `IJob<T>` → structured jobs (typed input, JSON serialized)

---

## Dispatch Model

- Wake-up signaling for local enqueue (`JobWakeUpChannel` — capacity 1, DropWrite)
- Polling fallback for distributed scenarios
- Triggers call `Signal()` after successful `EnqueueAsync` — same as `DefaultScheduler`

---

## Storage Model

- Storage is the single source of truth
- Dispatcher is stateless — all state transitions persisted
- `IStorageProvider` is stable — do not add methods without explicit architectural decision
- `JobRecord.TraceParent` already exists for W3C context propagation

---

## Design Constraints (Runtime Guarantees)

1. Wake-up signaling must never block
2. Deadline must be enforced before execution begins
3. Dead-letter handlers must never crash the dispatcher
4. Simple jobs must remain simple (`IJob`)
5. No unnecessary DTO requirements
6. Storage is authoritative for all state
7. Zero warnings in Release builds
8. Trigger packages must not create circular dependencies with core

---

## AI Execution System

All AI-assisted tasks use the **NexJob AI Operating Model**.

**Entry point:** `ai-method/QUICK_REFERENCE_ULTRA.md`
**Full docs:** `ai-method/README.md`

### How to Use

1. **Load foundation:** `ai-method/core/00-foundation-minimal.md` (every task)
2. **Choose workflow:** `ai-method/workflows/{feature|bugfix|test|refactor|reliability|release}.md`
3. **Choose mode:** `ai-method/modes/{01-architect|02-execution|03-validation|04-release}-mode.md`
4. **Load skill:** `skills/nexjob-trigger.md` for any trigger work, `skills/nexjob-core.md` for core work

### Core Invariants (Always Enforced)

- Storage is the single source of truth
- Dispatcher is stateless
- Deadline enforced before execution
- Dead-letter handlers never crash
- Wake-up signaling never blocks
- Trigger packages never modify core

---

## Code Quality

- Zero warnings in `Release` builds
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` must be respected
- No `NotImplementedException` or placeholders
- All public APIs must have XML documentation (`///`)
- Classes `sealed` by default
- `async/await` everywhere — never `.Result` or `.Wait()`
- `CancellationToken` propagated in all async calls
- Always use `.ConfigureAwait(false)` in library projects (`src/NexJob*`)
- `StringComparison.Ordinal` or `OrdinalIgnoreCase` for all string comparisons
- Banned APIs: `DateTime.Now` (use `UtcNow`), `.Result`, `.Wait()`
- **80% Unit Coverage** — strictly enforced via CI for all new code
- **Must-Have Testing Matrix** — every feature must cover: Retry & Dead-Letter, Concurrency, Crash Recovery, Deadline Enforcement, and Wake-Up Latency
- StyleCop violations fail the build (SA1202, SA1204, SA1413, SA1508)
- Always run `dotnet format` before committing
- **Testing Standard (Must-Have):** 80% unit test coverage is mandatory for Core, Providers, and Triggers.\n  - Integration and Reliability tests are excluded from the coverage metric and must stay out of the `ci.yml`.\n  - Every method or feature MUST have a Testing Matrix (Positive/Negative/Inputs).

---

## AI Guardrails (Strict)

- Always work on `develop` branch — never commit directly to `main`
- `main` is release-only
- Do not propose full rewrites
- Do not introduce new abstractions without clear benefit
- Do not change public contracts unless explicitly requested
- Prefer incremental, low-risk changes
- Never touch `IStorageProvider` signature without explicit architect approval

---

## PR Creation Rules

```bash
gh pr create \
  --title "<type>(<scope>): <description>" \
  --base develop \
  --body "## Summary
<one or two sentences describing what this PR does>

## Type of change
- [ ] Bug fix
- [ ] New feature
- [ ] New trigger provider
- [ ] New storage provider
- [ ] Refactor / cleanup
- [ ] Documentation
- [ ] Tests

## Checklist
- [ ] \`dotnet build\` passes with **0 warnings**
- [ ] \`dotnet test\` passes — no regressions
- [ ] New behaviour is covered by tests
- [ ] Public API has XML documentation (\`///\`)
- [ ] Commit messages follow Conventional Commits

## Related issues
<!-- Closes #123 -->"
```

## Engineering Rules

See `CONTRIBUTING.md`
