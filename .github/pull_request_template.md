## Summary

<!-- What does this PR do? One or two sentences. -->

## Type of change

- [ ] Bug fix
- [ ] New feature
- [ ] New storage provider
- [ ] Refactor / cleanup
- [ ] Documentation
- [ ] Tests

## Testing Matrix (Must-Have)

- [ ] **Positive Scenarios:** (e.g., successful job execution, correct type resolution)
- [ ] **Negative Scenarios:** (e.g., invalid JSON, missing types, service failures)
- [ ] **Edge Cases:** (e.g., null inputs, empty strings, cancellation tokens)
- [ ] **Combinations:** (e.g., multiple filters + retry policy)

## Risk and rollback

- **Risk:** <!-- low / medium / high; high = core, storage, migrations, workflows -->
- **How to revert:** <!-- e.g. revert commit; migration is/is not reversible -->

## Checklist

- [ ] `dotnet build` passes with **0 warnings**
- [ ] `dotnet test` passes — no regressions
- [ ] New behaviour is covered by tests
- [ ] Public API has XML documentation (\`///\`)
- [ ] Commit messages follow Conventional Commits (`feat:`, `fix:`, `docs:`, etc.)

## Related issues

<!-- Closes #123 -->
