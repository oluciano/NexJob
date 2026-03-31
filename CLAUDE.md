# NexJob — Project Context for Claude Code

This file is automatically loaded by Claude Code.

It defines architecture, constraints, and behavioral guarantees.

---

## Project Status

NexJob is a production-oriented background job processing library.

### Implemented

* In-memory storage
* `IJob` / `IJob<T>`
* Wake-up dispatch
* `deadlineAfter`
* Dead-letter handler
* Retry policies
* Scheduling
* Dashboard

### Evolving

* Distributed coordination
* Multi-node consistency
* Storage parity

---

## Core Principles

1. Simplicity first
2. Advanced scenarios supported
3. Predictability over magic
4. Developer experience matters
5. Reliability by design

---

## Job Model

* `IJob` → simple jobs
* `IJob<T>` → structured jobs

---

## Dispatch Model

* Wake-up signaling for local enqueue
* Polling fallback for distributed scenarios

---

## Deadline Model

* Defined via `deadlineAfter`
* Evaluated immediately after fetch and before execution
* Expired jobs are skipped

---

## Failure Model

* Retryable failure
* Permanent failure (dead-letter)
* Expired

---

## Storage Model

* Storage is the single source of truth
* Dispatcher is stateless
* All job state transitions must be persisted

---

## Design Constraints (Runtime Guarantees)

1. Wake-up signaling must never block
2. Deadline must be enforced before execution begins
3. Dead-letter handlers must never crash the dispatcher
4. Simple jobs must remain simple (`IJob`)
5. No unnecessary DTO requirements
6. Storage is authoritative for all state
7. Zero warnings in Release builds

---

## AI Execution Rules (Non-Negotiable)

These rules MUST be followed by any generated or modified code.

**BEFORE WRITING CODE:**
1. Read this entire section (yes, all of it)
2. Check `NEXJOB_AI_CONTEXT_MINIMAL.md` for core rules
3. Check `ARCHITECTURE.md` for design patterns
4. Review `CONTRIBUTING.md` for engineering standards
5. Review the "Code Style" section below — StyleCop violations will **fail the build**

**DURING CODE GENERATION:**
- Apply StyleCop rules **immediately** (not as a separate refactor step)
- Compile locally before committing (`dotnet build --configuration Release`)
- Zero warnings is a requirement, not a nice-to-have

### Code Quality

* No `NotImplementedException`
* No placeholders or incomplete implementations
* Code must be production-ready

### Compilation

* Zero warnings in `Release` builds
* `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` must be respected

### Async & Concurrency

* Never use `.Result` or `.Wait()`
* Always use `async/await`
* Always propagate `CancellationToken`
* Never ignore cancellation

### Class Design

* Classes must be `sealed` by default
* Only allow inheritance when explicitly required by architecture

### Public API

* All public types and members must have XML documentation (`///`)

### Code Style (StyleCop Compliance)

These are **ENFORCED** at build time with `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
Fix them **during code generation**, not after.

**Member Ordering (SA1202, SA1204)**
- Public members first
- Then static members
- Then instance methods
- Pattern: public → static helpers → private/internal instance methods

Example:
```csharp
public sealed class MyClass
{
    public string Property { get; set; }           // public first
    
    private static void HelperMethod() { }        // static next
    
    private async Task PrivateMethodAsync() { }   // instance last
}
```

**Trailing Commas (SA1413)**
- Multi-line initializers, parameter lists, argument lists MUST end with trailing comma
```csharp
var obj = new MyClass
{
    Prop1 = value1,
    Prop2 = value2,  // ← trailing comma required
};
```

**Blank Lines (SA1508)**
- No blank lines before closing brace
```csharp
public void Method()
{
    DoSomething();
}  // ← no blank line before this
```

**Variable Usage (S1481 / Sonar)**
- No unused local variables
- Use `_` if deliberately unused, or remove the variable entirely

**Exception Handling (S2139 / Sonar)**
- When catching exceptions, always log or rethrow with context
- Never silently swallow exceptions
```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Context about what failed");
    throw new SpecificException("Message with context", ex);  // rethrow with info
}
```

### Architecture Compliance

* Respect Job model (`IJob` and `IJob<T>`)
* Do not introduce unnecessary DTOs
* Do not bypass storage as source of truth
* Do not introduce static/global state (unless explicitly allowed)

### Testing Awareness

* Any new behavior must be testable
* Avoid designs that prevent deterministic testing

---

These rules override convenience or shortcuts.

---

## Engineering Rules

See `CONTRIBUTING.md`

