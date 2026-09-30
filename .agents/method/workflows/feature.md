# Workflow: Feature

## Steps
1. Architecture (01-architect-mode) → design spec, no code
   - No unnecessary DTOs
   - Storage = source of truth
2. Execution (02-execution-mode) → code + tests
   - Sealed classes by default
   - XML docs for public APIs
   - Zero warnings
   - 3N matrix: N1 positive + N2 negative + N3 invalid input
   - No existing passing tests modified
3. Validation (03-validation-mode) → compliance check
   - StyleCop compliant
   - All tests pass

## Deliverables
- Feature code (src/...)
- Unit tests (tests/NexJob.Tests/...)
- Integration tests if storage or consistency changes
- CHANGELOG.md updated (Added section)

## Exit criteria
- [ ] Zero warnings (Release build)
- [ ] All tests pass
- [ ] Architecture compliant
- [ ] XML documentation complete
- [ ] StyleCop compliant
- [ ] CHANGELOG updated
- [ ] 3N matrix applied (N1 positive, N2 negative, N3 invalid input)
- [ ] No existing passing test was modified without explicit justification + comment
