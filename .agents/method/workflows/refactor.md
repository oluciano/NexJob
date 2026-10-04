# Workflow: Refactor

**When:** Improving internal code structure, maintainability, or readability without changing any observable behavior.

---

## 1. Absolute Constraint: Strict Behavioral Parity
A refactor **MUST NOT** alter observable behavior or public API contracts:
- Zero changes to public method signatures or types.
- Zero changes to storage schemas or serialization formats.
- All existing tests MUST pass completely unmodified.

---

## 2. Pre-Flight & Context Warmup Gate (Phase 1)
- **WIP Limit = 1:** Verify no other issue has the `in-progress` label.
- **Mark in-progress:** Label the issue and post kickoff comment.
- **Branch Isolation:** `git checkout -b refactor/<issue-id>-<description>`.

---

## 3. Execution (Phase 3)
- Mode: `.agents/method/modes/02-execution-mode.md`.
- Refactor code in small, incremental steps.
- Maintain StyleCop compliance, XML documentation, and async conventions.
- Do NOT rewrite or modify any existing passing tests.

---

## 4. Stop Conditions (Mandatory Escalation)
- An existing test breaks and requires modification → **STOP**. (This indicates behavior changed).
- A public API signature needs to change → **STOP**. (Requires architect approval).
- A storage table/key format needs alteration → **STOP**.

---

## 5. Automated Verification & Exit Criteria
- [ ] All existing unit, integration, and reliability tests pass 100% unmodified
- [ ] Zero compiler warnings in Release build (`TreatWarningsAsErrors = true`)
- [ ] `dotnet format --verify-no-changes` passes cleanly
- [ ] Public API surface unchanged
