# AI Router

## feature
load: foundation-minimal + feature + architect-mode + execution-mode + validation-mode

## bugfix
load: foundation-minimal + bugfix + execution-mode + validation-mode

## test
load: foundation-minimal + test + validation-mode

## refactor
load: foundation-minimal + refactor + execution-mode + validation-mode

## reliability
load: foundation-minimal + reliability + execution-mode

## release
load: release-mode (04-release-mode.md)

---

## stop conditions (all workflows)
- ambiguous requirement → STOP, ask
- multiple valid approaches → STOP, ask
- scope unclear → STOP, ask
- invariant conflict → STOP, ask

## invariants (never violate)
- storage = source of truth
- dispatcher = stateless
- deadline checked before execution
- dead-letter handler never crashes
- zero warnings in Release build
