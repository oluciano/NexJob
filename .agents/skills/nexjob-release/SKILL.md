---
name: nexjob-release
description: >-
  Executes the streamlined, audit-proof NexJob release cycle with zero prompt fatigue:
  autonomously calculates SemVer, synchronizes Wiki and CHANGELOG via DocsTruth, validates
  Release build and unit tests, and presents a single decision gate before opening the Release PR to main.
---

# NexJob Release Cycle (Zero Prompt Fatigue)

This skill executes an automated, streamlined, and audit-proof release cycle for **NexJob**.
It eliminates confirmation fatigue by running all pre-flight calculations, doc sync, and quality checks **autonomously in one continuous pass**, pausing for user confirmation **only once** before opening the Release PR.

---

## Non-Negotiable Release Invariants

1. **Single User Gate:** Never prompt the user for intermediate steps (e.g., checking tags, running tests, syncing docs). Run all pre-flight stages autonomously and pause only to present the final release summary and confirm PR creation.
2. **Code is the Source of Truth:** Never alter production code (`src/**/*.cs`) to match documentation. Always update docs to match the code.
3. **SemVer Milestone Guard:**
   - **PATCH (`vX.Y.Z`):** Strictly bug fixes and docs. Zero new features, zero new public APIs.
   - **MINOR (`vX.Y.0`):** New backwards-compatible features, triggers, providers, options.
   - **MAJOR (`vX.0.0`):** Breaking public contract or storage format changes.
4. **Merge Commit Requirement:** Release PRs from `develop` into `main` MUST be merged using **"Create a merge commit"** (`--no-ff`), never Squash or Rebase, preserving git history and preventing phantom commit drift.
5. **Quality Floor:** Zero compiler warnings (`TreatWarningsAsErrors = true`), all unit tests pass, and packages pack cleanly.

---

## The 3-Step Streamlined Flow

```
┌────────────────────────────────────────────────────────┐
│ STEP 1: Autonomous Pre-Flight & Verification           │
│ - SemVer detection & NuGet check                       │
│ - DocsTruth & CHANGELOG ↔ Diff Cross-Check             │
│ - Zero-warning build, unit tests & dotnet pack         │
│ - Automatic Directory.Build.props & CHANGELOG bump     │
└──────────────────────────┬─────────────────────────────┘
                           │ (Silent execution)
                           ▼
┌────────────────────────────────────────────────────────┐
│ STEP 2: The Single Human Decision Gate                 │
│ - Present executive release report                     │
│ - Ask user ONCE: "Confirm & Open Release PR to main?"  │
└──────────────────────────┬─────────────────────────────┘
                           │ (User approves)
                           ▼
┌────────────────────────────────────────────────────────┐
│ STEP 3: PR Creation & Automated Post-Release           │
│ - Commit bump to develop & push                        │
│ - Open Release PR (develop ➔ main)                     │
│ - Post-merge: single-command sync-back (main ➔ develop)│
└────────────────────────────────────────────────────────┘
```

---

## Step 1: Autonomous Pre-Flight & Verification (Continuous Pass)

The agent executes the following checks sequentially in the workspace without prompting:

### 1. Version & SemVer Auditing
1. Query published versions:
   ```bash
   curl -s "https://api.nuget.org/v3-flatcontainer/nexjob/index.json"
   LAST_TAG=$(git describe --tags --abbrev=0)
   ```
2. Classify changes since `$LAST_TAG` into PATCH, MINOR, or MAJOR based on commits and `CHANGELOG.md`.
3. Check Release Size Guard:
   - Commits: `git rev-list --count $LAST_TAG..HEAD`
   - Files in `src/`: `git diff --name-only $LAST_TAG..HEAD -- src | wc -l`
   - Entries: count in `## [Unreleased]` of `CHANGELOG.md`
   - Classify size: 🟢 Green (≤12 entries), 🟡 Yellow (13-24 entries), 🔴 Red (≥25 entries).

### 2. Docs & Changelog Truth Gate
Execute `.agents/method/workflows/documentation.md`:
1. Run `dotnet run --project tools/DocsTruth -c Release -- --report docs-truth-report.md --strict`. Fix any doc drift until 0 findings.
2. Cross-check `CHANGELOG.md` against `git diff $LAST_TAG...HEAD --stat -- src/`:
   - Verify no unrecorded public changes.
   - Verify all library dependency bumps are under `### Dependencies` (v6.2.0).
   - Verify zero repo-relative markdown links (`[path](src/...)`) in `CHANGELOG.md` (v6.0.0).
3. If Wiki pages or READMEs need updates to match code, commit them directly to `develop`:
   `git commit -am "docs: synchronize wiki and readmes for vX.Y.Z release"`

### 3. Pre-Release Quality Compilation & Unit Tests
```bash
# 1. Zero compiler warnings across net8.0 and net10.0
dotnet build -c Release

# 2. All unit tests pass
dotnet test tests/NexJob.Tests/ -c Release --no-build

# 3. Code formatting verified
dotnet format --verify-no-changes

# 4. Packaging validation
dotnet pack -c Release
```

### 4. Packaging Rehearsal (Nothing is Published)

`dotnet pack` succeeding does not prove that a user can install the packages. Rehearse the release the way a user will meet it, with a release-candidate version and a local feed. Do this autonomously before opening the Release PR:

1. **Verify All Packable Projects Are in Publish Workflow:**
   ```bash
   comm -3 <(grep -L "<IsPackable>false" src/*/*.csproj | xargs -n1 basename | sed 's/\.csproj//' | sort) \
           <(grep -o "dotnet pack src/[A-Za-z.]*/" .github/workflows/publish.yml | sed 's|dotnet pack src/||; s|/||' | sort)
   ```
   *Requirement:* Output must be completely empty (all packable projects accounted for).
2. **Pack Release Candidate into Local Temporary Feed:**
   ```bash
   FEED=$(mktemp -d)
   for p in src/*/*.csproj; do dotnet pack "$p" -c Release -p:MinVerVersionOverride=X.Y.Z-rc.1 --output "$FEED"; done
   ```
   Verify each `.nupkg` contains DLL, XML documentation, README, and license, with correct `X.Y.Z-rc.1` dependency version.
3. **Install in Empty Consumer Application:**
   Create a throwaway console project **outside the repository** with a `nuget.config` mapping `NexJob*` to the local feed `$FEED`. Reference the packages simultaneously to confirm clean restore and zero version conflicts.
4. **Smoke Test Storage Providers:**
   Register jobs and verify execution, retries, and dead-letter handling across providers using throwaway containers on high ports (`35xxx`).
5. **Public API Contract Diff:**
   Compare public API surface of `NexJob.dll` against the last published release to ensure no accidental breaking changes exist in a minor/patch.

If any rehearsal step fails, the agent pauses and reports the exact failure before touching `Directory.Build.props`.

### 5. Prepare Version Bump & Changelog
1. Update `<VersionPrefix>X.Y.Z</VersionPrefix>` in `Directory.Build.props`.
2. Move content from `## [Unreleased]` in `CHANGELOG.md` to a new section:
   ```markdown
   ## [X.Y.Z] - YYYY-MM-DD
   ```
   Leave a fresh, blank `## [Unreleased]` at the top.

---

## Step 2: The Single Human Decision Gate

The agent stops and presents a single, concise executive summary:

```markdown
### 🚀 Release Candidate Ready: vX.Y.Z

- **Classification:** [PATCH | MINOR | MAJOR]
- **Release Size:** [🟢 Green | 🟡 Yellow | 🔴 Red] (<N> entries, <M> commits, <K> files in src/)
- **Quality Gates:**
  - [x] Zero compiler warnings (`net8.0` & `net10.0`)
  - [x] Unit test suite 100% green
  - [x] Packaging Rehearsal passed (all 16 packages restore & install cleanly)
  - [x] DocsTruth: 0 drift & CHANGELOG ↔ Diff cross-checked
- **Changes Prepared:**
  - `Directory.Build.props` -> `<VersionPrefix>X.Y.Z</VersionPrefix>`
  - `CHANGELOG.md` -> `## [X.Y.Z] - YYYY-MM-DD`

👉 **Do you approve opening the Release PR for vX.Y.Z from `develop` to `main`?**
```

---

## Step 3: PR Creation & Automated Post-Release

Upon receiving user approval:

### 1. Commit Bump and Open Release PR
```bash
git add Directory.Build.props CHANGELOG.md
git commit -m "chore(release): prepare vX.Y.Z"
git push origin develop

gh pr create \
  --base main \
  --head develop \
  --title "release: vX.Y.Z" \
  --body "## Summary
Release vX.Y.Z of NexJob.

### Checklist
- [x] Version bump in Directory.Build.props (vX.Y.Z)
- [x] CHANGELOG.md updated and cross-checked against git diff
- [x] DocsTruth passed (0 drift)
- [x] Release build 0 warnings on net8.0 and net10.0
- [x] Unit tests 100% passing
- [x] NuGet packages verified via dotnet pack"
```

The agent provides the clickable PR link to the user:
> *"Release PR opened: https://github.com/oluciano/NexJob/pull/<ID>. Please click **Merge Commit** on GitHub."*

### 2. Post-Merge Sync-Back (`main` ➔ `develop`)
Once the user confirms the PR was merged to `main`, the agent runs the sync-back in one shot:

```bash
git checkout develop
git pull origin develop
git fetch origin --tags
git merge origin/main --no-ff -m "chore: sync release vX.Y.Z from main into develop"
git push origin develop
```

### 3. Quick Verification
Verify that `develop` is strictly ahead/synced:
```bash
git log --oneline develop..origin/main   # Must be 0 commits
```

If the release included dashboard or sample updates, optionally deploy the live demo:
```bash
fly deploy -c deploy/fly.toml
```

Release is closed cleanly with zero prompt fatigue!
