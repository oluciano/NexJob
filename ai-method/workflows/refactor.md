# Workflow: Refactoring

**When:** Improving code structure, clarity, or maintainability without changing behavior

---

## Entry Criteria

- Scope is bounded (specific class, method, area)
- Goal is clear (clarity, structure, performance)
- No behavior change required
- Refactoring is beneficial (not premature)

---

## Steps

### 1. Define Scope & Goals

**Determine:**
- What code is being refactored?
- Why? (clarity, duplication, structure, performance)
- What should NOT change?
- How will you verify no behavior changed?

**Questions:**
- Is this refactoring necessary now? (avoid premature optimization)
- Will it make the code significantly clearer?
- Will all tests still pass?
- Is the scope truly isolated?

### 2. Implementation (Execution Mode)

**Use:** 02-execution-mode.md

**Key constraint:** NO BEHAVIOR CHANGE

**Output:**
- Refactored code only
- All tests still pass
- Zero warnings
- No new files (unless structure requires)

**Deliverables:**
1. Refactored code
2. All tests pass (no changes needed)
3. Zero compiler warnings

---

## Refactoring Constraints

- [ ] No behavior change
- [ ] No new features introduced
- [ ] No public API changes (unless necessary for clarity)
- [ ] No architecture changes
- [ ] No performance trade-offs (unless explicit goal)
- [ ] Existing tests still pass (no modifications)

---

## Validation (Validation Mode)

**Use:** 03-validation-mode.md

**Special checks for refactoring:**
- [ ] All original tests pass? (no modifications to tests)
- [ ] Behavior is identical? (same code paths, same logic)
- [ ] No architectural changes? (same patterns, same structure)
- [ ] Clarity improved? (subjective, but reviewable)
- [ ] Zero compiler warnings?
- [ ] StyleCop compliant?

---

## Code Review Checklist

Before merge:
- [ ] Behavior is unchanged
- [ ] All tests pass (unmodified)
- [ ] Code is clearer
- [ ] No unrelated changes
- [ ] Zero warnings
- [ ] Refactoring is justified (not premature)

---

## Output Requirements

**Refactored Code:**
- Same behavior as before
- Clearer structure or logic
- Improved naming (if applicable)
- Reduced duplication (if applicable)
- Production-ready

**Tests:**
- No modifications (should all pass as-is)
- If test modification necessary → STOP and ask

**Documentation:**
- Updated comments (if affected)
- No CHANGELOG entry (refactoring, not feature/fix)

---

## Refactoring Examples

### Extract Method (Good)
Before:
```csharp
var now = DateTime.UtcNow;
if (job.ExpiresAt < now) return JobStatus.Expired;
```

After:
```csharp
private static bool IsExpired(Job job) => job.ExpiresAt < DateTime.UtcNow;
if (IsExpired(job)) return JobStatus.Expired;
```

### Rename for Clarity (Good)
Before: `_sig` → After: `_wakeUpSignal`

### Extract Class (Be Cautious)
Only if reducing complexity and not changing behavior.
Verify all tests still pass without modification.

### Remove Duplication (Good)
If three similar code paths can consolidate without obscuring intent.

---

## Red Flags (Stop and Reconsider)

- Test modifications needed
- Public API changes
- Behavior becomes less obvious
- Performance trade-off not discussed
- Architecture changes creeping in
- Over-engineering introduced
- Tests start failing

---

## Exit Criteria

- [ ] Scope is bounded
- [ ] Behavior unchanged
- [ ] All original tests pass (unmodified)
- [ ] Code is clearer
- [ ] Zero compiler warnings
- [ ] StyleCop compliant
- [ ] Ready to merge

---

## Non-Refactoring Examples (Don't do)

These look like refactoring but violate the contract:

- "Simplify deadline logic" → may change behavior
- "Optimize retry evaluation" → may change behavior
- "Clean up dead-letter handling" → risky (exceptions involved)
- "Improve storage caching" → changes behavior
- "Extract common configuration" → new abstraction, new behavior surface

**When in doubt: ask if behavior could possibly change.**
