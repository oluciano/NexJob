# GEMINI.md

## Project

NexJob is a production-oriented background job processing library for .NET 8.
MIT licensed. Alternative to Hangfire with stronger deadline enforcement and free storage providers.

---

## Current Version: v0.6.0

### Implemented
- `IJob` / `IJob<T>` — simple and structured jobs
- Wake-up channel — near-zero latency local dispatch
- `deadlineAfter` — jobs expire if not executed in time (`JobStatus.Expired`)
- `IDeadLetterHandler<TJob>` — automatic fallback on permanent failure
- Retry policies — global + per-job `[Retry]` attribute
- `[Throttle]` — resource-based concurrency limits
- `IJobContext` — injectable runtime context (JobId, Attempt, Queue, Tags, Progress)
- Recurring jobs — via code + via `appsettings.json` (simple class name, not assembly-qualified)
- Schema migrations — auto-applied at startup with advisory locks
- Graceful shutdown — active jobs complete naturally
- Dashboard — dark UI, timeline, live updates, read-only mode, standalone for Worker Services
- OpenTelemetry + health checks
- 5 storage providers: InMemory, PostgreSQL, SQL Server, Redis, MongoDB
- Distributed reliability tests — 200 tests across all 4 real providers via Testcontainers

### Evolving
- Dashboard wave features (health badge, job timeline, worker heatmap, dead-letter inbox, anomaly detection)
- Distributed coordination
- Multi-node consistency
- Storage parity

---

## Core Principles

- Simplicity first
- Predictability over magic
- Reliability by design
- Developer experience matters

---

## Non-Negotiable Invariants

- Storage is the single source of truth
- Dispatcher is stateless — all state transitions must be persisted
- Deadline must be enforced before execution begins — expired jobs never execute
- Dead-letter handlers must never crash the dispatcher
- Wake-up signaling must never block
- Simple jobs must remain simple — no unnecessary DTOs

---

## Coding Rules

- Zero warnings in Release builds (`TreatWarningsAsErrors = true`)
- No placeholders, no `NotImplementedException`
- All public APIs must have XML documentation (`///`)
- Classes `sealed` by default
- `async/await` only — never `.Result` or `.Wait()`
- Propagate `CancellationToken` in all async calls
- Respect existing StyleCop rules (SA1202, SA1204, SA1413, SA1508)

---

## AI Execution System

Before executing any task, load:
- `ai-method/core/00-foundation-minimal.md` — always, every task
- Appropriate workflow: `ai-method/workflows/{feature|bugfix|test|refactor|reliability|release}.md`
- Appropriate mode: `ai-method/modes/{01-architect|02-execution|03-validation|04-release}-mode.md`

Quick router: `ai-method/QUICK_REFERENCE_ULTRA.md`

---

## Behavior Expectations

**When analyzing:**
- Respect the existing architecture
- Do not propose speculative rewrites
- Prefer incremental evolution

**When editing:**
- Keep changes minimal and production-safe
- Preserve public behavior unless explicitly asked otherwise
- Do not break invariants
- Do not introduce hidden behavior

**When refactoring:**
- Prefer clarity over abstraction
- Avoid unnecessary indirection
- Keep runtime guarantees intact

---

## AI Workflow

Before making changes:
1. Identify the affected invariant
2. Identify runtime risk
3. Prefer the smallest safe change
4. Validate against project rules

---

## AI Guardrails (Strict)

- Do not propose full rewrites
- Do not introduce new abstractions without clear benefit
- Do not change public contracts unless explicitly requested
- Prefer incremental, low-risk changes

---

## Output Style

- Be direct and precise
- Explain trade-offs briefly
- Prefer concrete implementation over generic advice

---

## Engineering Rules

See `CONTRIBUTING.md`