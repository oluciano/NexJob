# NexJob — AI Foundation (Minimal)

**Read this for 80% of tasks. Only add extended foundation if needed.**

---

## Core Invariants (Non-Negotiable)

- **Storage is the single source of truth** — no in-memory state overrides it
- **Dispatcher is stateless** — all state transitions must be persisted
- **Deadline enforced before execution** — expired jobs never execute
- **Dead-letter handlers must never crash** — errors logged and swallowed
- **Wake-up signaling never blocks** — bounded channel, collapses signals
- **Storage contracts are segregated** — `IJobStorage` (execution), `IRecurringStorage` (cron), `IDashboardStorage` (queries)
- **Multi-targeting baseline** — dual-target `net8.0;net10.0`; all code must compile on both with zero warnings

---

## Job Model

- `IJob` → simple jobs (no input)
- `IJob<T>` → structured input only (minimal, identity + intent)

**Rule:** No unnecessary DTOs. Input must be reproducible and contextual.

---

## State Transitions (All Persisted)

```
Enqueued → Processing → Succeeded
Processing → Failed → Retry OR Dead-letter
Enqueued → Expired (never executes)
Failed → Dead-letter (terminal, handler invoked)
```

---

## Storage Contract

### Execution result must be committed atomically
All job finalisation mutations — status, logs, continuations, recurring result — must be
persisted in a single call to `CommitJobResultAsync`. Never call `AcknowledgeAsync`,
`SaveExecutionLogsAsync`, and `EnqueueContinuationsAsync` as separate steps from the dispatcher.

**Rule:** Partial persistence is not acceptable. Either all finalisation mutations apply or none.

### CommitJobResultAsync is idempotent
Calling `CommitJobResultAsync` twice with the same `JobId` on a job already in a terminal
state (`Succeeded`, `Failed`, `Expired`) must be a no-op — no exception, no duplicate mutations.

**Rule:** Every provider implementation must include an idempotency guard at the start of `CommitJobResultAsync`.

### DuplicatePolicy governs re-enqueue on terminal failure
When `idempotencyKey` is supplied, `EnqueueAsync` checks for an existing job with the same key.
Active states (`Enqueued`, `Processing`, `Scheduled`, `AwaitingContinuation`) always deduplicate.
Terminal states (`Failed`, `Succeeded`, `Expired`) are governed by `DuplicatePolicy`.

**Rule:** `DuplicatePolicy` default is `AllowAfterFailed`. Rejection throws `DuplicateJobException` from the scheduler — never from the storage provider directly.

---

## Execution Constraints

1. **Deadline** — calculated at enqueue, stored as `ExpiresAt`, checked before execution
2. **Retry** — on failure: record attempt → evaluate policy → reschedule OR dead-letter
3. **Cancellation** — always propagate `CancellationToken`, never ignore
4. **Async** — never use `.Result` or `.Wait()`
5. **ConfigureAwait** — always use `.ConfigureAwait(false)` in library projects, except in Blazor component rendering lifecycle (`src/NexJob.Dashboard/`)
6. **Banned APIs** — `DateTime.Now` (always use `DateTime.UtcNow`), `.Result`, `.Wait()`

---

## Code Quality (Enforced at Build)

- Zero compiler warnings in `Release` builds (`TreatWarningsAsErrors = true`)
- Zero `NotImplementedException`
- Classes `sealed` by default
- Public APIs have XML docs (`///`)
- Production-ready code only
- **Add Overloads, Never Parameters (v5.9.0):** Never add optional parameters to public interfaces; always add new overloads to preserve binary and mock compatibility

---

## StyleCop (Automatic Violations Fail Build)

**Member Ordering (SA1202, SA1204):**
```csharp
public sealed class MyClass
{
    public string Property { get; set; }           // public first
    private static void HelperMethod() { }        // static next
    private async Task PrivateMethodAsync() { }   // instance last
}
```

**Trailing Commas (SA1413):**
```csharp
var obj = new MyClass { Prop1 = value1, Prop2 = value2, };  // ← required
```

**No blank lines before closing brace (SA1508):**
```csharp
public void Method()
{
    DoSomething();
}  // ← no blank line before }
```

**No unused variables (S1481):**
- Use `_` if deliberately unused, or remove entirely

**Exception handling (S2139):**
- Always log or rethrow with context, never silently swallow

---

## Test Suite Shape

- **3N Mandatory Matrix:** Every feature or bug fix must produce minimum 3 tests:
  - **N1 — Positive:** happy path works as expected
  - **N2 — Negative:** failure path fails as expected
  - **N3 — Invalid Input:** null, empty, boundary handled gracefully
- **One canonical test file per production class:** `FooTests.cs`. Twin files (`FooHardeningTests.cs`) are banned and enforced by `TestSuiteConventionTests`.
- **Existing tests are contracts:** NEVER weaken, invert or delete an assertion. Moving, merging into a `[Theory]` and deleting a proven duplicate are allowed.
- **No Fixed Sleeps:** A test never waits a fixed time (`Task.Delay(50)` is banned). Poll with `TestWait.UntilAsync`, `TaskCompletionSource`, or `PauseObservedSignal`.
- **Red-First Proof:** Write tests first, watch them fail for the right reason, and commit them alone (`test(scope): <what>, red (#id)`) before touching production code.
- **Internal code is not a contract:** simplify the existing method instead of adding a `*WithX` / `*V2` sibling.

---

## Golden Rule

**If behavior is not explicitly defined → DO NOT IMPLEMENT → ASK instead**
