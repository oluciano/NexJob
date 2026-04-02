# NexJob — AI Foundation (Minimal)

**Read this for 80% of tasks. Only add extended foundation if needed.**

---

## Core Invariants (Non-Negotiable)

- **Storage is the single source of truth** — no in-memory state overrides it
- **Dispatcher is stateless** — all state transitions must be persisted
- **Deadline enforced before execution** — expired jobs never execute
- **Dead-letter handlers must never crash** — errors logged and swallowed
- **Wake-up signaling never blocks** — bounded channel, collapses signals

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

## Execution Constraints

1. **Deadline** — calculated at enqueue, stored as `ExpiresAt`, checked before execution
2. **Retry** — on failure: record attempt → evaluate policy → reschedule OR dead-letter
3. **Cancellation** — always propagate `CancellationToken`, never ignore
4. **Async** — never use `.Result` or `.Wait()`

---

## Code Quality (Enforced at Build)

- Zero compiler warnings in `Release` builds
- Zero `NotImplementedException`
- Classes `sealed` by default
- Public APIs have XML docs (`///`)
- Production-ready code only

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

## Golden Rule

**If behavior is not explicitly defined → DO NOT IMPLEMENT → ASK instead**
