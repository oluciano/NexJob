# Workflow: Bugfix

**When:** Fixing a reported bug, unexpected exception, race condition, or behavioral defect.

---

## 1. Pre-Flight & Context Warmup Gate (Phase 1)
- **WIP Limit = 1:** Verify no other issue has the `in-progress` label (`gh issue list --label in-progress`).
- **Warmup Check:** Inspect the target code on `develop` to verify whether the defect still reproduces or was already resolved.
- **Mark in-progress:** Add `in-progress` label to the issue and post kickoff comment.
- **Branch Isolation:** Always branch off `develop`: `git checkout -b bugfix/<issue-id>-<description>`.

---

## 2. Root Cause Analysis & Red-First Proof (Phase 2)
- Identify the exact root cause: logic error, invariant breach, race condition, or deadlock.
- **Mandatory Reproduction Test:** Write a targeted regression test reproducing the exact failure before touching production code.
- **Red-First Proof:** Execute the test, watch it fail with the specific error, and commit the red test alone (`test(scope): reproduce <bug>, red (#id)`).
- **Mandatory 3N Matrix:**
  - **N1 — Positive:** Normal execution works as expected.
  - **N2 — Negative:** The bug scenario is handled without unhandled crash or state corruption.
  - **N3 — Boundary/Input:** Edge cases (null, empty, cancellation) handled cleanly.

---

## 3. Minimal Atomic Fix (Phase 3)
- Mode: `.agents/method/modes/02-execution-mode.md`.
- Make the **smallest safe change** to fix the root cause.
- **No refactoring:** Do not clean up unrelated code while fixing a bug.
- Respect all non-negotiable invariants (storage single source of truth, deadline checks, stateless dispatcher).
- **Immutable Tests Rule:** NEVER modify an existing passing test to make new code pass. Fix production code.

---

## 4. Automated Verification & Gates (Phase 4, 4.5, 4.6)
- **Formatting:** `dotnet format --verify-no-changes`.
- **Build:** `dotnet build -c Release` (0 warnings, `TreatWarningsAsErrors = true`).
- **Test Suite:** `dotnet test --no-build` (regression test and full test suite pass).
- **Continuous Changelog (Phase 4.5):** Add entry under `## [Unreleased]` -> `### Fixed` referencing issue/PR.
- **Documentation Truth Gate (Phase 4.6):** If the fix alters documented defaults or behavior, update the Wiki / README immediately.

---

## 5. Exit Criteria
- [ ] Root cause identified and documented
- [ ] Red-first reproduction test committed and green after fix
- [ ] 3N matrix satisfied
- [ ] All tests pass without modifying existing passing tests
- [ ] Zero compiler warnings in Release build
- [ ] `dotnet format` verified
- [ ] `CHANGELOG.md` updated under `[Unreleased]`
