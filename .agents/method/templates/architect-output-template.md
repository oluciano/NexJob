# Architect Output Template

## Task: [Reference]

## Overview
- **What:** Feature description & scope
- **Why:** Rationale & alignment
- **Impact:** Systems affected & compatibility

## Classes to Create
### ClassName (public/internal sealed)
- **Location:** src/NexJob/...
- **Responsibility:** What it does
- **Dependencies:** IService (injected)

## Method Signatures
```csharp
public async Task<T> MethodNameAsync(Type1 p1, CancellationToken ct)
```
- **Purpose:** What it does
- **Notes:** Important behavior

## Execution Flow
1. Step 1 (Happy Path)
2. Check deadline (before execution)
3. Persist result
- **Edge Cases:** Expired skip, storage failure, handler throws

## State Transitions
- **Before:** Enqueued
- **After (Success):** Succeeded
- **After (Failure):** Failed/Dead-letter (attempt recorded)

## Architecture Compliance
- [ ] Storage is source of truth (all transitions persisted)
- [ ] No in-memory cache overrides storage
- [ ] Uses IJob or IJob<T>, no unnecessary DTOs
- [ ] Dispatcher remains stateless, scope isolated
- [ ] CancellationToken propagated
- [ ] Deadline checked before execution
- [ ] Dead-letter handler safe (exceptions swallowed)

## Risks & Mitigations
- **Storage Bypass:** All transitions via storage API
- **Deadline Logic:** Check after fetch, before execute
- **Dead-Letter Crash:** Try-catch in handler

## Testing Strategy
- **Unit:** Isolated behavior, state transitions
- **Integration:** Workflow with real storage
- **Reliability:** Crash recovery, concurrency, deadline

## Implementation Notes
- **New Files:** src/NexJob/..., tests/NexJob.Tests/...
- **Modified:** Dispatcher.cs, IStorageProvider.cs
- **Fixed:** Core model and invariants MUST remain unchanged.
