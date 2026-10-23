---
name: nexjob-task-cycle
description: >-
  Executes development tasks, refactors, features, and bugfixes for the NexJob codebase
  using a disciplined cycle: Phase 0 Grooming/Architectural Debate, Boundary & Invariant Checks,
  Mandatory 3N Testing Matrix (Test-First), Atomic Implementation Guardrails, and Automated Verification Gate.
  Activate when implementing features, fixing bugs, refactoring, or planning/grooming tasks in NexJob.
---

# NexJob Disciplined Task Cycle

This skill provides an end-to-end, token-efficient, and mathematically disciplined workflow for developing on **NexJob** (.NET 8 and .NET 10).
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

### Step-by-Step Execution Sequence

Follow this strict step order:

1. **Step 1: Dispatch Architectural Subagent (Token Saver):**
   - **Antigravity:** Invoke an isolated read-only subagent (`invoke_subagent` using type `research` or `self`) with the prompt:
     > *"Act as NexJob Architect following `.agents/method/modes/01-architect-mode.md`. Analyze [Issue / Requirement], inspect the relevant codebase files in read-only mode, and produce the Output Structure: Overview, Classes to create, Methods & Signatures, Execution Flow, Edge Cases, Invariant Compliance, and the 3N Testing Matrix."*
   - **Claude Code:** Invoke a background task/agent (e.g. `Task` tool) with the same prompt and mode instructions.
   - **Benefit:** All file reads, grep searches, and exploratory reasoning happen in an isolated context, saving thousands of tokens in the primary conversation window.

2. **Step 2: Review Specification & Invariants:**
   - The primary agent receives the subagent's specification and verifies compliance against:
     - Core execution boundaries (`src/NexJob/Internal/` untouched).
     - Storage contract segregation (`IJobStorage`, `IRecurringStorage`, `IDashboardStorage`).
     - Trigger 5 Guarantees (if broker/cloud related).
     - Multi-targeting awareness (`net8.0` and `net10.0`).
     - Operational & SRE visibility (Dashboard representation needed? Is UI in scope?).

3. **Step 3: Interactive Alignment with User (`/grill-me` Protocol):**
   - If there are architectural trade-offs, scope ambiguities, or design choices, present them to the user:
     - **In Antigravity:** Trigger the native interactive modal (`ask_question` tool). Zero raw text slop in chat.
     - **In Claude Code:** Ask concise structured questions directly in chat.
   - Present 2 to 4 concise choices formatted in first-person voice, with the recommended architectural choice first prefixed by `(Recommended)`.

4. **Step 4: Formalize DoD & 3N Testing Plan:**
   - Confirm the agreed scope and matrix:
     - **N1 (Positive):** Happy path works as expected.
     - **N2 (Negative):** Failure path fails as expected (broker down, storage exception, timeout).
     - **N3 (Invalid / Boundary):** Null, empty, corrupted payload, extreme concurrency handled gracefully.

5. **Step 5: Backlog Health & Threshold Alert (Anti-Accumulation Guard):**
   - Check open issue volume (`gh issue list --state open --limit 50 | wc -l`).
   - If open issues exceed **15 items**, provide a gentle alert:
     > *"Heads-up: We currently have X open issues in the backlog. Would you like to review/attack one of the existing top priorities, or prune/validate stale tickets before logging a new one?"*

6. **Step 6: Backlog Crystallization (GitHub Issues Integration):**
   - Materialize each groomed item into a dedicated GitHub Issue via `gh issue create`.
   - **Language Mandate:** Issues must be written **strictly in English**.
   - Use conventional titles (`type(scope): description`), assign relevant labels (`bug`, `enhancement`, `reliability`, `performance`, `documentation`, `rfc`), and structure the body with:
     - **Context & Motivation**
     - **Target Files & Components Map** (baseline for future warmup/validity checks)
     - **Current vs Expected Behavior**
     - **Definition of Done (DoD)**
     - **3N Testing Matrix Plan**
   - **Assign target version & milestone:** A **patch** milestone (`vX.Y.Z`) accepts only small bug fixes. Any new feature, new options, or behavior changes belong to minor (`vX.Y.0`).
   - Once crystallized, proceed immediately to **Phase 1**.

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

3. **Squad Lane & Core Boundaries Check (GEMINI.md):**
   - Verify which projects and files are involved.
   - ❌ **Protected Core Files:** `src/NexJob/Internal/`, `IJobStorage`, `IRecurringStorage`, `IDashboardStorage`, `JobRecord`, `IScheduler`, `JobWakeUpChannel`.
   - 🛑 If an issue requires modifying protected core execution (e.g. `JobExecutor.cs`), it belongs to **Architect / Claude Code (bruxo)** or requires explicit architectural pre-approval before proceeding.

4. **Warmup Decision & WIP Limit Enforcement (WIP = 1):**
   - **If Already Solved or Obsolete:** Present evidence immediately to the user, document the rationale, and close the issue without generating redundant code (Phase 6, Scenario B).
   - **If Valid:**
     - **Enforce WIP Limit:** Run `gh issue list --label in-progress` to ensure no other issue is currently active. WIP limit per person/squad lane is strictly **1**.
     - **Post Kickoff Comment:**
       ```bash
       gh issue comment <id> --body "### Work Started (WIP)
       - **Branch:** \`<type>/<issue-id>-<description>\`
       - **Objective & Scope:** <Summary of approach, planned changes, and target files>
       - **DoD & 3N Matrix:** Positive, Negative, and Boundary test matrix planned."
       ```
     - **Apply `in-progress` Label:**
       ```bash
       gh issue edit <id> --add-label "in-progress"
       ```

5. **Branch Isolation Mandate:**
   - Always branch off the latest `develop`:
     ```bash
     git checkout develop && git pull origin develop
     git checkout -b <type>/<issue-id>-<short-description>
     ```
   - Never commit implementation code directly to `develop` or `main`.

6. **Context Engineering (State Tracking):**
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
> - Triggers & Brokers: `trigger.md`
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

### The Test Contract Rule:
- **NEVER** weaken, invert, skip or delete an assertion to make new code pass.
- **NO FIXED SLEEPS:** A test never waits a fixed time for something to happen (`Task.Delay(50)` is strictly banned). Must poll for observed state with `TestWait.UntilAsync` / `TestWait.SucceededAsync` / `TestWait.InvokedAsync`, `TaskCompletionSource`, or `PauseObservedSignal`.
- When an existing test breaks: fix the production code, not the test.
- Moving a test into its canonical `<Class>Tests.cs`, merging into a `[Theory]`, or deleting a proven duplicate is allowed; name the surviving test in the PR. Twin files such as `*HardeningTests.cs` are banned.
- The only exception to changing an expectation is an explicit architect decision (marked with `// Behavior changed in vX.Y: <reason>`).

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

### Step-by-Step Execution Sequence

Follow this strict step order:

1. **Step 1: Run Local Terminal Verification (Primary Agent):**
   Execute the verification commands directly in the workspace terminal:
   ```bash
   # 1. Verify Code Formatting & StyleCop Rules
   dotnet format --verify-no-changes

   # 2. Build in Release Mode with Zero Warnings (validating both net8.0 and net10.0)
   dotnet build -c Release

   # 3. Execute Unit Tests — one project at a time, scoped to what this task touched.
   # `dotnet test --no-build` with no project/filter runs the ENTIRE solution serially
   # in one process: it can take 15+ minutes, and running the reliability/stress suites
   # alongside everything else causes resource contention that fails tests which pass
   # in isolation (false positives). CI never does this — it matrices by project x
   # framework x category (see .github/workflows/ci.yml). Match that shape locally:
   dotnet test tests/<TouchedPackage>.Tests --no-build
   # Repeat per touched package's unit-test project (e.g. NexJob.Tests for core,
   # NexJob.RabbitMQ.Tests for the RabbitMQ trigger). Add --framework net8.0 / net10.0
   # only if you need to isolate one runtime leg; otherwise both run together per project.

   # 4. Execute Targeted Integration Tests (Mandatory when touching Storage, Core or Brokers)
   # If touching Core (src/NexJob/):
   dotnet test tests/NexJob.IntegrationTests --filter "FullyQualifiedName~InMemoryStorageProviderTests|FullyQualifiedName~JobExecutionEndToEndTests"

   # If touching Storage Providers (Postgres, SqlServer, Mongo, Redis):
   dotnet test tests/NexJob.IntegrationTests --filter "FullyQualifiedName~<Provider>"

   # If touching Messaging Triggers / Outbox (RabbitMQ, Kafka, SQS, Salesforce):
   dotnet test tests/NexJob.<Package>.IntegrationTests
   ```
   `tests/NexJob.ReliabilityTests` and `tests/NexJob.StressTests` are their own CI
   workflows (`reliability.yml`, `stress-tests.yml`), filtered and scheduled separately
   from the PR pipeline — they are not part of this local gate unless the task is
   specifically about reliability/crash-recovery behavior. Running them locally
   requires Docker containers they expect (SQL Server, etc.); do not add them to a
   routine verification pass.

2. **Step 2: Test Contract Self-Audit (Mandatory when touching `tests/**`):**
   ```bash
   git diff develop -- tests | grep -E '^-' | grep -E 'Should\(|Assert\.|Verify\(|Times\.|\[Fact|\[Theory|Skip ='
   ```
   Every line printed must be explained in the PR body: moved to which file, duplicate of which surviving test, or `// Behavior changed in vX.Y: <reason>`. An unexplained line means an assertion was weakened: restore it and fix the production code instead.

3. **Step 3: Dispatch Code Review Subagent (Token Saver):**
   Before opening the PR, delegate the dual-axis review to an isolated read-only subagent. The reviewer follows `REVIEW.md` (failure definitions, depth by path, severity tags, reporting format). Keep every finding, including the ones you fix, for the PR comment in Phase 5:
   - **Antigravity:** Invoke an isolated subagent (`invoke_subagent` using type `research` or `self`) with the prompt:
     > *"Act as NexJob Reviewer following `.agents/method/modes/03-validation-mode.md`. Inspect `git diff develop`, verify StyleCop rules, XML documentation, CancellationToken propagation, .ConfigureAwait(false) usage, and ensure the 3N testing matrix has no weakened assertions. Return a structured PASS or FAIL verdict with specific file and line citations."*
   - **Claude Code:** Invoke a background task/agent (e.g. `Task` tool) with the same prompt and mode instructions.
   - **Benefit:** The heavy diff inspection and standards checklist run in an isolated context, returning only the final verdict and necessary fixes to the primary session without token bloat.

4. **Step 4: Remediate Findings (Zero Toleration Gate):**
   - If the subagent or tests report any finding (`FAIL`), fix the production code or tests immediately.
   - Re-run the affected checks until all pass with **0 errors and 0 warnings** and the subagent returns `PASS`.
   - Once clean, proceed to **Phase 4.5**.

*For troubleshooting StyleCop warnings (SA1202, SA1204, SA1413, SA1508), refer to [verification-gate.md](./references/verification-gate.md).*

---

## Phase 4.5: Continuous Changelog Maintenance

> **Workflow Reference:** `.agents/method/workflows/documentation.md` (Section 3: Changelog ↔ Diff Cross-Check)  
> **Goal:** Keep `CHANGELOG.md` synchronized with every delivered change so releases are always ready.

Before generating the Pull Request:
1. Open `CHANGELOG.md`.
2. Ensure an `## [Unreleased]` section exists at the top.
3. Add a concise, professional bullet point under the appropriate category:
   - `### Added` for new features or packages (MINOR versions only).
   - `### Fixed` for bug fixes.
   - `### Changed` for behavioral/config changes.
   - `### Security` for vulnerability fixes.
   - `### Dependencies` for library dependency bumps (v6.2.0).
4. Reference the package/scope, description of behavior, and linked issue/PR (e.g. `(issue #146, PR #153)`).
5. **No repo-relative links:** Use plain text for paths (e.g. `src/NexJob/Job.cs`). Never use `[path](src/...)` which breaks strict MkDocs builds.

---

## Phase 4.6: Documentation & Wiki Truth Gate

> **Workflow Reference:** `.agents/method/workflows/documentation.md` & `.agents/skills/nexjob-docs-truth/SKILL.md`  
> **Goal:** Ensure the Wiki, Package READMEs, and CHANGELOG reflect all new options, architectural behaviors, and breaking/fixed patterns before any PR is submitted.

Every task that introduces or modifies public options, defaults, architecture behaviors, or multi-service patterns MUST audit and update documentation directly in the working branch:

> **Subagent Delegation:** In Antigravity or Claude Code, run the `nexjob-docs-truth` skill with `--scope diff` via an isolated read-only subagent. The subagent runs `dotnet run --project tools/DocsTruth -c Release -- --strict`, executes the Changelog ↔ Diff cross-check, and performs the semantic review of changed Wiki pages and package READMEs.

1. **Package / Root READMEs:**
   - If a new feature or behavior was added to a package, update the corresponding `src/<Package>/README.md` or root `README.md`.
2. **Wiki Pages (`docs/wiki/*.md`):**
   - **Configuration:** If new options or settings were introduced, update `docs/wiki/reference/configuration.md`.
   - **Execution & Retry:** If retry/failure/dead-letter mechanics changed, update `docs/wiki/concepts/retries.md`, `dead-letter.md` or `dead-letter-forwarding.md` (`retries-and-dead-letter.md` is the overview).
   - **Architecture & Best Practices:** If deployment topology, queue isolation, or ops hosting guidelines were impacted, update `docs/wiki/guides/best-practices.md`.
   - **Dashboard & Monitoring:** If dashboard options, UI scoping, or telemetry changed, update `docs/wiki/integrations/dashboard.md` and `docs/wiki/integrations/opentelemetry.md`.
   - **Failure semantics:** If what a failure costs a job changed (attempts used or given back, shutdown, throttle, pause, deadline, crash recovery, dead-letter calls), update `docs/wiki/concepts/delivery-guarantees.md`. It is the one table that lists them; a change that is not there makes the wiki wrong.
   - **Feature lists:** A new user-facing feature goes in the Key features list of `docs/wiki/introduction.md` and in Core Features of the root `README.md`, each name linking to its page.
   - **Links:** The docs site builds in strict mode on every pull request, so a broken link fails it. Do not link repository paths (`docs/wiki/...`) from `CHANGELOG.md`: the wiki shows that file as a page.
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
   After `gh pr create`, post the Step 3 findings as inline `[review]` comments (format in `REVIEW.md`; a finding with no line goes in one PR comment). Then answer **in the thread of each comment**: `Fixed` (with the commit), `Disputed` (with evidence) or `No change needed` (with why). A reply elsewhere leaves the comment vague. The release retro counts the `[review]` comments. Quick-depth PRs (docs, samples) skip this. Any agent running this cycle does it, so no agent depends on another reviewing its work.
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
- [x] No assertion weakened or removed; every removed assertion is listed above with its reason (moved, named duplicate, or \`Behavior changed\` marker)
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
