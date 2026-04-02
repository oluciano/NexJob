# NexJob AI Operating Model

A structured, token-efficient system for AI-assisted development on NexJob.

---

## Purpose

Eliminate AI hallucination, prevent unintended redesign, standardize execution workflows, reduce token usage, and improve predictability of AI outputs.

---

## Quick Start

**For most tasks:**
1. Start with `/ai-method/core/00-foundation-minimal.md`
2. Choose your workflow: `/ai-method/workflows/{type}.md`
3. Use appropriate mode: `/ai-method/modes/{number}.md`

**For complex scenarios:**
- Add `/ai-method/core/00-foundation-extended.md`
- Reference `/ai-method/templates/` for outputs

---

## Structure

### Core Foundation

**Minimal (80% of tasks):**
- `core/00-foundation-minimal.md` — Essential invariants, code quality rules, StyleCop rules

**Extended (complex scenarios):**
- `core/00-foundation-extended.md` — Full architecture, edge cases, detailed constraints

### Modes (How AI Thinks & Acts)

**01-architect-mode.md** — Design without code
- Specify classes, methods, flow
- Validate invariants
- No implementation

**02-execution-mode.md** — Implement exactly what was specified
- Production-ready code
- Strict compliance
- No guessing

**03-validation-mode.md** — Verify compliance and quality
- Architecture checks
- Code quality checks
- Hidden redesign detection
- Test coverage validation

**04-release-mode.md** — Prepare for production
- Release checklist
- Version discipline
- Changelog rules
- Packaging validation

### Workflows (When & How to Use Modes)

**feature.md** — Add new functionality
- Step 1: Architecture (mode 01)
- Step 2: Implementation (mode 02)
- Step 3: Validation (mode 03)

**bugfix.md** — Fix a reported issue
- Root cause analysis
- Minimal fix implementation
- Regression test
- Validation

**test.md** — Add tests for scenarios
- Define test scope
- Implement (real behavior, deterministic)
- Validate coverage

**refactor.md** — Improve code structure
- Bounded scope
- No behavior change
- All original tests pass
- Clarity improved

**reliability.md** — Validate critical scenarios
- Crash recovery, concurrency, deadline enforcement
- Real storage providers, deterministic
- All providers tested

**release.md** — Prepare for production
- Version & CHANGELOG
- Build & test validation
- Package & publish

### Templates (Reusable Formats)

**task-template.md** — Clearly specify work
- Context, requirements, acceptance criteria
- Deliverables, success definition

**architect-output-template.md** — Document architectural designs
- Overview, classes, methods, flow
- Invariant compliance, risks

**execution-handoff-template.md** — Communicate execution results
- Changes summary, deliverables
- Build/test results, compliance

**validation-report-template.md** — Document validation results
- Architecture compliance, code quality
- Test coverage, risk assessment

---

## Token Optimization Strategy

### Context Layering

1. **Minimal Foundation** (always load)
   - 200 tokens
   - Core rules that apply to ALL tasks
   - No explanations, only rules

2. **Extended Foundation** (only when needed)
   - 1000 tokens
   - Full architecture, edge cases
   - Used for complex scenarios only

3. **Workflow + Mode** (compose as needed)
   - 500-1500 tokens
   - Specific instructions for task type
   - Reusable, composable

### Expected Token Usage

| Scenario | Load | Tokens |
|----------|------|--------|
| Simple feature | Minimal + Workflow + Mode 02 | ~1500 |
| Complex feature | Extended + Architect + Mode 02 | ~3000 |
| Validation only | Validation Mode | ~800 |
| Release validation | Release Mode | ~600 |

### Token Savings vs. Monolithic Documents

- Single 10k-token mega-document: **Always load all 10k tokens**
- Modular system: **Load 200-3000 tokens as needed**
- **Savings: 50-95% token reduction per task**

---

## Usage Examples

### Example 1: Simple Feature

```
User: Add deadlineAfter to job enqueue

1. Load minimal foundation
2. Choose workflow: feature.md
3. Execute: Architect (mode 01) → Implementation (mode 02) → Validation (mode 03)
4. Output ready for merge

Total tokens: ~1500
```

### Example 2: Bug Fix

```
User: Jobs execute after deadline expires

1. Load minimal foundation
2. Choose workflow: bugfix.md
3. Root cause analysis
4. Execute: Fix implementation (mode 02) → Validation (mode 03)
5. Output: Fix + regression test

Total tokens: ~1200
```

### Example 3: Release Validation

```
User: Validate release readiness

1. Load release mode (04)
2. Run checklist
3. Output: READY / NOT READY

Total tokens: ~600
```

### Example 4: Complex Distributed Feature

```
User: Add distributed coordination

1. Load minimal + extended foundation
2. Choose workflow: feature.md
3. Execute: Architect (mode 01, with extended) → Implementation (mode 02) → Validation (mode 03)
4. Output: Complete feature design + code

Total tokens: ~3000
```

---

## Core Principles

1. **No Duplication** — Rules defined once, referenced everywhere
2. **Composable** — Mix and match modes, workflows, templates
3. **Explicit** — Clear, precise, no ambiguity
4. **Enforceable** — Checklists and validation gates
5. **Token-Efficient** — Load only what you need

---

## Design Constraints

**Non-Negotiable Invariants:**
- Storage is the single source of truth
- Dispatcher is stateless
- All state transitions must be persisted
- Deadline enforced before execution
- Dead-letter handlers never crash

**Code Quality:**
- Zero compiler warnings (Release build)
- Zero StyleCop violations
- Production-ready code only
- All public APIs documented

---

## Multi-Step AI Workflow

This system is designed for multi-step AI execution:

```
Step 1: Architect Mode
├─ Load foundation + extended (if needed)
├─ Design solution (no code)
├─ Output: Architecture specification

Step 2: Execution Mode
├─ Load foundation + architect output
├─ Implement specification
├─ Output: Code + tests

Step 3: Validation Mode
├─ Load validation mode
├─ Verify all checks
├─ Output: Pass/Fail with issues

Step 4: (Optional) Release Mode
├─ Load release mode
├─ Validate readiness
├─ Output: Release decision
```

Each step uses **only the context it needs**, minimizing token usage across the entire workflow.

---

## When to Use This System

**Use NexJob AI Operating Model when:**
- Implementing features
- Fixing bugs
- Adding tests
- Refactoring code
- Validating releases
- Making architectural decisions

**Don't use when:**
- Asking questions (use ARCHITECTURE.md directly)
- Reading existing code
- General project research

---

## Files in This System

```
ai-method/
├── README.md (this file)
├── core/
│   ├── 00-foundation-minimal.md (load for every task)
│   └── 00-foundation-extended.md (load only if needed)
├── modes/
│   ├── 01-architect-mode.md (design without code)
│   ├── 02-execution-mode.md (implement specification)
│   ├── 03-validation-mode.md (verify compliance)
│   └── 04-release-mode.md (production readiness)
├── workflows/
│   ├── feature.md (add new functionality)
│   ├── bugfix.md (fix reported issues)
│   ├── test.md (add tests)
│   ├── refactor.md (improve code structure)
│   ├── reliability.md (validate critical scenarios)
│   └── release.md (prepare for production)
└── templates/
    ├── task-template.md (specify work clearly)
    ├── architect-output-template.md (document designs)
    ├── execution-handoff-template.md (communicate results)
    └── validation-report-template.md (report validation)
```

---

## Success Metrics

This system is successful when:

✓ AI outputs are predictable and compliant
✓ Architectural invariants are never violated
✓ Token usage is minimized (50-95% reduction)
✓ No hallucination or unintended redesign
✓ Build passes with zero warnings first time
✓ All tests pass on first run
✓ Code review is straightforward (compliant by design)

---

## Version

**NexJob AI Operating Model v1.0**

Date: 2026-04-02
