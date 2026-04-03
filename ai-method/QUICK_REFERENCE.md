# NexJob AI Operating Model — Quick Reference

**Always start here. Load full docs from main README.md**

---

## Which File Do I Need?

### I want to... ADD A FEATURE

```
Load: foundation-minimal + feature.md + architect-mode + execution-mode
Process: Design → Implement → Validate
Output: Code + tests, zero warnings
```

### I want to... FIX A BUG

```
Load: foundation-minimal + bugfix.md + execution-mode
Process: Root cause → Fix → Test
Output: Fix + regression test, zero warnings
```

### I want to... ADD TESTS

```
Load: foundation-minimal + test.md
Process: Define scope → Implement → Validate
Output: Real behavior, deterministic tests
```

### I want to... REFACTOR CODE

```
Load: foundation-minimal + refactor.md
Process: Define scope → Execute → Validate (no behavior change)
Output: Clearer code, all original tests pass
```

### I want to... VALIDATE CRITICAL SCENARIOS

```
Load: foundation-minimal + reliability.md
Process: Design → Implement → Test all providers
Output: Crash recovery, concurrency, deadline tests
```

### I want to... PREPARE A RELEASE

```
Load: release-mode + release.md
Process: Validate → Update version/changelog → Publish
Output: READY / NOT READY status
```

### I want to... VALIDATE CODE (Quality Check)

```
Load: validation-mode
Process: Run checklist
Output: PASS / FAIL with issues
```

---

## Core Rules (Never Forget)

**Storage is single source of truth**
- All state persisted
- Dispatcher is stateless
- No in-memory overrides

**Deadline enforced before execution**
- Checked after fetch, before invoke
- Expired jobs never execute
- Marked as Expired status

**Dead-letter handlers never crash**
- Exceptions swallowed, logged
- Never propagate to dispatcher

**All code must pass Release build**
- Zero compiler warnings (non-negotiable)
- Zero StyleCop violations
- Compile before commit: `dotnet build --configuration Release`

**StyleCop Critical Rules**
```csharp
public sealed class MyClass
{
    public string Property { get; set; }           // public first
    private static void Helper() { }               // static next
    private async Task PrivateAsync() { }          // instance last
}

var obj = new Class { Prop1 = x, Prop2 = y, };    // trailing comma
```

---

## Task Template (Copy This)

```markdown
## Task: [Title]

### Context
- What: [Description]
- Why: [Business reason]
- Scope: [What changes, what doesn't]

### Requirements
- [ ] Requirement 1
- [ ] Requirement 2

### Acceptance Criteria
- [ ] Criterion 1 (testable)
- [ ] Criterion 2 (testable)

### Success Definition
- [ ] Code compiles (zero warnings)
- [ ] Tests pass
- [ ] Architecture compliant
```

---

## Workflow at a Glance

### Feature Workflow
```
1. Architect (design, no code)
   ├─ Use: architect-mode.md + architect-output-template.md
   └─ Output: Specification (classes, methods, flow)

2. Execute (implement specification)
   ├─ Use: execution-mode.md + code
   └─ Output: Code + tests + execution-handoff

3. Validate (verify compliance)
   ├─ Use: validation-mode.md
   └─ Output: PASS / FAIL + validation-report
```

### Bug Fix Workflow
```
1. Analyze (root cause)
2. Execute (minimal fix + regression test)
3. Validate (verify fix works, no regressions)
```

### Release Workflow
```
1. Update files (version, changelog)
2. Validate (release-mode checklist)
3. Publish (if READY)
```

---

## Code Quality Checklist

**Before committing:**
- [ ] `dotnet build --configuration Release` (zero warnings)
- [ ] All tests pass locally
- [ ] Public APIs have XML docs (`///`)
- [ ] Classes are `sealed` by default
- [ ] StyleCop compliant (member order, trailing commas, etc.)
- [ ] No `.Result` or `.Wait()` (use async/await)
- [ ] CancellationToken propagated everywhere
- [ ] No `NotImplementedException`
- [ ] Storage is source of truth (all state persisted)

---

## Red Flags (Stop & Ask)

- Task is ambiguous or incomplete
- Multiple valid approaches exist
- Impact on other systems unclear
- Storage persistence requirements unclear
- Deadline interaction unclear
- Dead-letter behavior uncertain
- StyleCop compliance unclear
- Whether feature is in scope unclear

**Wrong code is worse than incomplete code.**

---

## File Structure

```
ai-method/
├── README.md ← START HERE (overview + usage examples)
├── QUICK_REFERENCE.md ← YOU ARE HERE
├── INTEGRATION.md (how it fits with existing docs)
├── REORGANIZATION_SUMMARY.md (what was consolidated)
│
├── core/
│   ├── 00-foundation-minimal.md (200 tokens, daily use)
│   └── 00-foundation-extended.md (1000 tokens, complex only)
│
├── modes/
│   ├── 01-architect-mode.md (design without code)
│   ├── 02-execution-mode.md (implement spec)
│   ├── 03-validation-mode.md (verify compliance)
│   └── 04-release-mode.md (production readiness)
│
├── workflows/
│   ├── feature.md
│   ├── bugfix.md
│   ├── test.md
│   ├── refactor.md
│   └── reliability.md
│
└── templates/
    ├── task-template.md
    ├── architect-output-template.md
    ├── execution-handoff-template.md
    └── validation-report-template.md
```

---

## Token Usage

| Scenario | Files Loaded | Tokens |
|----------|--------------|--------|
| Simple feature | minimal + workflow + mode | ~1500 |
| Complex feature | minimal + extended + architect + mode | ~3000 |
| Validation | validation-mode | ~800 |
| Release check | release-mode | ~600 |
| **Average** | **Composable mix** | **1500-2000** |

vs. Old system: 5000-8000 tokens (always all-or-nothing)

**Savings: 60-85% token reduction**

---

## Validation Checklist (Quick)

**Architecture:**
- [ ] Storage is source of truth?
- [ ] Dispatcher stateless?
- [ ] State transitions persisted?
- [ ] Deadline before execution?
- [ ] Dead-letter handlers safe?

**Code Quality:**
- [ ] Zero compiler warnings?
- [ ] StyleCop compliant?
- [ ] Public APIs documented?
- [ ] No `.Result` or `.Wait()`?
- [ ] CancellationToken propagated?

**Tests:**
- [ ] All pass?
- [ ] Deterministic?
- [ ] Real behavior (no mocks)?
- [ ] Coverage complete?

**If all ✓ → READY TO MERGE**

---

## One-Minute Read

**NexJob AI Operating Model:**
- 18 files, zero duplication
- 4 modes: architect, execute, validate, release
- 6 workflows: feature, bugfix, test, refactor, reliability, release
- 4 templates: standardized formats
- Foundation rules: core invariants (200 tokens)
- Token-efficient: load only what you need

**How to use:**
1. Read this quick reference
2. Choose your task type (feature, bugfix, test, etc.)
3. Load foundation-minimal + appropriate workflow + mode
4. Follow steps
5. Use templates for outputs
6. Validate with validation-mode
7. Ship

**Success:** Zero warnings, all tests pass, architecture compliant, first time.

---

## Need Help?

- **Project overview** → CLAUDE.md
- **How NexJob works** → ARCHITECTURE.md
- **Development setup** → CONTRIBUTING.md
- **Full system** → README.md
- **Architect a feature** → architect-mode.md
- **Implement a feature** → execution-mode.md
- **Validate code** → validation-mode.md
- **Release** → release-mode.md

---

**Version:** 1.0  
**Last Updated:** 2026-04-02  
**System:** NexJob AI Operating Model
ystem:** NexJob AI Operating Model
