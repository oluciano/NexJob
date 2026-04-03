# GEMINI.md

## Project
NexJob is a production-oriented background job processing library for .NET.

## Current Scope
Implemented:
- In-memory storage
- IJob / IJob<T>
- Wake-up dispatch
- deadlineAfter
- Dead-letter handler
- Retry policies
- Scheduling
- Dashboard

Evolving:
- Distributed coordination
- Multi-node consistency
- Storage parity

## Core Principles
- Simplicity first
- Predictability over magic
- Reliability by design
- Developer experience matters

## Non-Negotiable Invariants
- Storage is the single source of truth
- Dispatcher is stateless
- All job state transitions must be persisted
- Deadline must be enforced before execution begins
- Expired jobs never execute
- Dead-letter handlers must never crash the dispatcher
- Wake-up signaling must never block
- Simple jobs must remain simple
- No unnecessary DTO requirements

## Coding Rules
- Zero warnings in Release builds
- Treat warnings as errors
- No placeholders
- No NotImplementedException
- All public APIs must have XML documentation
- Classes sealed by default
- Async/await only
- Never use .Result or .Wait()
- Propagate CancellationToken in all async calls
- Respect existing StyleCop rules

## Behavior Expectations
When analyzing:
- Respect the existing architecture
- Do not propose speculative rewrites
- Prefer incremental evolution

When editing:
- Keep changes minimal and production-safe
- Preserve public behavior unless explicitly asked otherwise
- Do not break invariants
- Do not introduce hidden behavior

When refactoring:
- Prefer clarity over abstraction
- Avoid unnecessary indirection
- Keep runtime guarantees intact

## AI Workflow
Before making changes:
1. Identify the affected invariant
2. Identify runtime risk
3. Prefer the smallest safe change
4. Validate against project rules

## Output Style
- Be direct
- Be precise
- Explain trade-offs briefly
- Prefer concrete implementation over generic advice

## AI Guardrails (Strict)

- Do not propose full rewrites
- Do not introduce new abstractions without clear benefit
- Do not change public contracts unless explicitly requested
- Prefer incremental, low-risk changes

## Engineering Rules

See `CONTRIBUTING.md`
