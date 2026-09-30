# Workflow: Refactor

## Constraint: NO BEHAVIOR CHANGE

## Steps
1. Define scope — what changes, what doesn't, how to verify
2. Execution (02-execution-mode) → refactored code only
   - Improved clarity or structure
   - Zero warnings
3. Validation (03-validation-mode) → behavior unchanged check
   - All original tests pass unmodified
   - StyleCop compliant

## Stop conditions
- Tests need modification → STOP, ask
- Public API changes → STOP, ask
- Behavior becomes less obvious → STOP, ask
- Architecture changes creeping in → STOP, ask
- Tempted to rewrite a test to make it pass → STOP, this is a behavior change

## Exit criteria
- [ ] All original tests pass (unmodified)
- [ ] Behavior identical
- [ ] Zero warnings (Release build)
- [ ] StyleCop compliant
- [ ] No CHANGELOG entry needed
