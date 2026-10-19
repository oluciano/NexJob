---
name: nexjob-wiki-guardian
description: >-
  Human-centric documentation architect and curator for NexJob. Refactors, consolidates, and authors
  wiki pages (docs/wiki), package READMEs, and guides with a dev-to-dev tone (zero AI fluff). Prevents
  information sprawl and feature accumulation, unifies scattered docs, extracts buried features,
  and validates technical accuracy against code via DocsTruth and MkDocs.
---

# NexJob Wiki Guardian: Curation & Documentation Architecture

> **Workflow Reference:** Follow `.agents/method/workflows/documentation.md`.  
> **Source of Truth Rule:** The code is the single source of truth; documentation describes reality.

The **Wiki Guardian** is the authoring, curation, and information architecture skill for **NexJob**.
Its purpose is to counter the natural degradation of technical documentation: unorganized text accumulation (*blind appending*), feature sprawl across multiple disconnected files, burying critical configurations inside generic tutorials, and corporate "AI fluff" (empty adjectives and marketing speak).

---

## The 4 Non-Negotiable Invariants

### 1. Anti-Accumulation & Consolidation (Refactor > Blind Appending)
- **Never blind append:** Before drafting any new section, grep across `docs/wiki/` to discover where related concepts already exist.
- **Strict canonicity:** Every concept, trigger, provider, or option has **one canonical home**. If a feature is partially explained across 3 different files, consolidate the content into the canonical location and replace the fragments with direct links.
- **Zero buried features:** Critical configurations, concurrency limits, or resilience semantics must never be buried as footnotes or inline code comments inside generic tutorials. Every feature must be visible in the page outline or options table.

### 2. Dev-to-Dev Tone (Zero AI Fluff / Zero Marketing)
- **No empty preambles:** Banned phrases include generic intros ("In today's fast-paced development landscape...", "NexJob provides a robust and seamless solution...", "Unlock the power of...").
- **Staff Engineer to Staff Engineer Style:** Concise, direct, and contextualized for engineers operating in production:
  1. **Purpose / Problem Solved:** 1 to 2 direct sentences explaining the exact problem solved.
  2. **Immediate Quickstart:** Minimal, copy-pasteable C# snippet ready to run.
  3. **Configuration Table:** Key name, type, real runtime default from code, and operational impact.
  4. **Gotchas & Failure Behavior:** Exact semantics when networks drop, timeouts occur, broker limits hit, deadlocks arise, or retries exhaust.

### 3. Human Information Architecture & Navigation
- **The 2-Click Rule:** An engineer must be able to locate any configuration or architectural capability of NexJob in at most 2 clicks from the Wiki home.
- **MkDocs Navigation Sync:** Every new page must be registered in the semantic navigation hierarchy of [`mkdocs.yml`](file:///home/luciano/git/NexJob/mkdocs.yml).
- **Strict Relative Links:** Use relative markdown paths between wiki pages (e.g., `[Configuration](../reference/configuration.md)`). Never use repo-relative paths pointing to `src/` inside `CHANGELOG.md` (breaks MkDocs strict build).

### 4. Code Fidelity (Docs-Truth)
- C# code is the single source of truth. Never document aspirational behavior or phantom APIs.
- Every C# snippet in documentation must be valid, modern C# (.NET 8+).
- Running the automated [`tools/DocsTruth`](file:///home/luciano/git/NexJob/tools/DocsTruth/Program.cs) validation check is mandatory before considering any documentation change complete.

---

## Quality Matrix: AI Slop vs Dev-to-Dev

| Scenario | Anti-Pattern (AI Slop / Blind Append) | Wiki Guardian Standard (Dev-to-Dev) |
| :--- | :--- | :--- |
| **Feature Introduction** | *"NexJob introduces a powerful and versatile Throttle mechanism designed to seamlessly empower developers to manage workload spikes effortlessly..."* | *"Throttle limits concurrent executions of a queue per worker instance. Use when downstream databases or external APIs cannot tolerate traffic spikes."* |
| **New Option Added** | Tucking an inline comment `// Now supports TimeoutSeconds = 30` inside a code block in an unrelated guide. | Adding the canonical row in `reference/configuration.md` options table with type, real default, and timeout expiration behavior. |
| **Fragmented Topic** | Creating a separate `guides/redis-cluster.md` duplicating setup steps already in `storage/redis.md`. | Enriching `storage/redis.md` with a dedicated Cluster topology section, preserving a single canonical guide for Redis. |
| **Failure Handling** | *"The system handles failures gracefully and transparently."* | *"If the broker disconnects, the trigger backs off exponentially (1s to 30s) and retains channel state. Uncommitted messages are nack'd and requeued."* |

---

## Curation Execution Cycle (4 Steps)

```
┌────────────────────────────────────────────────────────┐
│ STEP 1: Discovery & Anti-Sprawl Search                 │
│ - grep across docs/wiki to locate existing mentions    │
│ - Identify fragmentation, duplication, or stale notes  │
└──────────────────────────┬─────────────────────────────┘
                           │
                           ▼
┌────────────────────────────────────────────────────────┐
│ STEP 2: Architecture & Dev-to-Dev Authoring            │
│ - Consolidate into the canonical page (no append)      │
│ - Extract buried configs into structured tables        │
│ - Write minimal, copy-pasteable, compiling C# snippets │
└──────────────────────────┬─────────────────────────────┘
                           │
                           ▼
┌────────────────────────────────────────────────────────┐
│ STEP 3: Navigation Curation (MkDocs)                   │
│ - Update mkdocs.yml navigation if page hierarchy moves │
│ - Validate relative links between markdown files       │
│ - Ensure CHANGELOG entries use plain text for paths    │
└──────────────────────────┬─────────────────────────────┘
                           │
                           ▼
┌────────────────────────────────────────────────────────┐
│ STEP 4: Truth Gate Verification                        │
│ - Run dotnet run --project tools/DocsTruth -c Release  │
│ - Cross-check against git diff                         │
└────────────────────────────────────────────────────────┘
```

---

## Wiki Guardian Completion Checklist

Before completing any documentation update, verify:
- [ ] No marketing language, filler adjectives, or AI fluff.
- [ ] Duplicate topics unified in their canonical page with incoming links updated.
- [ ] All configuration options documented with type, real runtime default, and operational behavior.
- [ ] Production gotchas, timeouts, and failure modes explicitly stated.
- [ ] C# code snippets use real, valid .NET 8+ APIs.
- [ ] Navigation in `mkdocs.yml` is clean, balanced, and free of broken links.
- [ ] `dotnet run --project tools/DocsTruth -c Release -- --strict` passes with 0 drift.
