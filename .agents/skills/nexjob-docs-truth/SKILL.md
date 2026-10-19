---
name: nexjob-docs-truth
description: >-
  Proves that the NexJob wiki (docs/wiki), the package READMEs, CHANGELOG.md, and XML docs say what the code does.
  Runs the deterministic tools/DocsTruth checks (metrics, options, settings keys, defaults, stale terms), executes
  the Changelog vs Diff cross-check (omissions, ghost entries, SemVer guard, relative path lint), and performs a semantic
  review of documentation accuracy in two modes: --scope diff (PR/task) and --scope full (release).
---

# NexJob Docs & Changelog Truth

> **Workflow Reference:** Follow `.agents/method/workflows/documentation.md`.  
> **Source of Truth Rule:** The code is the single source of truth; `docs/wiki` and package `README.md` files describe reality.

This skill proves that documentation, the Wiki, and `CHANGELOG.md` stay 100% faithful to the codebase without omissions, drift, or phantom APIs.

---

## When it runs

| Caller | Scope |
| --- | --- |
| `nexjob-task-cycle` Phase 4.6 (change touched options, metrics, defaults, or behavior) | `diff` |
| `nexjob-release` Phase 3 (Release Truth Gate) | `full` |
| On demand ("is the wiki and changelog true?") | either |
| CI job `Docs Truth` on every PR | deterministic tool only |

---

## Subagent Delegation (Token Saver)

Because auditing documentation requires reading multiple `.md` files, READMEs, and the git diff, **delegate this review to an isolated read-only subagent**:

- **Antigravity:** Invoke an isolated subagent (`invoke_subagent` using type `research` or `self`) with the prompt:
  > *"Act as NexJob Docs & Changelog Reviewer following `.agents/method/workflows/documentation.md` and `.agents/skills/nexjob-docs-truth/SKILL.md`. Inspect `git diff`, cross-check `CHANGELOG.md` under `## [Unreleased]`, audit touched Wiki pages and READMEs, run `tools/DocsTruth`, and return the structured Docs Truth Report."*
- **Claude Code:** Invoke a background task/agent (e.g. `Task` tool) with the same instructions.
- **Benefit:** All heavy file reading, diff parsing, and cross-referencing run in an isolated context without polluting the primary conversation.

---

## Step 1: Run the Automated Deterministic Tool (Always First)

```bash
dotnet run --project tools/DocsTruth -c Release -- --report docs-truth-report.md --strict
```

The tool proves:
| Check | What it proves |
| --- | --- |
| `metrics` | Every instrument in `NexJobMetrics` is documented in `integrations/opentelemetry.md` |
| `options` | Every public property in `NexJobOptions` is documented in `reference/configuration.md` |
| `settings` | Every public property in `NexJobSettings` (appsettings keys) is documented there |
| `defaults` | Each `// Default:` in the configuration page equals the real runtime default |
| `stale-terms` | Phrases from `tools/DocsTruth/stale-terms.json` that stopped being true are removed |

- Exit code 1 with `--strict` if any *drift* is detected.
- Fix every *drift* in the documentation (never in code) until the tool reports 0 drift.

---

## Step 2: Changelog ↔ Diff Cross-Check (Auditoria Cruzada)

Cross-check `CHANGELOG.md` (`## [Unreleased]`) against `git diff <base>...HEAD --stat -- src/`:

1. **Omissions Check (Diff → Changelog):**
   - Did any public API, option, behavior, or package configuration change in `src/` without an entry in `CHANGELOG.md`?
   - Were `.csproj` dependencies bumped without being listed under `### Dependencies` (v6.2.0)?
2. **Ghost & Duplicate Check (Changelog → Diff):**
   - Does every bullet point in `## [Unreleased]` correspond to an actual change in the diff?
   - Are any items duplicated across different categories (e.g. mentioned in both `### Added` and `### Fixed`)?
3. **SemVer & Milestone Guard (v6.5.0):**
   - If the target release is a PATCH (`vX.Y.Z`), verify that NO new features or options exist under `### Added`. Patch releases accept strictly bug fixes (`### Fixed`).
4. **Markdown Link Linting (v6.0.0):**
   - Verify zero repo-relative links (`[foo](src/...)`) exist in `CHANGELOG.md`. Paths must be plain text because the changelog is copied into the Wiki and relative links break strict MkDocs site builds.

---

## Step 3: Semantic Review (What a Script Cannot Do)

For each changed behavior, verify that Wiki pages and READMEs accurately describe the implementation:

### `--scope diff`
1. List changed components: `git diff <base>...HEAD --stat -- src/`.
2. Map changed code to the corresponding Wiki pages (table below) and package `README.md` files.
3. Review pages that link to those pages via single-source rules.

### `--scope full`
Review the whole documentation in batches, one area at a time:

| Area | Wiki pages | Code Source |
| --- | --- | --- |
| Queues & recurring | `concepts/queues.md`, `concepts/recurring-jobs.md`, `concepts/scheduling.md`, `guides/multi-service.md`, `guides/runtime-control.md`, `guides/execution-windows.md` | `src/NexJob/Internal`, `NexJobOptions` |
| Retry, dead letter, guarantees | `concepts/retries-and-dead-letter.md`, `concepts/retries.md`, `concepts/dead-letter.md`, `concepts/dead-letter-forwarding.md`, `concepts/delivery-guarantees.md`, `concepts/idempotency.md`, `concepts/continuations.md`, `guides/circuit-breaker.md`, `guides/throttling.md` | `JobExecutor`, retry policy, dead-letter dispatcher, throttle registry, circuit breaker |
| Jobs and context | `concepts/job-types.md`, `guides/job-context.md`, `guides/job-filters.md`, `guides/best-practices.md`, `guides/common-scenarios.md`, `guides/writing-tests.md` | public interfaces and attributes in `src/NexJob` |
| Storage providers | `storage/*.md`, `reference/configuration.md` (storage parts) | `src/NexJob.Postgres`, `SqlServer`, `MongoDB`, `Redis` |
| Dashboard | `integrations/dashboard.md`, `playground.md` | `src/NexJob.Dashboard*` |
| Triggers and brokers | `integrations/triggers.md`, `integrations/kafka.md`, `integrations/rabbitmq.md`, `src/NexJob.Trigger.*/README.md`, `src/NexJob.Kafka/README.md`, `src/NexJob.RabbitMQ/README.md` | `src/NexJob.Trigger.*`, `NexJob.Kafka`, `NexJob.RabbitMQ` |
| Observability & ops | `integrations/opentelemetry.md`, `guides/alerts.md`, `reference/troubleshooting.md`, `reference/faq.md` | `NexJobMetrics`, `NexJobActivitySource`, log messages |
| Reference & onboarding | `reference/configuration.md`, `reference/migration.md`, `introduction.md`, `mental-model.md`, `quickstart.md`, `index.md` | `NexJobOptions`, `NexJobSettings`, `CHANGELOG.md` |

---

## Step 4: Site Build Verification (MkDocs Strict)

When documentation pages or the changelog change, verify the static site build:

```bash
# Prepare wiki files (syncs root CHANGELOG.md into wiki reference)
docs/site/prepare.sh

# Build MkDocs in strict mode (fails on broken links or warnings)
mkdocs build --strict
```

---

## Output: The Coverage Report

End every run with this structured report:

```markdown
Docs truth report: scope <diff|full>, version <x.y.z>, tool: <n> drift, <m> review
| Page / Area | Reviewed | Findings | Status |
| --- | --- | --- | --- |
| CHANGELOG.md | yes (cross-checked against diff) | 0 omitted, 0 duplicates | PASS |
| concepts/queues.md | yes (code: DefaultJobControlService) | 1 outdated default | FIXED |
| guides/job-filters.md | no (out of diff scope) | - | - |

- Unverified claims: <list, or none>
- CHANGELOG cross-check: 100% synchronized with git diff
- MkDocs strict build: PASS
```

---

## Rules

1. **The code wins:** If the code looks wrong, open an issue; do not change documentation to match a bug.
2. **Docs fixes touch docs only:** Documentation fixes go straight to `develop` (or the PR being reviewed); they never touch `src/` logic.
3. **One source per rule:** A rule is written in one canonical page; other pages link to it.
4. **Never claim a page is correct without opening the code:** If unverified, state "unverified".
5. **Compile snippets (v5.6.0):** Code snippets in docs must be valid and compilable.
6. **Plain text paths in CHANGELOG (v6.0.0):** No repo-relative markdown links.
