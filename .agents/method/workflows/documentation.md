# Workflow: Documentation & Changelog Truth

**When:** Updating documentation, modifying Wiki pages (`docs/wiki/`), updating package `README.md` files, adding `CHANGELOG.md` entries, or preparing a release truth gate.
**Execution Skills:** Governed by `.agents/skills/nexjob-wiki-guardian/SKILL.md` (curation & architecture) and `.agents/skills/nexjob-docs-truth/SKILL.md` (truth gate).

---

## 1. Golden Rules of Documentation

1. **Code is the Single Source of Truth:** Documentation describes what the code actually does, not what was intended or imagined.
2. **Compile What You Document (v5.6.0):** Every C# snippet in the Wiki or READMEs must be valid, modern C# that compiles against real packages without phantom APIs.
3. **Verify Claims in Code First (v5.6.0):** Grep across all providers and core implementation before writing claims into docs or issues.
4. **Single Canonical Changelog (v5.9.0):** Maintain root `CHANGELOG.md` only. Never edit `docs/wiki/reference/changelog.md` by hand — `docs/site/prepare.sh` overwrites it at build time.
5. **No Repo-Relative Links in CHANGELOG (v6.0.0):** Use plain text for paths (e.g. `src/NexJob.Kafka/KafkaTrigger.cs`) in `CHANGELOG.md`. Relative links like `[path](src/...)` break MkDocs strict site builds.
6. **Always Record Dependency Bumps (v6.2.0):** Every library dependency bump (e.g. `Cronos`, `Dapper`, `Npgsql`, `Confluent.Kafka`) must be listed under `### Dependencies` in `CHANGELOG.md`.
7. **SemVer & Milestone Alignment (v6.5.0):**
   - **PATCH (`vX.Y.Z`):** Only bug fixes under `### Fixed`.
   - **MINOR (`vX.Y.0`):** New features, options, or behavior changes under `### Added` or `### Changed`.

---

## 2. Pre-Flight & Scope Determination

Identify the scope of the documentation task:
- **PR Scope (`--scope diff`):** Validating changes introduced in the current branch against `develop`.
- **Release Scope (`--scope full`):** Complete release gate auditing all touched packages, Wiki pages, and full `CHANGELOG.md` against `<last-tag>...HEAD`.

---

## 3. Changelog ↔ Diff Cross-Check (Auditoria Cruzada)

Before finalizing docs, run a bidirectional audit between `git diff` and `CHANGELOG.md`:

```bash
# View all modified files across src/ and packages
git diff origin/develop...HEAD --stat -- src/
```

### Checks to Perform:
1. **Omissions Check (Diff → Changelog):**
   - Did any public API, option, behavior, or package configuration change in `src/` without a corresponding bullet in `CHANGELOG.md`?
   - Were `.csproj` dependencies bumped without being listed under `### Dependencies`?
2. **Ghost / Duplicate Check (Changelog → Diff):**
   - Does every bullet point in `## [Unreleased]` correspond to an actual change in the diff?
   - Are any items duplicated across different categories (e.g. mentioned in both `### Added` and `### Fixed`)?
3. **SemVer Guard:**
   - Are any new features or breaking behavioral changes listed under a patch release?
4. **Markdown Link Linting:**
   - Ensure zero repo-relative markdown links (`[text](src/...)` or `[text](../...)`) exist in `CHANGELOG.md`.

---

## 4. Automated DocsTruth Gate

Run the deterministic verification tool:

```bash
dotnet run --project tools/DocsTruth -c Release -- --report docs-truth-report.md --strict
```

The tool proves:
- **`metrics`:** Every instrument in `NexJobMetrics` is documented in `integrations/opentelemetry.md`.
- **`options`:** Every public property in `NexJobOptions` is documented in `reference/configuration.md`.
- **`settings`:** Every public property in `NexJobSettings` is documented in `reference/configuration.md`.
- **`defaults`:** Every `// Default:` annotation matches the actual runtime value.
- **`stale-terms`:** No deprecated phrases from `stale-terms.json` exist across the documentation.

*Exit criteria:* 0 drift findings under `--strict`.

---

## 5. Semantic Review & Wiki Mapping

For each changed component, verify and update the corresponding documentation:

| Component Touched | Primary Wiki Page | Package README |
| :--- | :--- | :--- |
| Core / Schedulers / Dispatcher | `concepts/queues.md`, `concepts/scheduling.md` | `src/NexJob/README.md` |
| Storage Providers | `storage/<provider>.md`, `reference/configuration.md` | `src/NexJob.<Provider>/README.md` |
| Cloud Triggers & Brokers | `integrations/triggers.md`, `integrations/<broker>.md` | `src/NexJob.Trigger.<Broker>/README.md` |
| Dashboard & UI | `integrations/dashboard.md` | `src/NexJob.Dashboard/README.md` |
| Metrics & Tracing | `integrations/opentelemetry.md` | `src/NexJob.OpenTelemetry/README.md` |

---

## 6. Site Build Verification (MkDocs Strict)

Ensure the static documentation site builds without errors or broken links:

```bash
# 1. Prepare wiki files (syncs root CHANGELOG.md into wiki reference)
docs/site/prepare.sh

# 2. Build MkDocs in strict mode (fails on warnings or broken links)
# Requires mkdocs-material installed in virtualenv
mkdocs build --strict
```

---

## 7. Exit Criteria

- [ ] All code snippets compile and reflect modern C# (.NET 8 and .NET 10)
- [ ] Bidirectional audit complete: no omitted changes, no ghost/duplicate entries
- [ ] No repo-relative links in `CHANGELOG.md`
- [ ] All dependency bumps listed under `### Dependencies`
- [ ] `dotnet run --project tools/DocsTruth -c Release -- --strict` passes with 0 drift
- [ ] `docs/site/prepare.sh` executed cleanly
- [ ] MkDocs build passes without broken links
