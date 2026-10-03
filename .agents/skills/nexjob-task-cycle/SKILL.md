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
[Phase 0: Technical Grooming & Architectural Debate]  ◄── 01-architect-mode.md
                         │
                         ▼
     [Phase 1: Pre-Flight & Context Warmup Gate]     ◄── Target Files Baseline
                         │
                         ▼
        [Phase 2: Mandatory 3N Testing Matrix]       ◄── Specialized Workflows
                         │                               (feature, bugfix, refactor,
                         ▼                                reliability)
         [Phase 3: Atomic Implementation]            ◄── 02-execution-mode.md
                         │
                         ▼
        [Phase 4: Automated Verification Gate]       ◄── 03-validation-mode.md
                         │
                         ▼
     [Phase 4.5: Continuous Changelog Maintenance]
                         │
                         ▼
     [Phase 4.6: Documentation & Wiki Truth Gate]
                         │
                         ▼
     [Phase 4.7: Dashboard UX/UI Audit Gate (Conditional)] ◄── ux-auditor skill
                         │
                         ▼
        [Phase 5: Handoff & PR Generation]
                         │
                         ▼
     [Phase 6: Issue Closeout & Acceptance Gate]
```

---

## Phase 0: Technical Grooming & Architectural Debate (Discuss / Refine)

> **Mode:** `.agents/method/modes/01-architect-mode.md`  
> **Goal:** Eliminate ambiguity, challenge assumptions, and agree on the Definition of Done (DoD) before generating code. **No production code generation permitted in this phase.**

When a task is new, non-trivial, or ambiguous (or when explicitly requested via *"faça um grooming desta task"*), adopt the **Tech Lead / Devil's Advocate / Architect** persona:

1. **Invariants & Scope Check:**
   - Consult `.agents/method/core/00-foundation-minimal.md`.
   - Does this touch core execution (`src/NexJob/Internal/`) or core storage contracts? *(If so, flag that it belongs to Architect/Codex/Bruxo).*
   - Does this change any public API signature or behavior?
2. **Failure Modes & Edge Cases:**
   - What happens if the broker drops connection mid-flight?
   - How are poison messages / deserialization failures handled? (Dead-letter vs rethrow).
   - What happens under concurrent execution or cancellation?
3. **Performance & Observability:**
   - Will this allocate on the hot execution path?
   - Is distributed tracing (`traceparent`) properly extracted and propagated?
4. **Operational & SRE Observability Check (Dual Persona: SRE & End User):**
   - Does this backend feature introduce new operational states, automated pauses, throttles, or failure thresholds?
   - **The SRE Question:** If this triggers at 3 AM during an outage, how does the on-call SRE discover, diagnose, and remediate it? Is a Dashboard representation required (`/queues`, `/servers`, `/jobs`, `/catalog`)?
   - **The Developer Question:** Does the developer enqueuing or inspecting jobs have clear visibility into whether their job is waiting, deferred, throttled, or paused?
   - Explicitly decide in grooming: **Is UI representation part of the current DoD**, or should a dedicated UI issue be logged?
5. **Interactive Alignment:**
   - Present 2 to 4 concise, targeted trade-off questions to the developer.
   - Once aligned, formalize the **Definition of Done (DoD)** and the **3N Testing Plan**.
6. **Backlog Health & Threshold Alert (Anti-Accumulation Guard):**
   - Before or upon creating new issues, monitor open issue volume (`gh issue list --state open --limit 50 | wc -l`).
   - If open issues exceed **15 items**, provide a gentle, non-bureaucratic prompt:
     > *"Heads-up: We currently have X open issues in the backlog. Would you like to review/attack one of the existing top priorities, or prune/validate stale tickets before logging a new one?"*
   - This ensures the project avoids zombie backlogs while preserving a fast "groom & attack" rhythm.
7. **Backlog Crystallization (GitHub Issues Integration):**
   - Materialize each groomed item into a dedicated GitHub Issue via `gh issue create`.
   - **Language Mandate:** Issues must be written **strictly in English**.
   - Use conventional titles (`type(scope): description`), assign relevant labels (`bug`, `enhancement`, `reliability`, `performance`, `documentation`, `rfc`), and structure the body with:
     - **Context & Motivation**
     - **Target Files & Components Map:** Explicit list of files/classes identified during grooming (serves as the baseline for future warmup/validity checks).
     - **Current vs Expected Behavior**
     - **Definition of Done (DoD)**
     - **3N Testing Matrix Plan**
   - Capture the issue ID (e.g. `#138`) to link in the eventual Pull Request.
   - **Assign the target version** (label and milestone named like the version, for example `v5.6.1`). A **patch** milestone accepts only small bug fixes: no new public API, no stored-format change, no behaviour change, and nothing on a hot path that needs investigation first. Everything else goes to the next minor milestone. This keeps releases small (see the Release Size Guard in the `nexjob-release` skill).

*For deep architectural questions and broker-specific dilemmas, refer to [grooming-guide.md](./references/grooming-guide.md).*

---

## Phase 1: Pre-Flight, Issue Warmup & Context Validation Gate

> **Goal:** Before touching code or creating branches, summarize the issue, validate whether it remains valid against the latest `develop`, and ensure it was not already addressed or superseded.

When picking an issue from the backlog, execute the **Warmup Gate**:

1. **Issue Context Summary:**
   - Review the issue description, DoD, and specifically the **Target Files & Components Map** recorded during grooming.
2. **Current State & Validity Check (Warmup):**
   - **Inspect Target Files on `develop`:** Check the mapped files on the latest `develop` (`git log -n 5 <path>`, `view_file` or `grep`) to inspect current implementation.
   - **Already Solved?** Verify if a recent PR or refactor already fixed or implemented this behavior indirectly.
   - **Still Valid?** Verify whether the classes, methods, or architectural premises cited in the issue still exist or have evolved.
   - **Active Collisions:** Check if another open PR or branch is actively touching the same components.
3. **Warmup Decision Gate & WIP Limit Enforcement (WIP = 1):**
   - **Enforce WIP Limit:** Verify that no other issue currently holds the `in-progress` label (`gh issue list --label "in-progress"`). Only one issue may be active at a time to prevent multitasking thrashing.
   - **Audit Comment & Status:** If valid, attach the `in-progress` label to the issue (`gh issue edit <id> --add-label "in-progress"`) and post a brief summary comment with the mapped Target Files and warmup findings.
   - **If Valid:** Present a brief summary of the current state vs planned change to the user, confirm alignment, select the appropriate workflow (`.agents/method/workflows/{feature|bugfix|refactor|reliability}.md`), and proceed to branch isolation.
   - **If Already Solved or Obsolete:** Present the evidence immediately to the user, document the rationale, and close the issue without generating redundant code (Phase 6, Scenario B).
4. **WIP Limit = 1 & Issue Kickoff Audit Comment (Scrum Discipline):**
   - **Check Active WIP:** Run `gh issue list --label in-progress` to ensure no other issue is currently in progress. WIP limit per person/squad lane is strictly **1**. Never start a new issue while another is marked `in-progress`.
   - **Post Kickoff Comment:** Before writing code or switching branches, post a clear summary/audit comment on the issue so the team and board know what is being worked on:
     ```bash
     gh issue comment <id> --body "### 🚀 Work Started (WIP)
     - **Branch:** \`<type>/<issue-id>-<description>\`
     - **Objective & Scope:** <Summary of approach, planned changes, and target files>
     - **DoD & 3N Matrix:** Positive, Negative, and Boundary test matrix planned."
     ```
   - **Apply in-progress Label:**
     ```bash
     gh issue edit <id> --add-label "in-progress"
     ```
5. **Inspect Target Files & Squad Lane (GEMINI.md):**
   - Check which projects/files are involved.
   - ❌ **Protected Core Files:** `src/NexJob/Internal/`, `IJobStorage`, `IRecurringStorage`, `IDashboardStorage`, `JobRecord`, `IScheduler`, `JobWakeUpChannel`.
   - 🛑 If an issue requires modifying protected core execution (e.g. issues like #201 or #204 in `JobExecutor.cs`), it belongs to **Architect / Claude Code (bruxo)** or requires explicit architectural pre-approval before proceeding.
6. **Branch Isolation Mandate:**
   - Always branch off the latest `develop`:
     ```bash
     git checkout develop && git pull origin develop
     git checkout -b <type>/<issue-id>-<short-description>
     ```
   - Never commit implementation code directly to `develop` or `main`.
7. **Context Engineering (State Tracking):**
   - Maintain task progress in `.gemini/scratch/task-state.md` with:
     - Objective & Linked Issue (#ID)
     - Target Files Baseline
     - Current Phase
     - Decisions & DoD
     - Next Atomic Action
   - This prevents context rot across long sessions or interruptions.

---

## Phase 2: Mandatory 3N Testing Matrix (Test-First)

> **Workflow Reference:** Follow the task-specific workflow from `.agents/method/workflows/`:
> - New Capabilities: `feature.md`
> - Bug Fixes: `bugfix.md` (mandatory reproduction test before fixing)
> - Code Cleanup: `refactor.md` (strict behavioral parity)
> - Stress/Timeouts: `reliability.md`
> 
> **Goal:** Solidify behavior with immutable test contracts before modifying production code.

Every feature or bug fix must produce at least 3 distinct test categories:

- **N1 — Positive:** Happy path operates as expected.
- **N2 — Negative:** Expected failure scenarios fail gracefully (timeouts, broker drops, dead-letter dispatch).
- **N3 — Invalid Input:** Boundary values, nulls, empty collections, malformed payloads.

### Red-First Proof (mandatory)
Writing the tests first is not enough: show that they test something.
1. **Run the new tests against the current code and watch them fail for the right reason** (the behaviour under test, not a compile error or a missing fixture). Quote the failing assertion in the issue or PR.
2. **Commit the red tests on their own** (`test(scope): <what>, red (#id)`) before the fix. Guard tests that already pass (N2/N3 that protect existing behaviour) may sit in the same commit, but say which are red and which are guards.
3. **Prove an assertion bites when it could pass vacuously** (it only checks "does not throw", the object exists, a fixed sleep elapsed, or a counter nobody reads): change the expected value on purpose once, confirm the failure shows the real value, then revert it.
4. **If the red run shows the premise was wrong** (the bug lives elsewhere, or the behaviour is already correct), stop and go back to Phase 0. Do not bend the test until it fails.
5. **Storage fixes go one commit per provider**, each turning the shared contract test green for that provider.

### The Immutable Test Contract Rule:
- **NEVER** rewrite, rename, or delete an existing passing test to make new code pass.
- When an existing test breaks: fix the production code, not the test.
- The only exception is if the architect explicitly changed the specification (marked with `// Behavior changed in vX.Y: <reason>`).

---

## Phase 3: Atomic Implementation Guardrails

> **Mode:** `.agents/method/modes/02-execution-mode.md`  
> **Goal:** Implement ONLY what was specified. No architectural redesign, no unrequested abstractions, smallest safe change with 100% adherence to NexJob coding standards.

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

> **Mode:** `.agents/method/modes/03-validation-mode.md`  
> **Goal:** Guarantee zero CI breakages and zero invariant drift before claiming work is finished.

Execute the verification sequence directly in the terminal:

```bash
# 1. Verify Code Formatting & StyleCop Rules
dotnet format --verify-no-changes

# 2. Build in Release Mode with Zero Warnings
dotnet build -c Release

# 3. Execute Unit Tests
dotnet test --no-build

# 4. Execute Targeted Integration Tests (Mandatory when touching Storage, Core or Brokers)
# If touching Core (src/NexJob/):
dotnet test tests/NexJob.IntegrationTests --filter "FullyQualifiedName~InMemoryStorageProviderTests|FullyQualifiedName~JobExecutionEndToEndTests"

# If touching Storage Providers (Postgres, SqlServer, Mongo, Redis):
# (Requires local Docker / Testcontainers if run locally, or verify targeted container tests pass)
dotnet test tests/NexJob.IntegrationTests --filter "FullyQualifiedName~<Provider>"

# If touching Messaging Triggers / Outbox (RabbitMQ, Kafka, SQS, Salesforce):
dotnet test tests/NexJob.<Package>.IntegrationTests
```

- **Architecture Compliance Audit (`03-validation-mode.md`):**
  - [ ] Storage is the single source of truth (no in-memory cache overriding state transitions).
  - [ ] Dispatcher remains stateless.
  - [ ] Deadline enforced BEFORE execution begins.
  - [ ] Dead-letter handler exceptions are swallowed/logged (never crashes dispatcher).
  - [ ] Storage & Core contracts verified against Integration Suite (`tests/NexJob.IntegrationTests`).
- **If any step fails:** Do not ask the user what to do. Inspect the failure, correct the production code, and re-run the gate until all pass with **0 errors and 0 warnings**.
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

## Phase 4.7: Operational Visibility & UX Audit Gate (Dual Persona: SRE & End User)

> **Persona:** Senior SRE / On-call Operator + Platform Product Designer.  
> **Goal:** Eliminate developer visual bias, missing operational feedback, hidden backend state blindness, and dead ends before shipping changes.

Every task evaluates this gate along two lanes:

### Lane A: When the task touches `src/NexJob.Dashboard` or UI components
1. **Activate `nexjob-dashboard-chaos-gate` & `ux-auditor`:**
   - Execute the autonomous browser chaos runner (`node .agents/skills/nexjob-dashboard-chaos-gate/scripts/chaos-runner.js`).
   - Run the 3 Persona Walkthroughs (User/Operator Catalog trigger, Developer failure/checkpoint inspection, SRE cluster safety).
   - Execute the Impossible/Boundary stress suite (parameter fuzzing, Unicode, corrupted IDs, mobile viewport wrapping).
   - Verify 0 unhandled 500 errors, 0 unhandled client exceptions, and inspect captured screenshots in `.dashboard-simulations/`.
2. **Audit Core Vectors:**
   - **Operational Feedback & Safety:** Do mutating actions (pause, resume, requeue, reset circuit) have confirmation modals (`confirm()`) to prevent disastrous accidental clicks in production?
   - **Action Symmetry & Traceability:** When displaying warnings (e.g. `⚡ CIRCUIT OPEN` or `⚠️ NO WORKERS`), can the operator click through directly to root-cause errors (`/failed?queue=...`) or active servers without dead ends?
   - **Read-Only / Multi-Cluster Safety:** Are action buttons properly hidden or guarded in read-only / replica cluster views?

### Lane B: When the task is purely Backend (Core, Storage, Triggers)
1. **The Backstage Observability Check:**
   - Does this backend feature introduce new states, auto-pausing, throttling, retention pruning, or failure modes that are currently invisible to operators?
   - **Benefit of the Doubt:** If an SRE has no way to see or control this behavior from the UI, the agent must explicitly flag:
     > *"Operational Observation: This feature introduces state X (e.g., auto-pause, deferred foreign jobs). It operates correctly, but consider logging an issue to expose this state/action on Dashboard screen Y for SREs."*

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
1. Add the `completed` label and remove `in-progress` (releasing the WIP slot):
   ```bash
   gh issue edit <id> --add-label "completed" --remove-label "in-progress"
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
