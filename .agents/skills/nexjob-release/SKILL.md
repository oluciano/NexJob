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
6. **Branch Strategy:** Releases are ALWAYS merged via PR from `develop` into `main` using **"Create a merge commit"** (not squash, not rebase). Never push directly to `main`. Never manually create git tags (CI creates the tag upon merge to `main`).
7. **Release Size Guard:** A release must stay small enough to review, canary and roll back. Phase 1 measures the size of what is about to ship and stops the flow when it is oversized (see the thresholds there). An oversized release ships only after the user picks a split or explicitly accepts the mitigation checklist. Never let `[Unreleased]` grow unnoticed: propose a release when it reaches the "green" cap.

---

## The 7-Phase Release Flow

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
                     │
                     ▼
[Phase 6: Post-Release Sync-Back (main ➔ develop)]
                     │
                     ▼
[Phase 7: Post-Release Learning Snapshot (Async Retro)]
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
5. **Measure the release size (Release Size Guard):**
   ```bash
   LAST=$(git describe --tags --abbrev=0)
   git rev-list --count $LAST..HEAD                                   # commits
   awk '/^## \[Unreleased\]/{f=1;next} /^## \[/{f=0} f && /^- \*\*/' CHANGELOG.md | wc -l   # changelog entries
   git diff --name-only $LAST..HEAD -- src | wc -l                    # changed files in src/
   ```
   Reference points from history: usual releases had 1-4 entries; v5.2.0 had 19; v5.6.0 had 43 (105 commits, 86 files in `src/`) and needed a risk review, a documentation audit and a follow-up patch plan.

   | Size | Entries | Commits | Files in `src/` | What to do |
   |---|---|---|---|---|
   | 🟢 Green | ≤ 12 | ≤ 40 | ≤ 25 | Proceed. |
   | 🟡 Yellow | 13-24 | 41-80 | 26-40 | Proceed only after the **mitigation checklist** below. |
   | 🔴 Red (oversized) | ≥ 25 | > 80 | > 40 | **Stop.** Present the options below and wait for the user. |

   A **PATCH** is only valid when it holds bug fixes alone: at most 10 entries, no `feat`, no new public API, no change to a stored data format, no behaviour change. Anything else is a MINOR (or a split).

   **Options when Red:** (a) release earlier, from the last known-good commit, and keep the rest in `develop`; (b) cut a fix-only branch from the last tag with the reviewed fixes and release that as a PATCH, then the features as the next MINOR; (c) proceed anyway, recording in the release PR that it is oversized and completing the mitigation checklist.

   **Mitigation checklist (Yellow and Red):**
   - Every entry that changes observable behaviour is listed in the release PR and in `docs/wiki/18-Migration.md` with what the user must do.
   - Every change to a stored format or key (schema, Redis keys, Mongo documents) has an explicit **mixed-version (rolling upgrade) statement**: what happens while old and new nodes run together, and how to recover.
   - The distributed reliability suite (`tests/NexJob.ReliabilityTests`, not run by CI) was run and its result recorded, with every failure classified as test issue or product issue.
   - Code examples added or changed in the docs were compiled and executed.
   - A rollout note (upgrade one node first, what to watch) and a rollback note (previous version, whether the schema migrations are reversible) are in the release PR.
   - A patch milestone (`vX.Y.Z+1`) exists for the follow-ups already known, so they do not pile into the next minor.

---

## Phase 2: User Confirmation of Version

Always ask the user explicitly before proceeding:
- Present the detected changes.
- Explain whether it was classified as **MAJOR**, **MINOR**, or **PATCH**.
- Propose the next version (e.g. `v5.4.0`).
- Report the size (entries, commits, files in `src/`) and its colour from the Release Size Guard; on Red, do not continue until the user chooses an option.
- Wait for user confirmation or alternative version request.

---

## Phase 3: Code vs Documentation Truth Gate (Auto-Fix on `develop`)

> **CRITICAL RULE:** **NEVER alter production code (`src/**/*.cs`) to match docs.** Always update the docs to match the real code!

1. **Audit Root `README.md` & `GEMINI.md`:**
   - **Packages Table:** Update all version badges in the Packages table to the new version `vX.Y.Z` (`img.shields.io/badge/nuget-vX.Y.Z-blue`).
   - **Roadmap:** Add the new version line with `✅` and summary of features delivered in `vX.Y.Z`.
   - **`GEMINI.md`:** Update `Current published version: **vX.Y.Z**`.
2. **Audit Package READMEs (`src/*/README.md`):**
   - Identify packages with code changes in this release (e.g. `src/NexJob.Dashboard/README.md`, `src/NexJob.Kafka/README.md`).
   - Check if options, methods, endpoints, themes, or UI features are accurately described.
   - If outdated or missing info, **edit the README immediately**.
3. **Mintlify Customer-Facing Documentation Sync (Interactive Step):**
   - Prompt the user:
     > *"Do you have any customer-facing documentation generated or updated in Mintlify (`mintlify-docs`)? If yes, trigger the review/generation in Mintlify now and reply 'gerou' or 'pronto'. If no Mintlify updates are needed, reply 'skip'."*
   - When the user confirms (e.g. 'gerou' or 'pronto'):
     - Execute `python3 docs/site/sync-from-mintlify.py`.
     - The script automatically pulls latest commits (`git pull`) from `mintlify-docs`, converts MDX to MkDocs Material Markdown, and updates `docs/wiki/`.
     - Run `docs/site/prepare.sh && mkdocs build` to verify 0 broken links with `strict: true`.
   - If 'skip': proceed with the existing documentation files.
4. **Audit Wiki (`docs/wiki/`):**
   - Check relevant wiki pages (e.g. `integrations/dashboard.md`, `integrations/triggers.md`, `integrations/kafka.md`, `concepts/`).
   - Check for obsolete signatures, missing screenshots/explanations of new UI screens, or omitted configuration parameters.
   - If outdated, **edit the Wiki pages immediately**.
5. **Commit & Push Docs Direct to `develop`:**
   - Commit all doc and wiki updates with message: `docs: synchronize wiki, root readme and package readmes for vX.Y.Z release`.
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

# 5. Full Dashboard UI Regression Gate (Autonomous Headless Browser)
# Ensures all routes, visual states, and adversarial edge-cases pass before shipping
node .agents/skills/nexjob-dashboard-chaos-gate/scripts/full-regression.js
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

---

## Phase 6: Post-Release Sync-Back (`main` ➔ `develop`)

Since release PRs are merged into `main` using **Merge Commit (`--no-ff`)** (the GitHub default "Create a merge commit" option), the merge commit and git tag must be synced back into `develop` to keep the branches aligned:

> ⚠️ **Critical:** The release PR on GitHub MUST be merged using **"Create a merge commit"** — NOT "Squash and merge" or "Rebase and merge".
> Squash merge collapses all commits into one new SHA. Git then no longer recognises those commits as present in `main`, causing ghost duplicates (e.g. 70+ phantom commits) to appear in every subsequent release PR. Merge Commit preserves the original SHAs so future PRs only show genuinely new commits.

1. **Pull and Sync Back:**
   ```bash
   git checkout develop
   git pull origin develop
   git fetch origin --tags
   git merge origin/main --no-ff -m "chore: sync release vX.Y.Z from main into develop"
   git push origin develop
   ```
2. **Verification:** Confirm `develop` contains the release tag and is strictly even/ahead of `origin/main`.
   ```bash
   git log --oneline develop..origin/main   # must be empty (0 commits)
   git log --oneline origin/main..develop   # shows only new work since release
   ```


---

## Phase 7: Post-Release Learning Snapshot (Async Retro)

> **Goal:** Continuous self-evolution without meetings or ceremony. Capture operational friction or test/CI hurdles and turn them into permanent rules.

Immediately following the sync-back to `develop`:

1. **Lightweight Friction Audit:**
   - Did any compiler warning, StyleCop rule, or flaky test delay the release cycle?
   - Did we encounter documentation drift that was caught late?
   - Record the release size (entries, commits, files in `src/`, colour) and whether a follow-up patch was needed. If the same size problem happened twice, lower the green cap or add a cadence rule.
2. **Repository Hardening (Direct to `develop`):**
   - If a recurring pain point or pattern was identified, synthesize it into a 1-line rule in `GEMINI.md` or a skill reference.
   - Commit directly to `develop`:
     ```bash
     git add GEMINI.md .agents/skills/
     git commit -m "chore(governance): record release vX.Y.Z learnings"
     git push origin develop
     ```
3. **Done:** The release is officially closed, codebase is updated, and the squad is sharper for the next cycle.
