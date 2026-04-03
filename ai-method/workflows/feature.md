# Workflow: Feature

## Steps
1. Architecture (01-architect-mode) → design spec, no code
   - No unnecessary DTOs
   - Storage = source of truth
2. Execution (02-execution-mode) → code + tests
   - Sealed classes by default
   - XML docs for public APIs
   - Zero warnings
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
