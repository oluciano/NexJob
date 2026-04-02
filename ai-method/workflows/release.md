# Workflow: Release

**When:** Preparing code for production release

---

## Entry Criteria

- All features and fixes for this release are merged
- Code is stable and tested
- Version bump is justified
- CHANGELOG is prepared

---

## Steps

### 1. Pre-Release Validation (Release Mode)

**Use:** 04-release-mode.md

**Checks:**
- [ ] Build passes with zero warnings (`dotnet build --configuration Release`)
- [ ] All tests pass (unit, integration, reliability)
- [ ] All packages pack correctly (`dotnet pack`)
- [ ] Version in `Directory.Build.props` matches CHANGELOG
- [ ] No `[Unreleased]` content left

**Decision:** READY / NOT READY

If NOT READY → Fix issues, return to validation

### 2. Finalize Version

**Update:**
1. `Directory.Build.props` — set version number
2. `CHANGELOG.md` — move `[Unreleased]` to released version with date

**Format:**
```markdown
## [0.6.0] - 2026-04-02
### Added
- specific feature description

### Fixed
- specific bug fix

### Changed
- specific change description
```

### 3. Commit & Tag

**Git commands:**
```bash
git add Directory.Build.props CHANGELOG.md
git commit -m "chore: prepare v0.6.0 release"
git tag v0.6.0
git push origin main
git push origin v0.6.0
```

### 4. Package & Publish

**Pack locally:**
```bash
dotnet pack -c Release
```

**Verify packages:**
- NexJob.{version}.nupkg
- NexJob.Postgres.{version}.nupkg
- NexJob.SqlServer.{version}.nupkg
- NexJob.MongoDB.{version}.nupkg
- NexJob.Redis.{version}.nupkg
- NexJob.Dashboard.{version}.nupkg
- NexJob.Templates.{version}.nupkg

**Publish to NuGet:**
(Automated or manual, depending on CI/CD setup)

### 5. Announcement

- GitHub release page (if applicable)
- Project documentation updates
- Blog post (if significant release)

---

## Version Discipline

### Semantic Versioning

**MAJOR** (breaking change):
- Removing public APIs
- Changing fundamental behavior
- Not backward compatible

**MINOR** (feature addition):
- New public APIs
- New features
- Backward compatible

**PATCH** (bug fix):
- Bug fixes
- No new features
- No breaking changes

### Version Numbering

- Format: `v{MAJOR}.{MINOR}.{PATCH}`
- Example: `v0.6.0`
- Pre-release (if needed): `v0.6.0-rc1`

---

## CHANGELOG Rules

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
- specific feature

### Fixed
- specific bug fix

### Changed
- specific change
```

### Rules

- [ ] `[Unreleased]` at top
- [ ] Released version has date (YYYY-MM-DD)
- [ ] Categories: Added, Fixed, Changed (no others)
- [ ] Each entry is a complete sentence
- [ ] No vague descriptions

---

## Release Readiness Checklist

### Code Quality
- [ ] Build passes with 0 warnings (Release config)
- [ ] Zero StyleCop violations
- [ ] All public APIs documented
- [ ] No `NotImplementedException` anywhere

### Testing
- [ ] Unit tests pass
- [ ] Integration tests pass
- [ ] Reliability tests pass (if applicable)
- [ ] No flaky tests

### Documentation
- [ ] CHANGELOG updated completely
- [ ] API changes documented
- [ ] Breaking changes marked
- [ ] Version number consistent

### Versioning
- [ ] Semantic versioning justified
- [ ] `Directory.Build.props` matches CHANGELOG
- [ ] No `[Unreleased]` content left

### Packaging
- [ ] `dotnet pack` succeeds
- [ ] All NuGet packages present
- [ ] Package metadata correct (license, description, etc.)

---

## Post-Release Steps

1. **NuGet Publish** (automated or manual)
   ```bash
   dotnet nuget push "bin/Release/*.nupkg" --source https://api.nuget.org/v3/index.json --api-key [YOUR_API_KEY]
   ```

2. **GitHub Release** (if applicable)
   - Create release from tag
   - Copy CHANGELOG section as release notes
   - Attach release notes

3. **Project Updates**
   - Update README (if version-specific)
   - Update samples (if applicable)
   - Update documentation links

4. **Announcements**
   - Blog post (if significant)
   - GitHub discussion/announcement
   - Social media (if applicable)

---

## Blocking Issues (Cannot Release)

- Compiler warnings
- Test failures
- Version mismatch between files
- `[Unreleased]` content in CHANGELOG
- Pack errors
- Missing public API documentation

---

## Release Workflow Summary

```
1. Validate (Release Mode)
   ├─ Build passes? YES
   ├─ Tests pass? YES
   ├─ Version correct? YES
   └─ Ready? YES
   
2. Update Files
   ├─ Directory.Build.props (version)
   └─ CHANGELOG.md ([Unreleased] → [version])

3. Commit & Tag
   ├─ git commit -m "chore: prepare vX.Y.Z release"
   └─ git tag vX.Y.Z

4. Package
   └─ dotnet pack -c Release

5. Publish
   └─ push to NuGet

6. Announce
   └─ GitHub release, blog, etc.
```

---

## Exit Criteria

- [ ] Release validation PASSED
- [ ] Version updated in all files
- [ ] CHANGELOG finalized
- [ ] Git committed and tagged
- [ ] Packages created
- [ ] Published to NuGet
- [ ] Announcement complete
- [ ] Ready for users
