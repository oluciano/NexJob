# AI Mode: Release

**Role:** Validate and prepare code for production release.

---

## Release Checklist

### Pre-Release Validation

- [ ] Build passes with zero warnings? (`dotnet build --configuration Release`)
- [ ] All tests pass? (unit, integration, reliability)
- [ ] All packages pack correctly? (`dotnet pack`)
- [ ] Version in `Directory.Build.props` matches latest CHANGELOG entry?
- [ ] No `[Unreleased]` content left in CHANGELOG?
- [ ] Git working tree clean? (all changes committed)

### Code Quality

- [ ] Zero compiler warnings (Release build)
- [ ] Zero StyleCop violations
- [ ] All public APIs have XML documentation
- [ ] No `NotImplementedException` anywhere
- [ ] No placeholder or incomplete implementations

### Tests

- [ ] Unit tests pass (`dotnet test tests/NexJob.Tests/`)
- [ ] Integration tests pass (requires Docker)
- [ ] Reliability tests pass (`dotnet test tests/NexJob.ReliabilityTests.Distributed -c Release`)
- [ ] No flaky tests or timing-dependent assertions

### Documentation

- [ ] CHANGELOG.md updated with all changes
- [ ] Release notes prepared (if external communication needed)
- [ ] API changes documented (if public surface changed)
- [ ] Breaking changes clearly marked (if applicable)

### Versioning

- [ ] Semantic versioning respected (MAJOR.MINOR.PATCH)
- [ ] Version bump justifiable (breaking, feature, patch)
- [ ] Version number consistent across:
  - `Directory.Build.props`
  - CHANGELOG.md
  - Git tags (if pushing to GitHub)

### Commits & History

- [ ] Commit messages follow Conventional Commits
- [ ] All PRs merged (if using PR workflow)
- [ ] No merge conflicts left
- [ ] Git history is clean (no force pushes)

---

## Version Discipline

### Semantic Versioning Rules

**MAJOR** (breaking change):
- Removing public APIs
- Changing fundamental behavior
- Incompatible with prior version

**MINOR** (feature addition):
- New public APIs
- New features
- Backward compatible

**PATCH** (bug fix):
- Bug fixes
- Documentation updates
- No new features, no breaking changes

### Version Format

```
v{MAJOR}.{MINOR}.{PATCH}
Example: v0.6.0
```

---

## Changelog Rules

### Format

```markdown
## [Unreleased]
### Added
- description of new features

### Fixed
- description of bug fixes

### Changed
- description of changes

## [0.6.0] - 2026-04-02
### Added
- specific feature description

### Fixed
- specific bug fix

### Changed
- specific change description
```

### Rules

- [ ] `[Unreleased]` section exists at top?
- [ ] Released version has date (YYYY-MM-DD)?
- [ ] Categories: Added, Fixed, Changed (no others)
- [ ] Each entry is a complete sentence
- [ ] No placeholders or vague descriptions

---

## Package Validation

### NuGet Packages to Create

- `NexJob` (core library)
- `NexJob.Postgres`
- `NexJob.SqlServer`
- `NexJob.MongoDB`
- `NexJob.Redis`
- `NexJob.Dashboard` (standalone)
- `NexJob.Templates` (CLI scaffolding)

### Per-Package Checks

- [ ] Package ID correct?
- [ ] Version number updated?
- [ ] Dependencies specified correctly?
- [ ] License specified (MIT)?
- [ ] Description clear?
- [ ] Icon/metadata present?
- [ ] `dotnet pack` succeeds?

### Command to Validate All

```bash
dotnet pack -c Release
```

---

## Release Readiness Decision Tree

```
Build passes with 0 warnings?
  NO  → FIX BUILD → Return "NOT READY"
  YES → Continue

All tests pass?
  NO  → FIX TESTS → Return "NOT READY"
  YES → Continue

Version matches CHANGELOG?
  NO  → UPDATE VERSION → Return "NOT READY"
  YES → Continue

No [Unreleased] content?
  NO  → MOVE TO RELEASED SECTION → Return "NOT READY"
  YES → Continue

All packages pack correctly?
  NO  → DEBUG PACK ERROR → Return "NOT READY"
  YES → Continue

→ Return "READY FOR RELEASE"
```

---

## Release Output

Report:

**Status:** READY / NOT READY

**Issues Found:** (list of blockers, if any)

**Version:** (what will be released)

**Components Packaged:**
- NexJob v{version}
- NexJob.Postgres v{version}
- NexJob.SqlServer v{version}
- NexJob.MongoDB v{version}
- NexJob.Redis v{version}
- NexJob.Dashboard v{version}
- NexJob.Templates v{version}

**Next Steps:** (if READY: publish to NuGet, tag in Git, announce)

---

## Post-Release Steps (Manual)

After validation passes:

1. Push to NuGet (if using automated publish)
2. Create git tag: `git tag v{VERSION}`
3. Push tags: `git push origin v{VERSION}`
4. Create GitHub release (if applicable)
5. Update project announcements/blog (if applicable)

---

## Blocking Issues

Any of these prevent release:

- Compiler warnings
- Test failures
- Version mismatch
- `[Unreleased]` content in CHANGELOG
- Pack errors
- Missing public API documentation
- Incomplete CHANGELOG entries
