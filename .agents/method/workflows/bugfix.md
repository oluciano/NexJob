# Workflow: Bugfix

## Steps
1. Root cause analysis — where, why, invariant or logic?
2. Architecture (01-architect-mode) — only if multi-file changes
3. Execution (02-execution-mode) → minimal fix + regression test
   - Minimal change (no refactoring)
   - Zero warnings
   - Regression test must cover 3N: positive, negative, invalid input
   - Never modify existing tests to pass — fix production code
4. Validation (03-validation-mode) → compliance check
   - StyleCop compliant
   - All tests pass

## Deliverables
- Fix code (minimal set)
- Regression test (reproduces bug, passes with fix)
- CHANGELOG.md updated (Fixed section)

## Exit criteria
- [ ] Root cause documented
- [ ] Regression test passes (N1 positive, N2 negative, N3 invalid input)
- [ ] All tests pass
- [ ] Zero warnings (Release build)
- [ ] StyleCop compliant
- [ ] CHANGELOG updated
- [ ] No existing passing test was modified without justification
