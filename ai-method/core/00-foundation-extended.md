# NexJob — AI Foundation (Extended)

**Read this only for complex scenarios, edge cases, or architectural questions.**

---

## Architecture Reference

See `ARCHITECTURE.md` for complete system design.

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

## Dispatch Flow (Detailed)

1. Fetch next eligible job from storage
2. Validate execution constraints (deadline, retry eligibility, scheduling)
3. Create isolated DI scope
4. Execute job handler
5. Persist result and transition state

**Non-responsibilities of Dispatcher:**
- Is NOT the source of truth
- Does NOT own job state
- Does NOT cache authoritative data

---

## Deadline Model (Detailed)

**Definition:**
- Specified via `deadlineAfter: TimeSpan?` at enqueue
- Stored as `ExpiresAt` in job record

**Behavior:**
- Calculated at enqueue time
- Evaluated after fetch, before execution
- If expired → job marked as `Expired`, execution skipped, no handler invoked

---

## Retry Model (Detailed)

**Evaluation:**
- Global policy applies unless overridden
- Per-job override via `[Retry]` attribute

**On failure:**
1. Attempt is recorded
2. Retry policy evaluated
3. If retries remain → rescheduled as `Enqueued`
4. If no retries → transitions to `Dead-letter`, handler invoked

---

## Dead-Letter Handling (Detailed)

**Triggered when:**
- All retries exhausted

**Behavior:**
- Handler resolved via DI (`IDeadLetterHandler<TJob>`)
- Executed in isolated DI scope
- Exceptions are logged and swallowed (never crash dispatcher)

---

## Job Lifecycle States (Complete)

- **Enqueued** → waiting to be processed
- **Processing** → currently being executed
- **Succeeded** → execution completed successfully
- **Failed** → execution failed, retry may still occur
- **Dead-letter** → permanently failed after all retries
- **Expired** → not executed before deadline

---

## Dependency Injection

- Each job execution runs in isolated DI scope
- Scoped services behave correctly
- No shared state across executions
- Job instances never reused

---

## Wake-Up Channel

**Characteristics:**
- Bounded channel (capacity = 1)
- Collapses multiple signals into single wake
- Never blocks producers

**Behavior:**
- Local enqueue → immediate wake-up signal
- Distributed enqueue → fallback to polling
- Signals are non-blocking and bounded

---

## Concurrency Model

Controlled by:
- Worker count
- Queue configuration
- Throttling rules (`[Throttle]`)
- Execution windows

Properties:
- Parallel execution within constraints
- Bounded concurrency
- No global locks

---

## Type Resolution

- Job types resolved using runtime metadata
- Types must exist at runtime
- Renames can break execution
- Versioning must be handled carefully

---

## Recurring Jobs

- Schedule definitions, not continuous executions
- Each occurrence creates a new job instance
- Concurrency controlled per schedule
- Guarantees depend on storage capabilities

---

## Provider Capability Differences

Different providers may vary in:
- Dequeue fairness
- Locking guarantees
- Recurring coordination
- Scheduling precision

**Architecture must tolerate these differences.**

---

## Observability

Exposed signals:
- Job execution rate
- Failures
- Retries
- Expired jobs
- Queue depth
- Dispatcher health

**Must reflect persisted state whenever possible (no derived state).**

---

## Performance Model

**Optimizations:**
- Wake-up signaling eliminates idle delay
- Bounded concurrency prevents resource exhaustion
- Minimal locking reduces contention

**Expected behavior:**
- Local enqueue → immediate execution attempt
- Idle system → no CPU waste

---

## Testing Strategy

### Standard Tests
- Unit tests (isolated logic)
- Integration tests (with real storage)

### Reliability Suite
- Separate project: `NexJob.ReliabilityTests.Distributed`
- Validates all scenarios against **real storage providers** via Docker
- Tests: Retry & Dead-Letter, Concurrency, Crash Recovery, Deadline Enforcement, Wake-Up Latency
- Providers: PostgreSQL 16, SQL Server 2022, Redis 7, MongoDB 7

---

## Architecture Compliance Checklist

When implementing features or fixes, verify:
- Respects Job model (`IJob` and `IJob<T>`)
- Does not introduce unnecessary DTOs
- Does not bypass storage as source of truth
- Does not introduce static/global state (unless explicitly allowed)
- Any new behavior is testable
- Designs don't prevent deterministic testing

---

## Code Quality Requirements

### Public API
- All public types and members must have XML documentation (`///`)

### Async & Concurrency
- Never use `.Result` or `.Wait()`
- Always use `async/await`
- Always propagate `CancellationToken`
- Never ignore cancellation

### Class Design
- Classes must be `sealed` by default
- Only allow inheritance when explicitly required by architecture

### Exception Handling
- Never silently swallow exceptions
- Always log or rethrow with context
- Dead-letter handler errors are exception (swallow only, log always)
