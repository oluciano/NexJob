# Architect Output Template

Use this to document architectural designs before implementation.

---

## Task

**[Reference the task being designed]**

---

## Overview

**What will change:**
- Feature description
- Scope and boundaries

**Why this approach:**
- Design rationale
- Architectural alignment

**Impact:**
- What systems are affected
- Backward compatibility (yes/no)

---

## Classes to Create

### ClassName1

**Visibility:** public sealed
**Responsibility:** What it does
**Location:** src/NexJob/...

**Fields:**
- `_fieldName: FieldType` (private) — why

**Dependencies:**
- `IService: IService` (injected)

---

### ClassName2

**Visibility:** internal sealed
**Responsibility:** What it does
**Location:** src/NexJob/...

---

## Method Signatures

### ClassName1

```csharp
public async Task<T> MethodNameAsync(
    parameter1: Type1,
    parameter2: Type2,
    cancellationToken: CancellationToken)
```

**Purpose:** What it does
**Returns:** What it returns
**Throws:** Any exceptions?
**Notes:** Important behavior

---

### ClassName2

```csharp
private bool IsValid(job: Job) => job.ExpiresAt > DateTime.UtcNow
```

---

## Execution Flow

### Happy Path
1. Step 1 — description
2. Step 2 — description
3. Check deadline (before execution)
4. Step 4 — description
5. Persist result to storage

### Failure Path
1. Failure occurs
2. Retry policy evaluated
3. If retries remain → reschedule
4. If exhausted → invoke dead-letter handler
5. Persist state

### Edge Cases
1. If deadline expired → skip execution, mark Expired
2. If storage unreachable → what happens?
3. If handler throws → log, continue

---

## State Transitions

**Before:**
- JobStatus: Enqueued
- Storage: job record persisted

**After (success):**
- JobStatus: Succeeded
- Storage: result persisted

**After (failure):**
- JobStatus: Failed or Dead-letter
- Storage: attempt recorded, next state scheduled

---

## Architecture Compliance

### Storage Authority
- [ ] Storage is source of truth? YES
- [ ] All state transitions persisted? YES
- [ ] In-memory cache overrides storage? NO

### Job Model Compliance
- [ ] Uses IJob or IJob<T>? YES
- [ ] Unnecessary DTOs? NO
- [ ] Input is minimal? YES

### Dispatcher Correctness
- [ ] Dispatcher remains stateless? YES
- [ ] Execution scope isolated? YES
- [ ] CancellationToken propagated? YES

### Deadline Handling
- [ ] Deadline checked before execution? YES
- [ ] Expired jobs never execute? YES
- [ ] State transitioned correctly? YES

### Retry & Dead-Letter
- [ ] Retries persisted? YES
- [ ] Dead-letter handler safe? YES (exceptions swallowed)
- [ ] Handler invoked appropriately? YES

---

## Invariants Preserved

- [ ] Storage is single source of truth
- [ ] Dispatcher is stateless
- [ ] All state transitions persisted
- [ ] Deadline enforced before execution
- [ ] Dead-letter handlers never crash
- [ ] Wake-up signaling never blocks
- [ ] Execution is deterministic and testable

---

## Risks & Mitigations

### Risk: Storage Bypass
**Mitigation:** All state transitions go through storage API

### Risk: Deadline Logic
**Mitigation:** Check after fetch, before execute, explicit skip

### Risk: Dead-Letter Crash
**Mitigation:** Handler wrapped in try-catch, exceptions logged only

### Risk: Determinism
**Mitigation:** No timing-dependent logic, all decisions persisted

---

## Testing Strategy

**Unit Tests:**
- Isolated class behavior
- State transitions
- Edge cases

**Integration Tests:**
- Full workflow with real storage
- End-to-end scenarios

**Reliability Tests:**
- Crash recovery
- Concurrency
- Deadline enforcement

---

## Implementation Notes

**Files to create:**
- src/NexJob/ClassName1.cs
- src/NexJob/ClassName2.cs
- tests/NexJob.Tests/ClassName1Tests.cs

**Files to modify:**
- src/NexJob/Dispatcher.cs (add step X)
- src/NexJob/IStorageProvider.cs (if new interface)

**Files NOT changing:**
- ARCHITECTURE.md (rationale should fit existing architecture)
- Public API surface (should be additive or transparent)

---

## What Will NOT Change

- Core job execution model
- Storage as source of truth
- Dispatcher responsibility
- Public API compatibility (unless breaking change)
- Existing invariants (must be preserved)

---

## Questions for Implementation Team

- Are there any ambiguities above?
- Does the flow make sense?
- Are method signatures clear?
- Is the architecture compliant?
- Can you implement this without changes to this design?

---

## Sign-Off

**Architect:** [AI or human architect]
**Date:** [Date]
**Status:** Ready for Implementation
