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

## AI Execution Rules (Non-Negotiable)

These rules MUST be followed by any generated or modified code.

### Code Quality

* No `NotImplementedException`
* No placeholders or incomplete implementations
* Code must be production-ready

### Compilation

* Zero warnings in `Release` builds
* `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` must be respected

### Async & Concurrency

* Never use `.Result` or `.Wait()`
* Always use `async/await`
* Always propagate `CancellationToken`
* Never ignore cancellation

### Class Design

* Classes must be `sealed` by default
* Only allow inheritance when explicitly required by architecture

### Public API

* All public types and members must have XML documentation (`///`)

### Architecture Compliance

* Respect Job model (`IJob` and `IJob<T>`)
* Do not introduce unnecessary DTOs
* Do not bypass storage as source of truth
* Do not introduce static/global state (unless explicitly allowed)

### Testing Awareness

* Any new behavior must be testable
* Avoid designs that prevent deterministic testing

---

These rules override convenience or shortcuts.

---

## Engineering Rules

See `CONTRIBUTING.md`

