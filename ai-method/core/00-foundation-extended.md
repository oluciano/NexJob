# NexJob — AI Foundation (Extended)

**Read this for complex scenarios or architectural compliance checks.**

---

## Architecture Reference

See `ARCHITECTURE.md` for the authoritative system design.

---

## Dispatcher Architecture

### JobDispatcherService is an orchestrator
`ExecuteJobAsync` is a thin orchestrator — it calls private named methods for each stage.
Do not add inline business logic to it. Each stage has a single responsibility:

- `TryHandleExpirationAsync` — deadline check only
- `PrepareInvocationAsync` — type resolution, migration, deserialization, DI scope
- `ExecuteWithThrottlingAsync` — throttle acquisition + job invocation
- `HandleFailureAsync` — retry calculation + decision logging
- `RecordSuccessMetrics` — metrics only

### JobInvocationContext owns the DI scope
`JobInvocationContext` implements `IDisposable` and disposes the DI scope on `Dispose()`.
Always use `using var context = await PrepareInvocationAsync(job)` — never dispose manually.

### Decision logging is mandatory at decision points
The dispatcher must log the *reason* for every automatic decision:
- Queue skipped (paused or outside window) → `LogDebug`
- Throttle wait started → `LogDebug`
- Retry scheduled → `LogInformation` with delay and scheduled time
- Dead-letter → `LogError`

---

## Design Constraints (Runtime Guarantees)

1. Wake-up signaling must never block (bounded channel, capacity 1, collapses signals)
2. Deadline must be enforced before execution begins (check after fetch, before invoke)
3. Dead-letter handlers must never crash dispatcher (swallow exceptions, log only)
4. Simple jobs must remain simple (`IJob` needs no input complexity)
5. No unnecessary DTO requirements (input must be minimal and reproducible)
6. Storage is authoritative for all state (no cache optimizations override storage)
7. Zero warnings in Release builds (treat warnings as errors)

---

## Testing Strategy

### Reliability Suite
- Project: `NexJob.ReliabilityTests.Distributed`
- Validates scenarios against **real storage providers** via Docker.
- Coverage: Retry & Dead-Letter, Concurrency, Crash Recovery, Deadline Enforcement, Wake-Up Latency.
- Providers: PostgreSQL 16, SQL Server 2022, Redis 7, MongoDB 7.

---

## Architecture Compliance Checklist

Verify these during implementation/fix:
- Respects Job model (`IJob` and `IJob<T>`).
- Does not introduce unnecessary DTOs.
- Does not bypass storage as source of truth.
- Does not introduce static/global state.
- Designs don't prevent deterministic testing.
- No in-memory optimization overrides storage truth.

---

## Code Quality Requirements

### Public API
- All public types and members must have XML documentation (`///`).

### Async & Concurrency
- Never use `.Result` or `.Wait()`.
- Always use `async/await`.
- Always propagate `CancellationToken`.
- Never ignore cancellation.

### Class Design
- Classes must be `sealed` by default.
- Only allow inheritance when explicitly required by architecture.

### Exception Handling
- Never silently swallow exceptions (log or rethrow).
- Dead-letter handler errors are exceptions (swallow only, log always).
