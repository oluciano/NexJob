# AI Mode: Release

**Role:** Validate, synchronize documentation, and govern production releases.  
**Execution Engine:** Run via the automated skill: `.agents/skills/nexjob-release/SKILL.md`.

---

## 1. Release Flow & Branch Strategy

- **`develop` branch:** All features, bug fixes, and documentation audits are committed to `develop`. Never push directly to `main`.
- **`main` branch:** Mirrors the live published packages on NuGet and GitHub Releases.
- **Release PR:** A Pull Request from `develop` into `main` using **"Create a merge commit"** (`--no-ff`). Never squash or rebase release PRs.
- **Tagging & Publishing:** Automated via CI/CD (`publish.yml`) on merge to `main`. **NEVER manually create git tags or manually push to NuGet.**

---

## 2. The 7 Canonical Release Phases

1. **Phase 1: Version Auditing, SemVer & Release Size Guard:**
   - Determine version change (Major, Minor, or Patch) based on NuGet (`api.nuget.org`) and git history since last tag.
   - Respect SemVer strictly (Major for breaking changes, Minor for backwards-compatible features/triggers, Patch for bug fixes only).
   - Evaluate the **Release Size Guard** (Green ≤12 entries/≤40 commits/≤25 src files; Yellow 13-24 entries; Red ≥25 entries — stop and propose split).
2. **Phase 2: User Confirmation of Version:**
   - **Mandatory Stop:** Always present detected changes and proposed version, and require explicit user confirmation or override before modifying any file.
3. **Phase 3: Code vs Documentation Truth Gate (Direct Sync on `develop`):**
   - **Code is the ultimate source of truth:** If docs disagree with code, fix the docs. **Never alter production code (`src/**/*.cs`) to match docs.**
   - Audit root `README.md`, package READMEs (`src/*/README.md`), and Wiki (`docs/wiki/*.md`).
   - If Mintlify customer docs exist (`mintlify-docs`), synchronize via `python3 docs/site/sync-from-mintlify.py` and verify `mkdocs build --strict`.
   - Commit documentation updates directly to `develop`.
4. **Phase 4: Pre-Release Quality & Packaging Rehearsal:**
   - Verify `dotnet build -c Release` (0 warnings, `TreatWarningsAsErrors = true`).
   - Verify all unit tests pass (`dotnet test tests/NexJob.Tests/ -c Release --no-build`).
   - Verify code formatting (`dotnet format --verify-no-changes`).
   - Verify all 16 packages pack (`dotnet pack -c Release`).
   - **Packaging Rehearsal:** Rehearse release candidate into a local temporary feed and test across all 5 providers (InMemory, PostgreSQL, SQL Server, Redis, MongoDB).
5. **Phase 5: Version Bump, Changelog & Release PR to `main`:**
   - Set `<VersionPrefix>X.Y.Z</VersionPrefix>` in `Directory.Build.props`.
   - Move `[Unreleased]` entries to `[X.Y.Z] - YYYY-MM-DD` in `CHANGELOG.md`.
   - Commit on `develop` and open Release PR to `main` (`gh pr create --base main --head develop --title "release: vX.Y.Z"`).
6. **Phase 6: Post-Release Sync-Back (`main` ➔ `develop`):**
   - Once merged to `main`, sync the release commit and tag back into `develop` using a merge commit (`git merge origin/main --no-ff`).
7. **Phase 7: Post-Release Learning Snapshot (Async Retro):**
   - Record any friction, flaky tests, or packaging hurdles into `GEMINI.md` or method references to permanently harden future releases.

---

## 3. Complete Package Catalog (16 Packages)

Every release validates the full catalog of packages:

### Core & Observability
- `NexJob` (Core execution engine, scheduler, and In-Memory provider)
- `NexJob.OpenTelemetry` (Tracing, metrics, and activity sources)

### Dashboard
- `NexJob.Dashboard` (Embedded middleware UI for ASP.NET Core)
- `NexJob.Dashboard.Standalone` (Embedded HTTP server UI for Worker Services)

### Storage Providers
- `NexJob.Postgres` (PostgreSQL storage with `SKIP LOCKED`)
- `NexJob.SqlServer` (SQL Server storage with `UPDLOCK, READPAST`)
- `NexJob.Redis` (Redis storage with distributed throttling)
- `NexJob.MongoDB` (MongoDB storage)

### Triggers & Broker Integrations
- `NexJob.Trigger.AzureServiceBus`
- `NexJob.Trigger.AwsSqs`
- `NexJob.Trigger.GooglePubSub`
- `NexJob.Trigger.Salesforce` (gRPC Pub/Sub API)
- `NexJob.Trigger.SalesforceStreaming` (CometD/Bayeux API)
- `NexJob.RabbitMQ` (Trigger + Outbox Producer)
- `NexJob.Kafka` (Trigger + Outbox Producer)

### Scaffolding & Templates
- `NexJob.Templates` (CLI starter templates)

---

## 4. Release Checklist

### Pre-Release Quality Gates
- [ ] Build passes with zero warnings in Release mode (`dotnet build -c Release`)
- [ ] All unit tests pass (`dotnet test tests/NexJob.Tests/ -c Release`)
- [ ] Code formatting verified (`dotnet format --verify-no-changes`)
- [ ] All 16 packages pack successfully (`dotnet pack -c Release`)
- [ ] Dashboard regression suite passed (`node .agents/skills/nexjob-dashboard-chaos-gate/scripts/full-regression.js`)

### Documentation & Versioning Gates
- [ ] Code vs Documentation Truth Gate passed (Wiki and Package READMEs match code)
- [ ] `Directory.Build.props` has updated `VersionPrefix`
- [ ] `CHANGELOG.md` moved `[Unreleased]` to `[X.Y.Z] - YYYY-MM-DD`
- [ ] Clean working tree on `develop`

### Prohibited Actions (Non-Negotiable)
- ❌ **NEVER** push directly to `main`
- ❌ **NEVER** create git tags manually (`publish.yml` creates tags on merge)
- ❌ **NEVER** modify production code (`src/**/*.cs`) to match outdated documentation
- ❌ **NEVER** squash merge or rebase release PRs into `main` (always use merge commit `--no-ff`)
