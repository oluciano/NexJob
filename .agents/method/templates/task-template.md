# Task Template

Use this to clearly specify work for an AI execution engine.

---

## Title

**[Descriptive task title]**

---

## Context

**What is this task?**
- Feature, bugfix, test, refactoring, reliability test, or release

**Why are we doing this?**
- Business reason, technical debt, reliability requirement, etc.

**Scope:**
- What will change
- What will NOT change

---

## Requirements

**Must do:**
- Explicit requirement 1
- Explicit requirement 2
- Explicit requirement 3

**Must NOT do:**
- Constraint 1
- Constraint 2

**Assumptions:**
- Assumption 1
- Assumption 2

---

## Acceptance Criteria

- [ ] Criterion 1 (testable, observable)
- [ ] Criterion 2
- [ ] Criterion 3

---

## Workflow

**Use this workflow:** [feature | bugfix | test | refactor | reliability | release]

**Reference architecture:** ARCHITECTURE.md, CLAUDE.md, foundation rules

---

## Deliverables

1. Code (if applicable)
   - File path: src/NexJob/...
   - Production-ready

2. Tests (if applicable)
   - File path: tests/NexJob.Tests/... (or appropriate)
   - All pass

3. Documentation (if applicable)
   - CHANGELOG update (Added / Fixed / Changed)
   - Public API docs (if new public APIs)

---

## Success Definition

Task is DONE when:
- [ ] All acceptance criteria met
- [ ] All deliverables complete
- [ ] Zero compiler warnings
- [ ] All tests pass
- [ ] Code reviewed and approved
- [ ] Ready to merge
