# NexJob AI Operating Model — Entry Points

**Start here. This file points you to the right place.**

---

## I'm Starting a New Task

→ Read [ai-method/QUICK_REFERENCE.md](ai-method/QUICK_REFERENCE.md) (1 minute)

Then:
- Choose your task type (feature, bugfix, test, refactor, reliability, release)
- Load the relevant workflow from `ai-method/workflows/`
- Follow the steps

---

## I Want to Understand the System

→ Start with [ai-method/README.md](ai-method/README.md) (5 minutes)

Covers:
- Purpose and structure
- How to use (quick start)
- Token optimization strategy
- Usage examples

---

## I Want Quick Answers

→ Use [ai-method/QUICK_REFERENCE.md](ai-method/QUICK_REFERENCE.md)

Has:
- Which file to load for your task
- Core rules (never forget)
- Workflow at a glance
- Code quality checklist
- Red flags (stop and ask)
- File structure
- Token usage comparison

---

## I'm Designing a Feature (Architect Mode)

→ Read [ai-method/modes/01-architect-mode.md](ai-method/modes/01-architect-mode.md)

Then use [ai-method/templates/architect-output-template.md](ai-method/templates/architect-output-template.md)

---

## I'm Implementing a Feature (Execution Mode)

→ Read [ai-method/modes/02-execution-mode.md](ai-method/modes/02-execution-mode.md)

First load:
1. [ai-method/core/00-foundation-minimal.md](ai-method/core/00-foundation-minimal.md)
2. [ai-method/modes/02-execution-mode.md](ai-method/modes/02-execution-mode.md)

Then output using [ai-method/templates/execution-handoff-template.md](ai-method/templates/execution-handoff-template.md)

---

## I'm Validating Code (Validation Mode)

→ Read [ai-method/modes/03-validation-mode.md](ai-method/modes/03-validation-mode.md)

Output results using [ai-method/templates/validation-report-template.md](ai-method/templates/validation-report-template.md)

---

## I'm Preparing a Release (Release Mode)

→ Read [ai-method/modes/04-release-mode.md](ai-method/modes/04-release-mode.md)

Covers:
- Release checklist
- Version discipline
- CHANGELOG rules
- Package validation
- Release decision tree

---

## I'm Adding a Feature

→ Follow [ai-method/workflows/feature.md](ai-method/workflows/feature.md)

Process:
1. Architecture (mode 01)
2. Implementation (mode 02)
3. Validation (mode 03)

---

## I'm Fixing a Bug

→ Follow [ai-method/workflows/bugfix.md](ai-method/workflows/bugfix.md)

Process:
1. Root cause analysis
2. Minimal fix implementation
3. Regression test
4. Validation

---

## I'm Adding Tests

→ Follow [ai-method/workflows/test.md](ai-method/workflows/test.md)

Covers:
- Unit tests
- Integration tests
- Reliability tests

---

## I'm Refactoring Code

→ Follow [ai-method/workflows/refactor.md](ai-method/workflows/refactor.md)

Key constraint: **No behavior change**

---

## I'm Testing Critical Scenarios (Reliability)

→ Follow [ai-method/workflows/reliability.md](ai-method/workflows/reliability.md)

Covers:
- Crash recovery
- Concurrency
- Deadline enforcement
- Wake-up latency
- All storage providers

---

## I'm Preparing a Release

→ Follow [ai-method/workflows/release.md](ai-method/workflows/release.md)

Process:
1. Pre-release validation
2. Finalize version & changelog
3. Commit & tag
4. Package & publish

---

## I Need to Understand Core Rules

→ Read [ai-method/core/00-foundation-minimal.md](ai-method/core/00-foundation-minimal.md)

Essential (load for every task):
- Core invariants
- Job model
- State transitions
- Execution constraints
- Code quality rules
- StyleCop rules

---

## I Need Extended Architecture Details

→ Read [ai-method/core/00-foundation-extended.md](ai-method/core/00-foundation-extended.md)

Covers (load only when needed):
- Full architectural rules
- Edge cases
- Detailed constraints
- Testing strategy
- Architecture compliance checklist

---

## I Want to Understand How Everything Fits Together

→ Read [ai-method/INTEGRATION.md](ai-method/INTEGRATION.md)

Explains:
- How ai-method/ connects to existing docs
- Information flow
- Documentation hierarchy
- Reference chains
- No redundancy principle

---

## I Want to Know What Was Changed

→ Read [ai-method/REORGANIZATION_SUMMARY.md](ai-method/REORGANIZATION_SUMMARY.md)

Covers:
- What was consolidated
- Key improvements
- Feature completeness
- Success indicators

---

## I Need to Specify Work Clearly

→ Use [ai-method/templates/task-template.md](ai-method/templates/task-template.md)

Template for:
- Context
- Requirements
- Acceptance criteria
- Success definition

---

## Quick Navigation Map

```
New Task?
├─ START → QUICK_REFERENCE.md
└─ Choose: feature / bugfix / test / refactor / reliability / release
           ↓
         workflows/{type}.md
           ↓
         Follow steps
           ↓
         Load foundation-minimal + mode
           ↓
         Execute → Output → Validate
```

## Core Files (Always Know These)

- [ai-method/README.md](ai-method/README.md) — System overview
- [ai-method/QUICK_REFERENCE.md](ai-method/QUICK_REFERENCE.md) — Quick lookup
- [ai-method/core/00-foundation-minimal.md](ai-method/core/00-foundation-minimal.md) — Core rules (200 tokens)

## Mode Files (Choose One for Your Step)

- [ai-method/modes/01-architect-mode.md](ai-method/modes/01-architect-mode.md) — Design without code
- [ai-method/modes/02-execution-mode.md](ai-method/modes/02-execution-mode.md) — Implement spec
- [ai-method/modes/03-validation-mode.md](ai-method/modes/03-validation-mode.md) — Verify compliance
- [ai-method/modes/04-release-mode.md](ai-method/modes/04-release-mode.md) — Production readiness

## Workflow Files (Choose One for Your Task Type)

- [ai-method/workflows/feature.md](ai-method/workflows/feature.md)
- [ai-method/workflows/bugfix.md](ai-method/workflows/bugfix.md)
- [ai-method/workflows/test.md](ai-method/workflows/test.md)
- [ai-method/workflows/refactor.md](ai-method/workflows/refactor.md)
- [ai-method/workflows/reliability.md](ai-method/workflows/reliability.md)
- [ai-method/workflows/release.md](ai-method/workflows/release.md)

## Template Files (Use for Outputs)

- [ai-method/templates/task-template.md](ai-method/templates/task-template.md) — Specify work
- [ai-method/templates/architect-output-template.md](ai-method/templates/architect-output-template.md) — Document designs
- [ai-method/templates/execution-handoff-template.md](ai-method/templates/execution-handoff-template.md) — Communicate results
- [ai-method/templates/validation-report-template.md](ai-method/templates/validation-report-template.md) — Report validation

---

## TL;DR (30 Seconds)

1. **Quick Reference:** [QUICK_REFERENCE.md](ai-method/QUICK_REFERENCE.md)
2. **Full System:** [README.md](ai-method/README.md)
3. **Choose Task:** [workflows/](ai-method/workflows/)
4. **Load Foundation:** [core/00-foundation-minimal.md](ai-method/core/00-foundation-minimal.md)
5. **Choose Mode:** [modes/](ai-method/modes/)
6. **Output Template:** [templates/](ai-method/templates/)
7. **Execute → Ship**

---

**Version:** 1.0  
**Date:** 2026-04-02  
**System:** NexJob AI Operating Model
