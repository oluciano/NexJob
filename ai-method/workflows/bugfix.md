# Workflow: Bug Fix

**When:** Fixing a reported issue or regression

---

## Entry Criteria

- Bug is clearly described
- Root cause understood (or will be investigated)
- Scope bounded to the bug

---

## Steps

### 1. Root Cause Analysis

**Investigate:**
- Where does the bug manifest?
- What code path is affected?
- Why does the bug occur?
- Is it an invariant violation or implementation error?

**Questions to answer:**
- Does it involve deadline enforcement?
- Does it involve retry/dead-letter?
- Does it involve state persistence?
- Does it violate an invariant?
- Is it architecture compliance or logic error?

### 2. Architecture (Architect Mode - if complex)

**Use:** 01-architect-mode.md (only if multi-file changes)

**Output:**
- Root cause explained
- Fix design (if not obvious)
- Files that will change
- What will NOT change

**Validation:**
- Fix doesn't introduce new issues
- No unrelated changes needed
- Architecture still intact

### 3. Implementation (Execution Mode)

**Use:** 02-execution-mode.md

**Output:**
- Fix code
- Regression test (prevents recurrence)
- Modified code only (no refactoring)

**Deliverables:**
1. Fix code (minimal change)
2. Regression test (new test file)
3. Zero warnings at compile time

---

## Test Requirements

### Regression Test

**Must include:**
- Scenario that triggers the bug
- Expected behavior after fix
- Assertion that bug doesn't recur

**Example structure:**
```csharp
// Given: Job with [condition that triggers bug]
// When: [action]
// Then: [expected behavior after fix]
```

### Coverage

- Bug scenario covered?
- Edge cases covered?
- Related scenarios pass?

---

## Code Review Checklist

Before merge:
- [ ] Root cause is clear
- [ ] Fix is minimal (only necessary changes)
- [ ] Regression test passes
- [ ] All tests pass
- [ ] No new warnings
- [ ] Unrelated code untouched
- [ ] CHANGELOG updated (Fixed section)

---

## Validation (Validation Mode)

**Use:** 03-validation-mode.md

**Checks:**
- [ ] Compiler warnings? (must be zero)
- [ ] StyleCop violations? (must be zero)
- [ ] All tests pass (including regression)?
- [ ] Root cause actually fixed?
- [ ] No unrelated changes?
- [ ] No architectural violations?

---

## Output Requirements

**Fix Code:**
- Minimal change set
- No refactoring
- No unrelated improvements
- Production-ready
- Zero warnings

**Regression Test:**
- Real behavior (no mocks unless necessary)
- Deterministic
- Reproduces the bug (before fix)
- Passes with fix
- Clear test name describing the bug

**Documentation:**
- CHANGELOG.md updated (Fixed section)
- Root cause comment (if non-obvious)

---

## Example Workflow

1. **Analyze:** Bug is in deadline enforcement — check happens after execution instead of before
2. **Fix:** Move deadline check to correct location (fetch → deadline check → execute)
3. **Test:** Add regression test that verifies expired jobs don't execute
4. **Validate:** All tests pass, zero warnings, deadline behavior correct
5. **Merge:** Ready to ship

---

## Exit Criteria

- [ ] Root cause documented
- [ ] Bug is fixed
- [ ] Regression test passes
- [ ] All tests pass
- [ ] Zero compiler warnings
- [ ] StyleCop compliant
- [ ] CHANGELOG updated
- [ ] Ready to merge
