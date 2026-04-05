# NexJob — Project Context for Claude Code

This file is automatically loaded by Claude Code.

It defines architecture, constraints, and behavioral guarantees.

---

## Project Status

NexJob is a production-oriented background job processing library.

### Implemented

* In-memory storage
* `IJob` / `IJob<T>`
* Wake-up dispatch
* `deadlineAfter`
* Dead-letter handler
* Retry policies
* Scheduling
* Dashboard

### Evolving

* Distributed coordination
* Multi-node consistency
* Storage parity

---

## Core Principles

1. Simplicity first
2. Advanced scenarios supported
3. Predictability over magic
4. Developer experience matters
5. Reliability by design

---

## Job Model

* `IJob` → simple jobs
* `IJob<T>` → structured jobs

---

## Dispatch Model

* Wake-up signaling for local enqueue
* Polling fallback for distributed scenarios

---

## Deadline Model

* Defined via `deadlineAfter`
* Evaluated immediately after fetch and before execution
* Expired jobs are skipped

---

## Failure Model

* Retryable failure
* Permanent failure (dead-letter)
* Expired

---

## Storage Model

* Storage is the single source of truth
* Dispatcher is stateless
* All job state transitions must be persisted

---

## Design Constraints (Runtime Guarantees)

1. Wake-up signaling must never block
2. Deadline must be enforced before execution begins
3. Dead-letter handlers must never crash the dispatcher
4. Simple jobs must remain simple (`IJob`)
5. No unnecessary DTO requirements
6. Storage is authoritative for all state
7. Zero warnings in Release builds

---

## AI Execution System

All AI-assisted tasks use the **NexJob AI Operating Model** — a structured, token-efficient framework for predictable AI execution.

### Quick Start

**Entry point:** `ai-method/QUICK_REFERENCE.md` (2 minutes)
**Full docs:** `ai-method/README.md` (complete guide)
**Navigation:** `AI_METHOD_ENTRY.md` (quick links to all components)

### How to Use

1. **Load foundation:** `ai-method/core/00-foundation-minimal.md` (every task, 200 tokens)
2. **Choose workflow:** `ai-method/workflows/{feature|bugfix|test|refactor|reliability|release}.md`
3. **Choose mode:** `ai-method/modes/{01-architect|02-execution|03-validation|04-release}-mode.md`
4. **Use templates:** `ai-method/templates/` for standardized outputs

### Core Invariants (Always Enforced)

- **Storage is the single source of truth** — no in-memory state overrides it
- **Dispatcher is stateless** — all state transitions must be persisted
- **Deadline enforced before execution** — expired jobs never execute
- **Dead-letter handlers never crash** — errors logged and swallowed
- **Wake-up signaling never blocks** — bounded channel, collapses signals

### Code Quality Enforced at Build Time

- Zero warnings in `Release` builds (`dotnet build --configuration Release`)
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` must be respected
- No `NotImplementedException` or placeholders
- All public APIs must have XML documentation (`///`)
- Classes `sealed` by default
- Async/await everywhere (never `.Result` or `.Wait()`)
- `CancellationToken` propagated in all async calls
- Always use `.ConfigureAwait(false)` in library projects (`src/NexJob*`)
- Use `StringComparison.Ordinal` or `OrdinalIgnoreCase` for all string comparisons
- Prohibit banned APIs: `DateTime.Now` (use `UtcNow`), `.Result`, `.Wait()` (see `BannedSymbols.txt`)
- StyleCop violations fail the build (SA1202, SA1204, SA1413, SA1508)
- Always run `dotnet format` before committing changes

---

## AI Guardrails (Strict)

- Always work on `develop` branch — never commit directly to `main`
- `main` is release-only — only merged via release PR
- Do not propose full rewrites
- Do not introduce new abstractions without clear benefit
- Do not change public contracts unless explicitly requested
- Prefer incremental, low-risk changes

---

## PR Creation Rules

When opening a pull request, always use `gh pr create` with `--body` following
the project template at `.github/pull_request_template.md`.

Required format:
```bash
gh pr create \
  --title "<type>(<scope>): <description>" \
  --base develop \
  --body "## Summary
<one or two sentences describing what this PR does>

## Type of change
- [ ] Bug fix
- [ ] New feature
- [ ] New storage provider
- [ ] Refactor / cleanup
- [ ] Documentation
- [ ] Tests

## Checklist
- [ ] \`dotnet build\` passes with **0 warnings**
- [ ] \`dotnet test\` passes — no regressions
- [ ] New behaviour is covered by tests
- [ ] Public API has XML documentation (\`///\`)
- [ ] Commit messages follow Conventional Commits

## Related issues
<!-- Closes #123 -->"
```

Mark the correct `Type of change` checkbox with `[x]`.
Fill `Related issues` only when there is a related issue — otherwise remove the line.

## Engineering Rules

See `CONTRIBUTING.md`

