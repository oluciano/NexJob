# Workflow: Bugfix

## Steps
1. Root cause analysis — where, why, invariant or logic?
2. Architecture (01-architect-mode) — only if multi-file changes
3. Execution (02-execution-mode) → minimal fix + regression test
   - Minimal change (no refactoring)
   - Zero warnings
4. Validation (03-validation-mode) → compliance check
   - StyleCop compliant
   - All tests pass

## Deliverables
- Fix code (minimal set)
- Regression test (reproduces bug, passes with fix)
- CHANGELOG.md updated (Fixed section)

## Exit criteria
- [ ] Root cause documented
- [ ] Regression test passes
- [ ] All tests pass
- [ ] Zero warnings (Release build)
- [ ] StyleCop compliant
- [ ] CHANGELOG updated
