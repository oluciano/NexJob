# Integration with Existing Docs

This file explains how the NexJob AI Operating Model integrates with existing documentation.

---

## Documentation Hierarchy

```
CLAUDE.md (Project context, automatically loaded)
├── References: ai-method/
│
ARCHITECTURE.md (System design, authoritative reference)
├── Referenced by: foundation-extended.md, all workflows
│
CONTRIBUTING.md (Development setup, engineering standards)
├── Referenced by: all workflows
│
ai-method/ (Execution framework for AI assistants)
├── core/ (Foundation rules)
├── modes/ (How AI thinks and acts)
├── workflows/ (When and how to use modes)
└── templates/ (Reusable formats)
```

---

## What Each Document Does

### CLAUDE.md (Project Context)

**Automatically loaded by Claude Code**

**Contains:**
- Project status and vision
- Core principles
- Job model (IJob, IJob<T>)
- Dispatch model (wake-up channel, polling)
- Design constraints
- Engineering rules reference

**Use for:** Understanding NexJob project context, vision, principles

**Does NOT contain:** Specific AI execution instructions (those are in ai-method/)

---

### ARCHITECTURE.md (System Design)

**Authoritative reference for how NexJob works**

**Contains:**
- Job lifecycle and state transitions
- Dispatch flow (detailed)
- Dispatcher responsibilities
- Deadline model (detailed)
- Retry model (detailed)
- Error handling model
- Dead-letter handling
- Type resolution
- Recurring jobs
- Concurrency model
- Storage authority
- Observability
- Performance model
- Invariants and tradeoffs

**Use for:** Understanding how NexJob functions internally

**Referenced by:** foundation-extended.md, all modes, all workflows

---

### CONTRIBUTING.md (Development Setup)

**How to contribute, test, and develop NexJob**

**Contains:**
- Development setup (git clone, dotnet build, dotnet test)
- Project structure
- Running tests (unit, integration, reliability)
- Adding storage providers
- Branch workflow
- Pull request checklist
- Commit message format
- Non-negotiables (mandatory for every PR)
- Coding conventions
- Testing strategy

**Use for:** Setting up development environment, understanding contribution standards

**Referenced by:** bugfix.md, feature.md, refactor.md, test.md, reliability.md

---

### ai-method/README.md (Execution Framework)

**How AI assistants execute tasks on NexJob**

**Contains:**
- Purpose and structure
- Quick start
- Modes (architect, execution, validation, release)
- Workflows (feature, bugfix, test, refactor, reliability, release)
- Templates (task, architect output, execution handoff, validation report)
- Token optimization strategy
- Usage examples
- Design constraints

**Use for:** Getting started with AI-assisted development on NexJob

---

### ai-method/core/ (Foundation Rules)

**Essential rules that apply to ALL code**

**00-foundation-minimal.md:**
- Core invariants
- Job model
- State transitions
- Execution constraints
- Code quality (enforced at build)
- StyleCop rules
- Golden rule

**Use for:** Every task (load this first, 200 tokens)

**00-foundation-extended.md:**
- Full architecture (references ARCHITECTURE.md)
- Design constraints
- Detailed dispatch flow
- Deadline model (detailed)
- Retry and dead-letter (detailed)
- Job lifecycle (complete)
- DI and execution scope
- Wake-up channel (detailed)
- Concurrency model
- Type resolution
- Recurring jobs
- Provider differences
- Observability
- Performance model
- Testing strategy (references CONTRIBUTING.md)
- Architecture compliance checklist
- Code quality requirements

**Use for:** Complex scenarios, architectural decisions (load only when needed, 1000 tokens)

---

### ai-method/modes/ (How AI Thinks & Acts)

**01-architect-mode.md**
- How to design without coding
- What output looks like
- Validation before output
- Risk flagging

**02-execution-mode.md**
- Execution contract (can't redesign, must be precise)
- Fail-safe behavior (stop and ask)
- Code quality validation
- Test requirements
- When to stop and ask

**03-validation-mode.md (NEW)**
- Architecture compliance checks
- Code quality checks (StyleCop, Sonar)
- Feature-specific checks
- Integration checks
- Hidden redesign red flags
- Test coverage checks
- Validation output format

**04-release-mode.md**
- Release checklist
- Version discipline
- CHANGELOG rules
- Package validation
- Release readiness decision tree
- Blocking issues

**Use for:** Understanding how to approach tasks in different modes

---

### ai-method/workflows/ (When & How to Use Modes)

**feature.md** → Architect → Execute → Validate
**bugfix.md** → Analyze → Execute → Validate
**test.md** → Design → Execute → Validate
**refactor.md** → Scope → Execute → Validate
**reliability.md** → Design → Execute → Validate
**release.md** → Validate → Prepare → Publish

**Use for:** Following structured process for specific task type

---

### ai-method/templates/ (Reusable Formats)

**task-template.md**
- Specify work clearly
- Requirements, acceptance criteria
- Success definition

**architect-output-template.md**
- Document architectural designs
- Classes, methods, flow
- Invariant compliance

**execution-handoff-template.md**
- Communicate execution results
- Changes, deliverables, build/test status

**validation-report-template.md**
- Document validation verification
- Architecture compliance, code quality
- Risk assessment, approval

**Use for:** Standardizing communication across AI steps

---

## How They Work Together

### Simple Feature (Step 1: Architect)

```
User: "Add feature X"
  ↓
AI loads:
  - CLAUDE.md (context)
  - foundation-minimal.md (rules)
  - ARCHITECTURE.md (system design)
  - feature.md (workflow)
  - architect-mode.md (how to design)
  ↓
AI outputs:
  - Design using architect-output-template.md
  - No code, pure specification
```

### Simple Feature (Step 2: Execute)

```
User: "Implement the design from step 1"
  ↓
AI loads:
  - CLAUDE.md (context)
  - foundation-minimal.md (rules)
  - CONTRIBUTING.md (coding conventions)
  - feature.md (workflow)
  - execution-mode.md (how to implement)
  - architect-output-template.md (previous design)
  ↓
AI outputs:
  - Code (production-ready)
  - Tests (deterministic)
  - Using execution-handoff-template.md
```

### Complex Feature (All Steps)

```
User: "Implement distributed coordination"
  ↓
Step 1 - Architect:
  - Load: CLAUDE.md + foundation-minimal + ARCHITECTURE.md + extended
  - Output: Detailed design (architect-output-template.md)
  ↓
Step 2 - Execute:
  - Load: CLAUDE.md + foundation-minimal + CONTRIBUTING.md
  - Input: Architect output
  - Output: Code + tests (execution-handoff-template.md)
  ↓
Step 3 - Validate:
  - Load: validation-mode.md
  - Input: Execution handoff
  - Output: Validation report (validation-report-template.md)
```

### Release

```
User: "Validate release readiness"
  ↓
AI loads:
  - CLAUDE.md (context)
  - CONTRIBUTING.md (conventions, changelog)
  - release-mode.md (checklist)
  - release.md (workflow)
  ↓
AI outputs:
  - Release readiness: READY / NOT READY
  - Issues found (if any)
```

---

## Information Flow

### Task Specification

```
CLAUDE.md (project vision)
    ↓
User task + context
    ↓
foundation-minimal.md (rules that apply)
    ↓
ARCHITECTURE.md (how system works)
    ↓
CONTRIBUTING.md (development standards)
    ↓
Chosen workflow (feature, bugfix, etc.)
    ↓
Chosen mode (architect, execute, validate, release)
    ↓
Relevant template (task, architect output, etc.)
    ↓
AI executes task
```

### Quality Validation

```
foundation-minimal.md (code quality rules)
    ↓
CONTRIBUTING.md (non-negotiables)
    ↓
validation-mode.md (comprehensive checklist)
    ↓
AI validates output
    ↓
validation-report-template.md (document results)
    ↓
PASS / FAIL decision
```

---

## Token Usage Pattern

### What Each Document Costs (Approximate)

| Document | Tokens | When |
|----------|--------|------|
| CLAUDE.md | 1500 | Always (auto-loaded) |
| foundation-minimal | 200 | Every task |
| foundation-extended | 1000 | Complex scenarios only |
| ARCHITECTURE.md | 2000 | Referenced when needed |
| CONTRIBUTING.md | 1000 | Development tasks |
| One workflow | 400 | Per task |
| One mode | 800 | Per step |
| One template | 200 | Per output |

### Typical Task Loading

**Simple feature (Architect):**
```
CLAUDE.md (1500, auto)
+ foundation-minimal (200)
+ ARCHITECTURE.md (2000)
+ feature.md (400)
+ architect-mode.md (800)
= 4900 tokens
```

**Simple feature (Execute):**
```
CLAUDE.md (1500, auto)
+ foundation-minimal (200)
+ CONTRIBUTING.md (1000)
+ feature.md (400)
+ execution-mode.md (800)
= 3900 tokens
```

**Validation:**
```
CLAUDE.md (1500, auto)
+ validation-mode.md (800)
= 2300 tokens
```

**vs. Old System:**
```
CLAUDE.md (4000)
+ feature_prompt (500)
+ AI_EXECUTION (800)
+ claude_architect (600)
+ ai_context (1000)
= 6900 tokens (non-composable, always all-or-nothing)
```

---

## Reference Chain

When an AI assistant is working on a task:

1. **Load:** `CLAUDE.md` (auto-loaded, project context)
2. **Load:** `ai-method/README.md` (navigation)
3. **Choose:** Appropriate workflow (feature, bugfix, test, etc.)
4. **Choose:** Appropriate mode (architect, execute, validate, release)
5. **Reference:** Foundation rules (minimal, then extended if needed)
6. **Reference:** `ARCHITECTURE.md` (system design)
7. **Reference:** `CONTRIBUTING.md` (standards)
8. **Output:** Using relevant template

Each document references the others via explicit links.

---

## No Redundancy

**Information appears exactly once:**

- "Storage is source of truth" → `foundation-minimal.md` line X
- "Use async/await" → `foundation-minimal.md` line Y
- "Deadline enforced before execution" → both `foundation-minimal.md` and `ARCHITECTURE.md` (complementary views)
- "Zero warnings required" → `foundation-minimal.md` and `CONTRIBUTING.md` (same rule, different context)

**Modes reference foundations, not vice versa:**
- `execution-mode.md` says "follow foundation-minimal.md"
- Foundation doesn't know about modes

**Workflows reference modes and foundations:**
- `feature.md` says "use architect-mode.md for step 1"
- Modes don't know about workflows

---

## Updating Documentation

### If Project Constraints Change

**Update:** `foundation-minimal.md` (and extended if needed)
- All modes and workflows automatically respect the change
- Explicit reference ensures consistency

### If Architecture Changes

**Update:** `ARCHITECTURE.md`
- `foundation-extended.md` references it
- All tasks automatically see new architecture

### If Development Process Changes

**Update:** `CONTRIBUTING.md`
- All workflows reference it
- All tasks automatically follow new process

### If Workflow Changes (e.g., "always add reliability tests")

**Update:** Appropriate workflow (e.g., `feature.md`)
- Only that workflow affected
- Other workflows unchanged

---

## Integration Summary

| Document | Purpose | Loaded By | References |
|----------|---------|-----------|-----------|
| CLAUDE.md | Project context | Auto | foundation, ai-method |
| ARCHITECTURE.md | System design | Extended foundation | N/A |
| CONTRIBUTING.md | Dev standards | Workflows | N/A |
| foundation-minimal | Core rules | All tasks | CLAUDE |
| foundation-extended | Complex rules | Complex tasks | ARCHITECTURE |
| architect-mode | Design without code | feature/complex workflows | foundation |
| execution-mode | Implement specification | All execution workflows | foundation |
| validation-mode | Verify compliance | Validation step | foundation |
| release-mode | Production readiness | Release workflow | foundation |
| Workflows | Step-by-step process | User + task type | foundation, modes |
| Templates | Reusable formats | Modes + workflows | N/A |

---

## Conclusion

The NexJob AI Operating Model integrates seamlessly with existing documentation:

- **CLAUDE.md** provides project context (auto-loaded)
- **ARCHITECTURE.md** is the authoritative system design reference
- **CONTRIBUTING.md** defines development standards
- **ai-method/** provides execution framework and task workflows

Together they form a complete, non-redundant, composable system for AI-assisted development on NexJob.
