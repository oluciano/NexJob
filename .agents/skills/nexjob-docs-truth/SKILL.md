---
name: nexjob-docs-truth
description: >-
  Proves that the NexJob wiki (docs/wiki), the package READMEs and the XML docs say what the code does.
  Runs the deterministic tools/DocsTruth checks (metrics, options, settings keys, defaults, stale terms) and then a semantic
  review of the behaviour the documentation describes, in two modes: --scope diff (what a pull request or a version changed)
  and --scope full (the whole documentation, with a coverage report per page). Use in nexjob-release Phase 3, in
  nexjob-task-cycle Phase 4.6, or on demand ("is the wiki still true?", "full docs regression").
---

# NexJob Docs Truth

The code is the source of truth; `docs/wiki` is the only source of the documentation (there is no second copy to sync).
This skill proves that the documentation still describes the code. It is permanent: it is not tied to a release.

## When it runs

| Caller | Scope |
| --- | --- |
| `nexjob-task-cycle` Phase 4.6 (a change touched an option, a metric, a default, a behaviour) | `diff` |
| `nexjob-release` Phase 3 (Truth Gate) | `full` |
| On demand | either |
| CI job `Docs Truth` on every pull request | the deterministic tool only, report in the job summary |

## Step 1: run the tool (always first)

```bash
dotnet run --project tools/DocsTruth -c Release -- --report docs-truth-report.md --strict
```

It always covers the whole surface, whatever the scope:

| Check | What it proves |
| --- | --- |
| `metrics` | every instrument of the NexJob meter is on `integrations/opentelemetry.md` |
| `options` | every public property of `NexJobOptions` is mentioned in `reference/configuration.md` |
| `settings` | every public property of `NexJobSettings` (an appsettings key) is mentioned there |
| `defaults` | each `// Default:` in the configuration page equals the real default (numbers, booleans, enums, strings, durations such as `15s`, `5min`, `7 days`); anything else is listed as *review* |
| `stale-terms` | phrases from `tools/DocsTruth/stale-terms.json` that stopped being true, in the wiki, the READMEs and the XML docs |

- Exit code 1 only with `--strict` and at least one *drift* finding; *review* items never fail.
- Fix every *drift* in the documentation (never in the code) and re-run until the tool reports 0 drift.
- Look at every *review* item by hand and say what you concluded.

## Step 2: semantic review (what a script cannot do)

For each described behaviour, open the code that implements it and confirm the sentence is still true: defaults, order of
operations, what happens on failure, which name is stored, what is logged, what is counted. Quote the code location in the
finding. A claim you could not verify is reported as unverified, never as correct.

### `--scope diff`
1. List what changed: `git diff <base>...HEAD --stat` (a pull request) or `git diff <last-tag>...HEAD --stat` (a version).
2. Map the changed code areas to the pages that describe them (table below) and review only those pages and the
   READMEs of the changed packages.
3. Also review the pages that link to those pages through the single-source rules (for example `multi-service.md` links to
   `queues.md`): the rule lives in one page, the others must only link.

### `--scope full`
Review the whole documentation in batches, one area at a time, so the context stays small. Start every batch from the tool report.

| Area | Wiki pages | Code |
| --- | --- | --- |
| Queues and recurring | `concepts/queues.md`, `concepts/recurring-jobs.md`, `concepts/scheduling.md`, `guides/multi-service.md`, `guides/runtime-control.md`, `guides/execution-windows.md` | `src/NexJob/Internal` (dispatcher, scheduler, registrar, `QueueNames`), `NexJobOptions` |
| Retry, dead letter, guarantees | `concepts/retries-and-dead-letter.md`, `concepts/delivery-guarantees.md`, `concepts/idempotency.md`, `concepts/continuations.md`, `guides/circuit-breaker.md`, `guides/throttling.md` | `JobExecutor`, retry policy, dead-letter dispatcher, throttle registry, circuit breaker |
| Jobs and context | `concepts/job-types.md`, `guides/job-context.md`, `guides/job-filters.md`, `guides/best-practices.md`, `guides/common-scenarios.md`, `guides/writing-tests.md` | public interfaces and attributes in `src/NexJob` |
| Storage | `storage/*.md`, `reference/configuration.md` (storage parts) | `src/NexJob.Postgres`, `SqlServer`, `MongoDB`, `Redis` |
| Dashboard | `integrations/dashboard.md`, `playground.md` | `src/NexJob.Dashboard*` |
| Triggers and brokers | `integrations/triggers.md`, `integrations/kafka.md`, `integrations/rabbitmq.md`, `src/NexJob.Trigger.*/README.md`, `src/NexJob.Kafka/README.md`, `src/NexJob.RabbitMQ/README.md` | `src/NexJob.Trigger.*`, `NexJob.Kafka`, `NexJob.RabbitMQ` |
| Observability and operations | `integrations/opentelemetry.md`, `guides/alerts.md`, `reference/troubleshooting.md`, `reference/faq.md` | `NexJobMetrics`, `NexJobActivitySource`, log messages |
| Reference and onboarding | `reference/configuration.md`, `reference/migration.md`, `introduction.md`, `mental-model.md`, `quickstart.md`, `index.md` | `NexJobOptions`, `NexJobSettings`, `CHANGELOG.md` |

Not reviewed: `reference/changelog.md` (written at release time) and `reference/how-we-test.md` unless tests changed.

## Output: the coverage report

End every run with this table, in the issue or in the release PR, so what was not checked is visible:

```
Docs truth report: scope <diff|full>, version <x.y.z>, tool: <n> drift, <m> review
| Page | Reviewed | Findings | Fixed |
| --- | --- | --- | --- |
| concepts/queues.md | yes (code: DefaultJobControlService, NexJobOptions) | 1 | yes |
| guides/job-filters.md | no (out of the diff) | - | - |
Unverified claims: <list, or none>
```

## Rules
1. The code wins. If the code looks wrong, open an issue; do not change the documentation to match a bug and do not change the code here.
2. Documentation fixes go straight to `develop` (or into the pull request being reviewed); they never touch `src/` logic.
3. One source per rule: a rule is written in one page and the others link to it. If you find a rule copied in two pages, replace the copy by a link.
4. Never claim a page is correct without having opened the code it describes. Say "unverified".
5. A new breaking change adds its stale phrases to `tools/DocsTruth/stale-terms.json` with `since` set to the version, so the old wording cannot come back.

## Extending the tool
- A new check is a static class in `tools/DocsTruth` with a `Run(...)` returning `Finding`s, wired in `DocsTruthRunner`, with
  tests in `tests/NexJob.Tests/DocsTruth` (positive, negative, invalid input; red first).
- The CI job `Docs Truth` is non-blocking. When the baseline stays at 0 drift, remove `continue-on-error` and it blocks.
