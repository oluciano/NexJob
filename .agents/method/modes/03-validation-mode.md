# AI Mode: Validation

**Role:** Verify code compliance with architecture, invariants, and quality standards.

---

## Validation Gate

Use this mode AFTER execution to catch drift, hidden redesign, or invariant violations.

---

## Architecture Compliance Checks

### Storage Authority
- [ ] All state transitions persisted? (check before/after snapshots)
- [ ] In-memory caching overrides storage? (NO)
- [ ] Dispatcher remains stateless? (no instance variables holding state between cycles)
- [ ] Storage is consulted for every decision? (deadline, retry, scheduling)

### Job Model
- [ ] Uses `IJob` or `IJob<T>` only? (no new base types)
- [ ] Unnecessary DTOs introduced? (NO)
- [ ] Input is minimal and reproducible? (identity + intent only)
- [ ] Simple jobs remain simple? (`IJob` has no input bloat)

### Deadline Enforcement
- [ ] Deadline checked BEFORE execution begins? (not after)
- [ ] Expired jobs never execute? (skipped gracefully)
- [ ] Deadline comparison logic correct? (use `DateTime.UtcNow`)
- [ ] Expired jobs logged or tracked? (observability)

### Retry & Dead-Letter
- [ ] Retries persisted on failure? (not in-memory counting)
- [ ] Exhausted retries transition to dead-letter? (all paths covered)
- [ ] Dead-letter handler exceptions swallowed? (logged, never propagate)
- [ ] Dead-letter handler invoked only when appropriate? (not on deadline expire)

### Dispatcher Correctness
- [ ] Dispatcher stateless between cycles? (no caches, no state holding)
- [ ] Execution scope isolated per job? (DI scope per invocation)
- [ ] CancellationToken propagated? (through all async calls)
- [ ] No `.Result` or `.Wait()` calls? (all async/await)

---

## Code Quality Checks

### Compilation
- [ ] Builds with zero warnings? (`dotnet build --configuration Release`)
- [ ] TreatWarningsAsErrors respected? (all warnings fixed)
- [ ] All dependencies resolved? (no missing packages)

### StyleCop Compliance (SA rules)

**Member Ordering (SA1202, SA1204):**
- [ ] Public members first?
- [ ] Static helpers next?
- [ ] Private instance methods last?

**Trailing Commas (SA1413):**
- [ ] Multi-line initializers end with comma?
- [ ] Multi-line parameter lists end with comma?
- [ ] Multi-line argument lists end with comma?

**Blank Lines (SA1508):**
- [ ] No blank line before closing brace? `}`

**Variable Usage (S1481):**
- [ ] No unused local variables?
- [ ] Deliberately unused marked with `_`?

**Exception Handling (S2139):**
- [ ] All caught exceptions logged or rethrown?
- [ ] Dead-letter handler: logged (not rethrown)?
- [ ] No silent swallows except dead-letter handlers?

### Public API Documentation
- [ ] All public types have XML docs? (`///`)
- [ ] All public members documented?
- [ ] Docs describe behavior, not just repeat name?

### Async & Concurrency
- [ ] No `.Result` or `.Wait()`? (async/await only)
- [ ] CancellationToken propagated everywhere?
- [ ] Cancellation not ignored?

### Class Design
- [ ] Classes sealed by default? (unless inheritance required)
- [ ] Dependencies injected, not static?
- [ ] No global state introduced?

---

## Feature-Specific Checks

### Recurring Jobs
- [ ] Schedule definitions preserved?
- [ ] Each occurrence creates new instance?
- [ ] Concurrency per schedule enforced?

### Wake-Up Channel
- [ ] Never blocks producers? (bounded, capacity 1)
- [ ] Collapses multiple signals? (overwrite, not queue)
- [ ] Polling fallback for distributed?

### Observability
- [ ] Metrics reflect persisted state? (not in-memory counters)
- [ ] No derived or fake state exposed?
- [ ] Signals available for monitoring?

---

## Integration Checks

### Dependency Injection
- [ ] Scoped services behave correctly?
- [ ] No shared state across executions?
- [ ] Job instances never reused?

### Type Resolution
- [ ] Runtime type metadata used correctly?
- [ ] Type renaming won't silently break?
- [ ] Versioning strategy respected?

### Provider Differences
- [ ] Tolerates variation in dequeue fairness?
- [ ] Doesn't assume locking guarantees?
- [ ] Handles timing differences?

---

## Hidden Redesign Red Flags

Watch for subtle architectural violations:

- **In-memory caches** disguised as "optimization"
- **Dispatcher state** (instance variables persisting across cycles)
- **Deadline logic** moved to wrong place
- **Dead-letter exceptions** propagating uncaught
- **Storage bypasses** via application-level caching
- **Synchronous blocking** in async methods
- **Untraced state transitions** (persisted only partially)
- **New DTOs** introduced silently
- **Job model complexity** growing on `IJob`
- **Magic behavior** making system unpredictable

---

## Test Coverage Checks

- [ ] New behavior has tests?
- [ ] 3N matrix applied? (N1 positive, N2 negative, N3 invalid input)
- [ ] Tests are deterministic? (no race conditions, timing dependencies)
- [ ] Tests avoid mocks when possible? (use real storage)
- [ ] Edge cases covered? (deadline expire, retry exhaustion, crash recovery)
- [ ] Integration tests pass?
- [ ] Reliability suite passes (if applicable)?
- [ ] **No existing passing test was modified without justification?**
- [ ] **If test was modified — comment explains the behavior change?**

---

## Validation Output

When validating, report:

1. **Verdict** — PASS / FAIL / CONDITIONAL
2. **Issues found** — specific violations, line numbers if applicable
3. **Risk level** — LOW / MEDIUM / HIGH (based on invariant impact)
4. **Recommendations** — what to fix before merge

---

## Blocking Issues (Must Fix)

- Compiler warnings
- StyleCop violations
- Storage bypass
- Deadline violation
- Dead-letter handler crash risk
- Missing XML docs on public API
- Untraced state transitions
- Synchronous blocking
- **Existing passing test was modified to make new code pass (without documented behavior change)**
- **New feature missing 3N test matrix**

---

## Non-Blocking Issues (Suggest Fix)

- Code clarity (rename for readability)
- Test coverage gaps
- Performance optimization opportunities
- Documentation improvements
