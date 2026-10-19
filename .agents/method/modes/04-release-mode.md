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

## 2. Streamlined Release Flow (Zero Prompt Fatigue)

The release cycle is orchestrated by `.agents/skills/nexjob-release/SKILL.md` in three stages, eliminating confirmation fatigue by running pre-flight checks autonomously and pausing only once for human approval:

1. **Step 1: Autonomous Pre-Flight & Verification (Continuous Pass):**
   - **Version & SemVer:** Query published NuGet versions and local git tags. Classify SemVer (Major, Minor, or Patch) and check Release Size Guard (Green/Yellow/Red).
   - **Docs & Changelog Truth Gate:** Run `dotnet run --project tools/DocsTruth -c Release -- --strict` and cross-check `CHANGELOG.md` against `git diff --stat -- src/`. Direct-commit any doc sync to `develop`.
   - **Quality Compilation & Packaging:** Verify `dotnet build -c Release` (0 warnings on net8.0 & net10.0), run unit tests (`dotnet test tests/NexJob.Tests/ -c Release --no-build`), check formatting, and execute **Packaging Rehearsal** (pack `-rc.1` into local feed, verify consumer app restore and zero version conflicts).
   - **Prepare Bump:** Stage `<VersionPrefix>` update in `Directory.Build.props` and move `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD` in `CHANGELOG.md`.

2. **Step 2: The Single Human Decision Gate:**
   - Present the executive release summary (version, size, quality status, docs parity).
   - Ask the user **ONCE**: *"Do you approve opening the Release PR for vX.Y.Z from develop to main?"*

3. **Step 3: PR Creation & Automated Post-Release:**
   - Commit bump to `develop`, push, and open the Release PR (`gh pr create --base main --head develop`).
   - After user merges on GitHub using **Merge Commit (`--no-ff`)**, execute single-command sync-back (`git merge origin/main --no-ff`) and push to `develop`.
   - Record learning retro snapshot in `GEMINI.md` if any friction occurred.

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

### Post-Release & Deployment Gates
- [ ] Release PR merged into `main` using **Merge Commit** (`--no-ff`)
- [ ] Tag synced back to `develop` (`git merge origin/main --no-ff`)
- [ ] Live Demo deployed to Fly.io if Dashboard or samples were updated (`fly deploy -c deploy/fly.toml`, from the repository root)

### Prohibited Actions (Non-Negotiable)
- ❌ **NEVER** push directly to `main`
- ❌ **NEVER** create git tags manually (`publish.yml` creates tags on merge)
- ❌ **NEVER** modify production code (`src/**/*.cs`) to match outdated documentation
- ❌ **NEVER** squash merge or rebase release PRs into `main` (always use merge commit `--no-ff`)
