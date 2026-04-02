# Workflow: Feature Implementation

**When:** Adding new functionality to NexJob

---

## Entry Criteria

- Feature is documented (issue or specification)
- Scope is clear and bounded
- Architectural impact understood

---

## Steps

### 1. Architecture (Architect Mode)

**Use:** 01-architect-mode.md

**Output:**
- Design specification (no code)
- Classes and methods to create
- Execution flow
- What won't change

**Validation:**
- Respects Job model
- Doesn't introduce unnecessary DTOs
- Storage remains source of truth
- New invariants needed? (list them)

### 2. Implementation (Execution Mode)

**Use:** 02-execution-mode.md

**Output:**
- Code (production-ready)
- Tests (new test files)
- Zero warnings at compile time

**Deliverables:**
1. Feature code (src/NexJob/...)
2. Unit tests (tests/NexJob.Tests/...)
3. Integration tests (if applicable)

### 3. Validation (Validation Mode)

**Use:** 03-validation-mode.md

**Checks:**
- [ ] Compiler warnings? (must be zero)
- [ ] StyleCop violations? (must be zero)
- [ ] All tests pass?
- [ ] Architecture compliance?
- [ ] Storage persistence correct?
- [ ] Deadline behavior correct?

### 4. Code Review

**Before merge, verify:**
- [ ] Feature works as designed
- [ ] Tests cover the scenario
- [ ] No unrelated code changed
- [ ] Public APIs documented
- [ ] CHANGELOG updated (Added section)

---

## Output Requirements

**Code:**
- Production-ready (no placeholders)
- Zero warnings
- Sealed classes
- XML docs for public APIs
- StyleCop compliant

**Tests:**
- Real behavior (no mocks unless necessary)
- Deterministic
- Cover happy path and edge cases
- All pass locally

**Documentation:**
- CHANGELOG.md updated
- Public API documented
- Architecture impact noted (if relevant)

---

## Common Patterns

### Adding a Storage Provider

1. Create `src/NexJob.{Name}/`
2. Implement `IStorageProvider`
3. Ensure atomic dequeue strategy
4. Add DI extension
5. Add integration tests
6. Update CONTRIBUTING.md

### Adding a Job Attribute

1. Create attribute class (sealed, with docs)
2. Update dispatcher to respect it
3. Add tests for attribute behavior
4. Document interaction with retry/deadline

### Adding Observability

1. Create metrics/signals (name: `nexjob.*`)
2. Ensure observable state is persisted
3. Add tests for metric accuracy
4. Document new signals

---

## Exit Criteria

- [ ] All tests pass (unit, integration, reliability)
- [ ] Zero compiler warnings
- [ ] StyleCop compliant
- [ ] Architecture validated
- [ ] CHANGELOG updated
- [ ] Ready to merge
