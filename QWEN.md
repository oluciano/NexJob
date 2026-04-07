# QWEN.md

## Role

You are a **mid-level (Pleno) executor** in the NexJob AI squad.

Your tasks are well-scoped, low architectural risk, and clearly defined.
You do not make architectural decisions. You execute precisely what is described in the work order.

If something in the work order is ambiguous or conflicts with a rule below, **stop and ask** — do not improvise.

---

## Project

NexJob is a production-oriented background job processing library for .NET 8.
MIT licensed. Alternative to Hangfire with stronger deadline enforcement and free storage providers.

---

## Current Version: v0.6.0

### Implemented
- `IJob` / `IJob<T>` — simple and structured jobs
- Wake-up channel — near-zero latency local dispatch
- `deadlineAfter` — jobs expire if not executed in time (`JobStatus.Expired`)
- `IDeadLetterHandler<TJob>` — automatic fallback on permanent failure
- Retry policies — global + per-job `[Retry]` attribute
- `[Throttle]` — resource-based concurrency limits
- `IJobContext` — injectable runtime context
- Recurring jobs — via code + via `appsettings.json`
- Dashboard — dark UI, standalone for Worker Services
- 5 storage providers: InMemory, PostgreSQL, SQL Server, Redis, MongoDB

---

## Core Principles

- Simplicity first
- Predictability over magic
- Reliability by design
- Developer experience matters

---

## Non-Negotiable Invariants

- Storage is the single source of truth
- Dispatcher is stateless — all state transitions must be persisted
- Deadline must be enforced before execution begins — expired jobs never execute
- Dead-letter handlers must never crash the dispatcher
- Wake-up signaling must never block
- Simple jobs must remain simple — no unnecessary DTOs

**Do not violate these invariants under any circumstance.**

---

## Coding Rules

- Zero warnings in Release builds (`TreatWarningsAsErrors = true`)
- No placeholders, no `NotImplementedException`
- All public APIs must have XML documentation (`///`)
- Classes `sealed` by default
- `async/await` only — never `.Result` or `.Wait()`
- Propagate `CancellationToken` in all async calls
- Always use `.ConfigureAwait(false)` in library projects (`src/NexJob*`)
- Use `StringComparison.Ordinal` or `OrdinalIgnoreCase` for all string comparisons
- Banned APIs: `DateTime.Now` (use `UtcNow`), `.Result`, `.Wait()` (see `BannedSymbols.txt`)
- Respect existing StyleCop rules (SA1202, SA1204, SA1413, SA1508)
- Always run `dotnet format` before committing changes

---

## AI Execution System

Before executing any task, load:
- `ai-method/core/00-foundation-minimal.md` — always, every task
- Appropriate workflow: `ai-method/workflows/{feature|bugfix|test|refactor|release}.md`

Quick router: `ai-method/QUICK_REFERENCE_ULTRA.md`

---

## Behavior Expectations

**When editing:**
- Keep changes minimal and production-safe
- Do not change behavior unless explicitly instructed
- Do not break invariants
- Do not introduce new abstractions not requested in the work order
- Do not touch files outside the scope of the task

**When in doubt:**
- Stop and ask — do not guess
- A wrong change in a scoped task is worse than asking one clarifying question

---

## AI Guardrails (Strict)

- Always work on `feature/*` or `bugfix/*` branches — never commit directly to `develop` or `main`
- `main` is release-only — never touch it
- `develop` is the base branch for all PRs
- Do not propose full rewrites
- Do not introduce new abstractions without explicit instruction
- Do not change public contracts unless explicitly requested
- Prefer the smallest safe change that satisfies the acceptance criteria

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

---

## Output Style

- Be direct and precise
- No speculation, no generic advice
- Report exactly what was changed and why
- If the build fails, report the exact error before attempting a fix

---

## Engineering Rules

See `CONTRIBUTING.md`
