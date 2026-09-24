---
name: nexjob-release
description: >-
  Executes the official NexJob release cycle: NuGet/tag version check, automated SemVer detection (Major/Minor/Patch)
  with user confirmation, Code vs Documentation Truth Gate (auto-updating Wiki and package READMEs directly on develop without modifying code),
  quality & packaging verification gate, and opening the release PR from develop to main.
---

# NexJob Release Cycle Skill

This skill enforces an audit-proof, disciplined, and automated workflow for releasing **NexJob** to NuGet and GitHub.
It guarantees that code and documentation (Wiki + Package READMEs) are strictly aligned before any release reaches `main`.

---

## Non-Negotiable Invariants

1. **Truth Invariant (Code vs Documentation):** The code is the ultimate source of truth. If Wiki or READMEs are outdated, missing features, or describe contradictory behavior, **fix the documentation immediately** — **NEVER** modify production code during the release cycle.
2. **Direct Doc Sync on `develop`:** Documentation fixes (Wiki in `docs/wiki/` and Package READMEs in `src/*/README.md`) can be committed directly to `develop` without a separate PR before opening the release PR. (Samples are excluded from release sync).
3. **NuGet & Git Tag Alignment:** Always query `https://api.nuget.org/v3-flatcontainer/nexjob/index.json` and local `git tag` to ensure version monotonicity. Never reuse an existing tag or version.
4. **SemVer Detection & User Confirmation:** Automatically inspect commits and `CHANGELOG.md` since the last tag to determine if the release is **MAJOR**, **MINOR**, or **PATCH**, propose the next version number, and **ask the user to confirm or override**.
5. **Quality Gate:** Release builds must pass with **0 warnings** (`TreatWarningsAsErrors = true`), all unit tests must pass, and `dotnet pack` must produce valid `.nupkg` packages.
6. **Branch Strategy:** Releases are ALWAYS merged via PR from `develop` into `main`. Never push directly to `main`. Never manually create git tags (CI creates the tag upon merge to `main`).

---

## The 5-Phase Release Flow

```
[Phase 1: Version Auditing & SemVer Detection]
                     │
                     ▼
[Phase 2: User Confirmation of Version]
                     │
                     ▼
[Phase 3: Code vs Documentation Truth Gate (Auto-Sync to develop)]
                     │
                     ▼
[Phase 4: Pre-Release Quality & Packaging Verification]
                     │
                     ▼
[Phase 5: Version Bump, Changelog & Release PR to main]
```

---

## Phase 1: Version Auditing & SemVer Detection

1. **Query NuGet & Git Tags:**
   ```bash
   curl -s "https://api.nuget.org/v3-flatcontainer/nexjob/index.json"
   git tag -l -n1
   ```
2. **Inspect Changes Since Last Tag:**
   ```bash
   git log $(git describe --tags --abbrev=0)..HEAD --oneline
   ```
3. **Determine SemVer Category:**
   - **MAJOR (vX.0.0):** Breaking changes to public interfaces (`IJob`, `IJobStorage`, public constructors, removed/renamed public APIs).
   - **MINOR (vX.Y.0):** New backwards-compatible features, new dashboard pages, new triggers, new options/delegates, new extension methods.
   - **PATCH (vX.Y.Z):** Bug fixes, performance optimizations, documentation-only changes, zero new features.
4. Formulate the proposed version (e.g. if NuGet has `5.3.0` and new dashboard features are added, propose `v5.4.0`).

---

## Phase 2: User Confirmation of Version

Always ask the user explicitly before proceeding:
- Present the detected changes.
- Explain whether it was classified as **MAJOR**, **MINOR**, or **PATCH**.
- Propose the next version (e.g. `v5.4.0`).
- Wait for user confirmation or alternative version request.

---

## Phase 3: Code vs Documentation Truth Gate (Auto-Fix on `develop`)

> **CRITICAL RULE:** **NEVER alter production code (`src/**/*.cs`) to match docs.** Always update the docs to match the real code!

1. **Audit Package READMEs (`src/*/README.md`):**
   - Identify packages with code changes in this release (e.g. `src/NexJob.Dashboard/README.md`, `src/NexJob.Kafka/README.md`).
   - Check if options, methods, endpoints, themes, or UI features are accurately described.
   - If outdated or missing info, **edit the README immediately**.
2. **Audit Wiki (`docs/wiki/`):**
   - Check relevant wiki pages (e.g. `10-Dashboard.md`, `19-Triggers.md`, `20-Kafka.md`, `Home.md`).
   - Check for obsolete signatures, missing screenshots/explanations of new UI screens, or omitted configuration parameters.
   - If outdated, **edit the Wiki pages immediately**.
3. **Commit & Push Docs Direct to `develop`:**
   - Commit all doc and wiki updates with message: `docs: synchronize wiki and package readmes for vX.Y.Z release`.
   - Push directly to `origin/develop`.

---

## Phase 4: Pre-Release Quality & Packaging Verification

Execute the non-negotiable compilation and packaging gate:

```bash
# 1. Zero compiler warnings in Release
dotnet build -c Release

# 2. All unit tests pass
dotnet test tests/NexJob.Tests/ -c Release --no-build

# 3. Formatting verified
dotnet format --verify-no-changes

# 4. Packaging validation (assert all nupkgs build cleanly)
dotnet pack -c Release
```

If any check fails: **STOP**. Fix the issue, verify again, and only continue when 100% green.

---

## Phase 5: Version Bump, Changelog & Release PR to `main`

1. **Update `Directory.Build.props`:**
   - Set `<VersionPrefix>X.Y.Z</VersionPrefix>`.
2. **Update `CHANGELOG.md`:**
   - Move the content under `## [Unreleased]` to a new release header:
     ```markdown
     ## [X.Y.Z] - YYYY-MM-DD
     ```
   - Leave a blank `## [Unreleased]` section at the top.
3. **Commit on `develop`:**
   ```bash
   git add Directory.Build.props CHANGELOG.md
   git commit -m "chore(release): prepare vX.Y.Z"
   git push origin develop
   ```
4. **Open Release PR (`develop` ➔ `main`):**
   ```bash
   gh pr create \
     --base main \
     --head develop \
     --title "release: vX.Y.Z" \
     --body "## Summary
   Release vX.Y.Z of NexJob.
   
   ### Key Changes
   <Brief summary from CHANGELOG>
   
   ### Checklist
   - [x] Version bump in Directory.Build.props (vX.Y.Z)
   - [x] CHANGELOG.md updated
   - [x] Code vs Documentation Truth Gate passed (Wiki & Package READMEs synchronized)
   - [x] Release build 0 warnings
   - [x] Unit tests 100% passing
   - [x] NuGet package creation verified"
   ```
5. **Handoff:** Notify the user that the Release PR is open and ready to merge. Once merged to `main`, GitHub Actions automatically creates the git tag, GitHub Release, and publishes to NuGet.org.
