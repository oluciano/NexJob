# NEXJOB_AI_CONTEXT_MINIMAL.md

## Core Rules

Storage = source of truth\
Dispatcher = stateless\
All state transitions must be persisted\
No hidden behavior\
Prefer simplicity over magic\
Incremental evolution over rewrite

## Job Model

Use IJob for internal jobs\
Use IJob`<T>`{=html} for external/structured input

Rule: minimal input (identity + intent)

## Job Lifecycle

States: Enqueued Processing Succeeded Failed (retryable) Dead-letter
(terminal) Expired (terminal, never executed)

## Deadline

ExpiresAt defined at enqueue\
Must be checked before execution\
Expired jobs must never run

## Retry

On failure: record attempt retry if allowed otherwise → dead-letter

Dead-letter handler must never crash dispatcher

## Dispatch

Wake-up (local) + polling (fallback)\
Wake-up must be: - bounded - non-blocking

## Storage

Storage owns: - state - retries - schedules - deadlines - history

Never override storage with memory

## Observability

Use persisted truth only\
Expose: - lifecycle - failures - retries - dead-letter - expired -
queues - timing

No fake state

## Dashboard Rules

-   lightweight
-   no rewrite
-   server-side driven
-   no hidden UI state
-   query-based navigation
-   deterministic behavior

## Engineering Rules

-   no placeholders
-   no NotImplementedException
-   production-ready only
-   zero warnings
-   async/await only
-   no .Result / .Wait()
-   propagate CancellationToken
-   sealed classes by default
-   XML docs for public APIs
-   avoid DTO explosion
-   avoid global state

## Prompt Header

Read NEXJOB_AI_CONTEXT_MINIMAL.md before answering.\
Do not violate CLAUDE.md rules.\
Implement only the scope described below.
