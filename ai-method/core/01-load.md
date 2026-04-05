# LOAD.md

## Purpose

Define how "Load X.md" must be interpreted.

This prevents ambiguity between:
- conceptual loading (correct)
- tool-based file access (forbidden in STRICT modes)

---

## Load Semantics

"Load <file>.md" means:

- Apply the rules, constraints, and behavior defined in the file
- Treat the file as already available in memory
- Do NOT attempt to read the file using any tool

---

## Non-Negotiable Rules

- Load is a conceptual operation
- Load MUST NOT trigger:
  - Read
  - FileSearch
  - WebSearch
  - Any external tool

If a model attempts to read a file using a tool:
→ IMMEDIATE FAIL

---

## Execution Modes Interaction

### In EXECUTION / STRICT modes:

- Files listed in "Load" are considered preloaded
- The model must directly apply their rules
- No retrieval is allowed

---

## Allowed Behavior

- Apply constraints from loaded files
- Enforce rules during reasoning and response

---

## Forbidden Behavior

- Calling any tool to access loaded files
- Re-fetching content
- Treating "Load" as a dynamic operation

---

## Failure Conditions

Immediate FAIL if:

- Any tool is used to access loaded files
- The model attempts to retrieve file content
- The model ignores loaded constraints

---

## Example

### Correct

Load:
- LOCAL.md

→ Apply LOCAL.md rules directly

### Incorrect

Load:
- LOCAL.md

→ Calls:
{
  "name": "Read",
  "arguments": { "file_path": "LOCAL.md" }
}

→ FAIL

---

## Design Principle

Load defines behavior, not data retrieval.

The model must behave as if the file was already internalized.

---

## Success Definition

Load is correctly applied when:

- No tools are used
- Rules from loaded files are enforced
- Behavior changes accordingly (e.g., STRICT mode respected)
