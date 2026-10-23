# REVIEW.md

Rules for any reviewer (human, `/code-review`, Gemini). Project context lives in `CLAUDE.md`; this file only says what to look for and how to report.

## What failure looks like here

- **Weakened test contract:** a removed or changed assertion (`.Should()`, `Assert.`, `.Verify(`, `Times.`, `Throw`) or a removed `[Fact]`/`[Theory]` under `tests/`. Acceptable only if the assertion reappears elsewhere in the PR (a move), it is a named duplicate of a surviving test, or it carries `// Behavior changed in vX.Y: <reason>`.
- **Broken invariant** in `src/NexJob`: expired job executes; dispatcher holds state outside storage; wake-up signaling can block; a dead-letter handler exception escapes.
- **Storage fix without a test on every provider** (InMemory, PostgreSQL, SQL Server, Redis, MongoDB) and a CI filter that runs it.
- **Trigger package** touching `JobWakeUpChannel`, `IStorageProvider`, `JobRecord` or any core internal.
- **Stored identifier changed** (rename/remap) without migration and the rename checklist.
- **Docs that lie:** wiki, README, CHANGELOG or XML docs describing behavior the code no longer has.
- **New warning** in a Release build, a fixed `Sleep` in a test, or a twin test file (`FooHardeningTests.cs`).

## Depth by path

- Deep (`/code-review high`): `src/NexJob/**`, storage providers, migrations, `.github/workflows/**`.
- Standard: triggers, dashboard, tools.
- Quick: docs, samples.

## Where findings go

- Deep and Standard PRs: findings are posted on the PR as one comment starting with `[review]` (`/code-review --comment` also works). The release retro counts the `[review]` comments.
- Quick PRs: no automatic review.
- Whoever authors the PR runs the review step themselves (Claude, Gemini or the owner). No agent depends on another: a second-reviewer audit happens only when the owner asks for it, and the owner may approve or point out issues directly. Any `[review]` comment, from any source, uses the same severity tags.

## Reporting

- Every finding cites file and line. Never flag from a name alone.
- Tag each: **Important** (likely real bug), **Nit**, **Pre-existing** (not introduced by this PR).
- At most 3 nits. Skip formatting the analyzers already enforce.
- Skip: `site/`, `coverage/`, `TestResults/`, `BenchmarkDotNet.Artifacts/`, generated code.
- Start the summary with a count ("2 findings: 1 Important, 1 Pre-existing"). If nothing needs fixing, say "No issues found."
- Do not repeat findings already posted. After 3 rounds, question the approach instead of adding more comments.
- An **Important** finding is fixed, or disputed with evidence (file and line showing it is false). "Needs a bigger change" is not a dispute until the cost was checked in the code.
- Review the claimed intent too: for a bug fix, check the root cause is fixed, not the symptom.

## Recurring findings

A finding that shows up in 3 or more PRs is not fixed again by hand; it gets promoted at the next release retro:

1. **Enforceable by a machine** → make it fail the build: analyzer or `.editorconfig` rule, or a convention test like `TestSuiteConventionTests`. Then delete it from this file.
2. **Needs judgment** → add one line under "What failure looks like here".
3. **Reviewer is wrong or it is intentional** → add one line under "Skip" with the reason, so it is never charged again.

Count with `gh pr list --state merged --search "merged:>=<last tag date>" --json number` plus each PR's review comments, grouped by theme.
