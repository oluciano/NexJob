# Workflow: Feature

**When:** Implementing a new capability, storage provider, trigger, dashboard feature, or public API.

---

## 1. Pre-Flight & Context Warmup Gate (Phase 1)
- **WIP Limit = 1:** Verify no other issue has the `in-progress` label (`gh issue list --label in-progress`).
- **Warmup Check:** Inspect mapped target files on `develop` to ensure requirements are not already met or superseded.
- **Mark in-progress:** Add `in-progress` label to the issue and post kickoff comment.
- **Branch Isolation:** Always branch off `develop`: `git checkout -b feat/<issue-id>-<description>`.

---

## 2. Technical Architecture & Invariants (Phase 0)
- Mode: `.agents/method/modes/01-architect-mode.md`.
- No unnecessary DTOs or leaky abstractions.
- Storage remains the single source of truth; dispatcher remains stateless.
- If touching core execution (`src/NexJob/Internal/`), require explicit architectural approval.

---

## 3. Mandatory 3N Testing Matrix & Red-First Proof (Phase 2)
Write the tests FIRST before implementing production code:
- **N1 — Positive:** Happy path operates as expected.
- **N2 — Negative:** Handled failure scenarios (timeouts, broker drops, dead-letter dispatch).
- **N3 — Invalid Input:** Boundary values, nulls, empty collections, malformed arguments.
- **Red-First Proof:** Run the new tests against existing code, watch them fail for the expected reason, and commit them first (`test(scope): <description>, red (#id)`).
- **Test Contract Rule:** Never weaken, invert or delete an assertion. Fix the production code, not the tests. Moving or merging tests into the canonical `<Class>Tests.cs` is allowed; no `*HardeningTests.cs` twins.

---

## 4. Atomic Implementation (Phase 3)
- Mode: `.agents/method/modes/02-execution-mode.md`.
- Implement ONLY what was specified in the DoD.
- Sealed classes by default; XML documentation (`///`) on all public APIs.
- Purity: async/await only, propagate `CancellationToken`, `.ConfigureAwait(false)` (except in Blazor UI lifecycle).
- Zero compiler warnings (`TreatWarningsAsErrors = true`).

---

## 5. Automated Verification & Gates (Phase 4, 4.5, 4.6, 4.7)
- **Formatting:** `dotnet format --verify-no-changes`.
- **Build:** `dotnet build -c Release` (0 warnings, 0 errors).
- **Unit & Integration Tests:** `dotnet test --no-build` and targeted provider tests.
- **Continuous Changelog (Phase 4.5):** Add entry under `## [Unreleased]` -> `### Added` referencing issue/PR.
- **Documentation & Wiki Truth Gate (Phase 4.6):** Update package `README.md` and relevant Wiki pages (`docs/wiki/*.md`).
- **Dashboard Chaos Gate (Phase 4.7):** If touching UI, run headless browser regression (`node .agents/skills/nexjob-dashboard-chaos-gate/scripts/full-regression.js`).

---

## 6. Exit Criteria
- [ ] WIP Limit respected and kickoff comment logged
- [ ] Red-First proof verified and committed
- [ ] 3N matrix satisfied (N1 positive, N2 negative, N3 invalid input)
- [ ] Zero compiler warnings in Release build
- [ ] `dotnet format` verified
- [ ] All unit and integration tests pass
- [ ] No existing passing tests modified without explicit architectural approval
- [ ] `CHANGELOG.md` updated under `[Unreleased]`
- [ ] Documentation & Wiki updated to match code
