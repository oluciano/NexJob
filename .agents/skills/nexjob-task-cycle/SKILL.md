---
name: nexjob-task-cycle
description: >-
  Executes development tasks, refactors, features, and bugfixes for the NexJob codebase
  using a disciplined cycle: Phase 0 Grooming/Architectural Debate, Boundary & Invariant Checks,
  Mandatory 3N Testing Matrix (Test-First), Atomic Implementation Guardrails, and Automated Verification Gate.
  Activate when implementing features, fixing bugs, refactoring, or planning/grooming tasks in NexJob.
---

# NexJob Disciplined Task Cycle

This skill provides an end-to-end, token-efficient, and mathematically disciplined workflow for developing on **NexJob** (.NET 8).
It synthesizes the best principles of context engineering and verification gates with NexJob's non-negotiable architectural invariants and multi-agent squad governance.

---

## The 6-Phase Workflow

```
[Phase 0: Technical Grooming & Architectural Debate]
                         │
                         ▼
        [Phase 1: Pre-Flight & Boundary Check]
                         │
                         ▼
        [Phase 2: Mandatory 3N Testing Matrix]
                         │
                         ▼
         [Phase 3: Atomic Implementation]
                         │
                         ▼
        [Phase 4: Automated Verification Gate]
                         │
                         ▼
     [Phase 4.5: Continuous Changelog Maintenance]
                         │
                         ▼
        [Phase 5: Handoff & PR Generation]
                         │
                         ▼
     [Phase 6: Issue Closeout & Acceptance Gate]
```

---

## Phase 0: Technical Grooming & Architectural Debate (Discuss / Refine)

> **Goal:** Eliminate ambiguity, challenge assumptions, and agree on the Definition of Done (DoD) before generating code.

When a task is new, non-trivial, or ambiguous (or when explicitly requested via *"faça um grooming desta task"*), adopt the **Tech Lead / Devil's Advocate** persona and execute an interactive grooming session:

1. **Invariants & Scope Check:**
   - Does this touch core execution (`src/NexJob/Internal/`) or core storage contracts? *(If so, flag that it belongs to Architect/Codex/Bruxo).*
   - Does this change any public API signature or behavior?
2. **Failure Modes & Edge Cases:**
   - What happens if the broker drops connection mid-flight?
   - How are poison messages / deserialization failures handled? (Dead-letter vs rethrow).
   - What happens under concurrent execution or cancellation?
3. **Performance & Observability:**
   - Will this allocate on the hot execution path?
   - Is distributed tracing (`traceparent`) properly extracted and propagated?
4. **Interactive Alignment:**
   - Present 2 to 4 concise, targeted trade-off questions to the developer.
   - Once aligned, formalize the **Definition of Done (DoD)** and the **3N Testing Plan**.
5. **Backlog Crystallization (GitHub Issues Integration):**
   - Materialize each groomed item into a dedicated GitHub Issue via `gh issue create`.
   - **Language Mandate:** Issues must be written **strictly in English**.
   - Use conventional titles (`type(scope): description`), assign relevant labels (`bug`, `enhancement`, `reliability`, `performance`, `documentation`, `rfc`), and structure the body with:
     - **Context & Motivation**
     - **Current vs Expected Behavior**
     - **Definition of Done (DoD)**
     - **3N Testing Matrix Plan**
   - Capture the issue ID (e.g. `#138`) to link in the eventual Pull Request.

*For deep architectural questions and broker-specific dilemmas, refer to [grooming-guide.md](./references/grooming-guide.md).*

---

## Phase 1: Pre-Flight & Boundary Check (Anti-Drift)

> **Goal:** Ensure context stays lean and the agent stays strictly within its assigned lane.

1. **Inspect Target Files & Squad Lane (GEMINI.md):**
   - Check which projects/files are involved.
   - ❌ **Protected Core Files:** `src/NexJob/Internal/`, `IJobStorage`, `IRecurringStorage`, `IDashboardStorage`, `JobRecord`, `IScheduler`, `JobWakeUpChannel`.
   - 🛑 If an issue requires modifying protected core execution (e.g. issues like #201 or #204 in `JobExecutor.cs`), it belongs to **Architect / Claude Code (bruxo)** or requires explicit architectural pre-approval before proceeding.
2. **Branch Isolation Mandate:**
   - Always branch off the latest `develop`:
     ```bash
     git checkout develop && git pull origin develop
     git checkout -b <type>/<issue-id>-<short-description>
     ```
   - Never commit implementation code directly to `develop` or `main`.
3. **Context Engineering (State Tracking):**
   - Maintain task progress in `.gemini/scratch/task-state.md` with:
     - Objective & Linked Issue (#ID)
     - Current Phase
     - Decisions & DoD
     - Next Atomic Action
   - This prevents context rot across long sessions or interruptions.

---

## Phase 2: Mandatory 3N Testing Matrix (Test-First)

> **Goal:** Solidify behavior with immutable test contracts before modifying production code.

Every feature or bug fix must produce at least 3 distinct test categories:

- **N1 — Positive:** Happy path operates as expected.
- **N2 — Negative:** Expected failure scenarios fail gracefully (timeouts, broker drops, dead-letter dispatch).
- **N3 — Invalid Input:** Boundary values, nulls, empty collections, malformed payloads.

### The Immutable Test Contract Rule:
- **NEVER** rewrite, rename, or delete an existing passing test to make new code pass.
- When an existing test breaks: fix the production code, not the test.
- The only exception is if the architect explicitly changed the specification (marked with `// Behavior changed in vX.Y: <reason>`).

---

## Phase 3: Atomic Implementation Guardrails

> **Goal:** Smallest safe change with 100% adherence to NexJob coding standards.

Apply the following mandatory engineering rules:
- **Sealed by default:** All new classes must be `sealed` unless designed for extension.
- **Async purity:** `async`/`await` throughout. Never use `.Result` or `.Wait()`.
- **CancellationToken:** Propagated across all async call chains.
- **ConfigureAwait:** Always append `.ConfigureAwait(false)` across library projects (`src/NexJob*`), EXCEPT in Blazor component rendering lifecycle (`src/NexJob.Dashboard/Pages`) which must preserve the `DispatcherSynchronizationContext`.
- **Time Invariant:** Use `DateTime.UtcNow`. Banned API: `DateTime.Now`.
- **String Comparisons:** Always specify `StringComparison.Ordinal` or `StringComparison.OrdinalIgnoreCase`.
- **Documentation:** Full XML documentation (`///`) on all public types and members.
- **No Placeholders:** Zero `NotImplementedException`, zero TODO comments in production code.

---

## Phase 4: Automated Verification Gate

> **Goal:** Guarantee zero CI breakages before claiming work is finished.

Execute the verification sequence directly in the terminal:

```bash
# 1. Verify Code Formatting & StyleCop Rules
dotnet format --verify-no-changes

# 2. Build in Release Mode with Zero Warnings
dotnet build -c Release

# 3. Execute Unit Tests
dotnet test --no-build
```

- **If any step fails:** Do not ask the user what to do. Inspect the failure, correct the production code, and re-run the gate until all 3 pass with **0 errors and 0 warnings**.
- For troubleshooting StyleCop warnings (SA1202, SA1204, SA1413, SA1508), refer to [verification-gate.md](./references/verification-gate.md).

---

## Phase 4.5: Continuous Changelog Maintenance

> **Goal:** Keep `CHANGELOG.md` synchronized with every delivered change so releases are always ready.

Before generating the Pull Request:
1. Open `CHANGELOG.md`.
2. Ensure an `## [Unreleased]` section exists at the top.
3. Add a concise, professional bullet point under the appropriate category:
   - `### Added` for new features or packages.
   - `### Fixed` for bug fixes.
   - `### Changed` for behavioral/config changes.
   - `### Security` for vulnerability fixes.
4. Reference the package/scope, description of behavior, and linked issue/PR (e.g. `(issue #146, PR #153)`).

---

## Phase 4.6: Documentation & Wiki Truth Gate

> **Goal:** Ensure the Wiki and Package READMEs reflect all new options, architectural behaviors, and breaking/fixed patterns before any PR is submitted.

Every task that introduces or modifies public options, defaults, architecture behaviors, or multi-service patterns MUST audit and update documentation directly in the working branch:

1. **Package / Root READMEs:**
   - If a new feature or behavior was added to a package, update the corresponding `src/<Package>/README.md` or root `README.md`.
2. **Wiki Pages (`docs/wiki/*.md`):**
   - **Configuration:** If new options or settings were introduced, update `docs/wiki/11-Configuration-Reference.md`.
   - **Execution & Retry:** If retry/failure/dead-letter mechanics changed, update `docs/wiki/06-Retry-And-Dead-Letter.md`.
   - **Architecture & Best Practices:** If deployment topology, queue isolation, or ops hosting guidelines were impacted, update `docs/wiki/13-Best-Practices.md`.
   - **Dashboard & Monitoring:** If dashboard options, UI scoping, or telemetry changed, update `docs/wiki/10-Dashboard.md` and `docs/wiki/12-OpenTelemetry.md`.
3. **Accuracy Check:** Never leave documentation to be fixed "later in release mode" if the code introducing the change is already being PR'd into `develop`.

---

## Phase 5: Handoff & PR Generation

> **Goal:** Deliver a fully documented, ready-to-merge Pull Request.

Once the verification gate passes cleanly:
1. Provide a concise summary of changes and the completed 3N matrix.
2. Present the ready-to-run GitHub CLI command following the conventional commits standard:

```bash
gh pr create \
  --title "<type>(<scope>): <short description>" \
  --base develop \
  --body "## Summary
<one or two sentences describing the change>

## Type of change
- [ ] Bug fix
- [ ] New feature
- [ ] New trigger provider
- [ ] Refactor / cleanup
- [ ] Tests

## Verification Checklist
- [x] \`dotnet format --verify-no-changes\` passed
- [x] \`dotnet build -c Release\` passed with 0 warnings (TreatWarningsAsErrors)
- [x] \`dotnet test\` passed with 3N test coverage (Positive/Negative/Input)
- [x] No protected core files or storage interfaces modified
- [x] Public API has XML documentation (///)
- [x] Documentation & Wiki updated (\`README.md\` and \`docs/wiki/*.md\`)
- [x] \`CHANGELOG.md\` updated under \`[Unreleased]\`

## Related issues
<!-- Use 'Closes #<id>' for features/bugs. Use 'Relates to #<id>' for ongoing RFCs or architectural spikes. -->
Closes #<id>"
```

---

## Phase 6: Issue Closeout & Acceptance Gate (Audit-Proof Closure)

> **Goal:** Ensure issues are never closed silently or ambiguously. Every closed issue must have an explicit acceptance statement or abandonment rationale.

When an issue reaches completion or is decided to be dismissed:

### Scenario A: Delivered & Accepted (PR Merged)
1. Add the `completed` label to the issue:
   ```bash
   gh issue edit <id> --add-label "completed"
   ```
2. Post an **Acceptance Comment** summarizing the resolution and PR reference:
   ```bash
   gh issue comment <id> --body "### Acceptance & Resolution Statement
   - **Delivered in:** PR #<pr-number>
   - **Verification:** 3N Testing Matrix passing, 0 compiler warnings, 100% format compliant.
   - **Status:** Verified and accepted into \`develop\`."
   ```
3. GitHub automatically closes the issue via the PR's `Closes #<id>` keyword, or close it explicitly:
   ```bash
   gh issue close <id> --reason "completed"
   ```

### Scenario B: Rejected, Deprecated, or Abandoned (Not Planned)
1. Add the `abandoned` label (or `wontfix` / `invalid`):
   ```bash
   gh issue edit <id> --add-label "abandoned"
   ```
2. Post an explicit **Decision Rationale Comment** explaining *why* the issue was dropped:
   ```bash
   gh issue comment <id> --body "### Closure Rationale (Not Planned)
   - **Reason:** <Clear, respectful technical justification or architectural trade-off explaining why this path was not adopted>.
   - **Alternative:** <Reference to superseding issue/RFC if applicable>."
   ```
3. Close the issue as not planned:
   ```bash
   gh issue close <id> --reason "not planned"
   ```
