---
name: nexjob-dashboard-chaos-gate
description: >-
  Autonomous UX/UI, Persona Walkthrough & Chaos Stress Gate for NexJob Dashboard.
  Triggers automatically whenever changes touch `src/NexJob.Dashboard*` (or runs on-demand).
  Simulates User (Operator), Developer, and SRE personas using headless browser automation,
  executes adversarial/impossible boundary tests (fuzzing query params, mobile viewports, XSS payloads),
  and captures visual screenshots and regression reports without requiring manual test scripts.
---

# NexJob Dashboard Chaos & Persona Gate

An autonomous, scriptless UX/UI validation and chaos testing gate for the NexJob dashboard.
Rather than requiring brittle manual test scripts, this skill uses a **Goal-Driven Headless Browser Engine** (Playwright) to simulate real-world personas and adversarial conditions.

---

## Operational Dual Modes

The Chaos Gate operates in two distinct, complementary modes:

### Mode 1: Daily Change-Driven Chaos Gate (`nexjob-task-cycle` - Phase 4.7)
- **Trigger:** Any PR, bugfix, or feature modifying `src/NexJob.Dashboard*`.
- **Strategy:** Cirúrgica e contextual.
- **Workflow:**
  1. Inspects the `git diff` of the specific change.
  2. Generates dynamic 3N UI scenarios (Valid, Invalid, Boundary/Edge-case) targeting the affected components.
  3. Seeds synthetic scenarios (e.g., job with checkpoint, stripped payload, circuit in error) only as demanded by the change.
  4. Runs `chaos-runner.js` to assert immediate feedback and prevent frustration traps.

### Mode 2: Release Full UI Regression Gate (`nexjob-release` - Phase 4)
- **Trigger:** Official release preparation from `develop` to `main`.
- **Strategy:** Broad, deterministic baseline.
- **Workflow:**
  1. Seeds the complete synthetic ecosystem (all states: enqueued, running, failed with stack traces, checkpoints, stripped payloads, orphan queues, and multi-viewport layouts).
  2. Runs `full-regression.js` auditing all 14 dashboard routes, sorting algorithms, filter combinations, action links, and mobile/tablet layouts.
  3. Enforces 0 HTTP 500 errors and 100% resistance to adversarial fuzzing before release packaging.

---

## The 3 Persona Walkthroughs

### 1. 👩‍💼 User / Operator Persona
- **Goal:** Discover and trigger background jobs via the Catalog.
- **Validations:**
  - Can navigate to `/catalog` cleanly.
  - Can search and filter definitions.
  - Clicks `Trigger Now` on an available job definition.
  - Verifies immediate visual feedback: presence of `✅ Job enqueued successfully` banner with active link to the triggered job.
  - Verifies no unhandled crashes or confusing dead ends.

### 2. 👨‍💻 Developer Persona
- **Goal:** Investigate job failures, trace checkpoints, and inspect parameters.
- **Validations:**
  - Navigates to `/failed` and inspects error stacks.
  - Navigates to `/jobs` with active query filters (`status`, `queue`, `search`).
  - Verifies presence of interactive **Active Filter Chips** with individual `[x]` removal.
  - Opens Job Detail: checks for `💾 Checkpoint State` panel, formatted JSON, and retention markers (`TrimPayloadOnSuccess`).
  - Tests action symmetry: verify Cancel/Delete/Requeue buttons exist according to job status.

### 3. 🚨 SRE / Infrastructure Persona
- **Goal:** Cluster health, queue congestion, circuit breakers, and blast-radius safety.
- **Validations:**
  - Audits `/queues` for orphan queue warnings (enqueued count > 0 with 0 workers listening).
  - Verifies circuit breaker drill-down links: `HalfOpen` / `Recovering` cards provide direct navigation to `/failed?queue=...`.
  - Audits destructive safety in `/settings`: verifies `onclick="return confirm('...')"` guards on cluster-wide Pause/Resume actions.
  - Audits **Mobile Viewport (390px)**: verifies topology diagrams wrap responsively and horizontal overflow is eliminated.

---

## The "Impossible & Boundary" Chaos Suite (Adversarial Fuzzing)

The chaos runner attacks the dashboard with edge-case payloads to verify that the server **never crashes or returns 500**:
1. **Negative & Massive Pagination:** `?page=-999999&limit=999999999`
2. **Malformed States & SQL Injection Probes:** `?status=__SQL_INJECTION__&cluster=non_existent_cluster_99`
3. **Extreme Unicode & Script Tags:** `?search=🔥🔥🔥%20<script>alert(1)</script>`
4. **Corrupted Job Identifiers:** `/jobs/corrupted-non-guid-job-id-xyz`

---

## How to Run

### Step 1: Ensure Sample Dashboard Is Running
If the sample is not already running, launch the background Worker Service:
```bash
dotnet run --project samples/NexJob.Sample.WorkerService/NexJob.Sample.WorkerService.csproj
```
*(Runs by default on `http://localhost:5005/dashboard`)*

### Step 2: Execute the Chaos Runner
Run the script using Node.js:
```bash
node .agents/skills/nexjob-dashboard-chaos-gate/scripts/chaos-runner.js
```

### Step 3: Inspect Artifacts & Report
- Screenshots are saved directly to `.dashboard-simulations/*.png`.
- Full execution telemetry is written to `.dashboard-simulations/chaos-report.json`.
- The agent reviews the JSON report, confirms that all personas passed (`PASS`), verifies that 100% of impossible tests were resisted, and reports the visual summary to the user.
