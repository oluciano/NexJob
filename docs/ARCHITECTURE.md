# NexJob — Architecture

This document defines how NexJob works internally.

It is the authoritative reference for:

* execution model
* data flow
* system responsibilities
* invariants and guarantees

---

# Job Lifecycle

A job transitions through the following states:

* **Enqueued** → waiting to be processed
* **Processing** → currently being executed
* **Succeeded** → execution completed successfully
* **Failed** → execution failed, retry may still occur
* **Dead-letter** → permanently failed after all retries
* **Expired** → not executed before deadline

All state transitions must be persisted in storage.

---

# State Transitions

Typical transitions:

* Enqueued → Processing
* Processing → Succeeded
* Processing → Failed
* Failed → Enqueued (retry)
* Failed → Dead-letter
* Enqueued → Expired

### Notes

* `Failed` represents a retryable state
* `Dead-letter` is a terminal state
* `Expired` is terminal and indicates execution never started

---

# Dispatch Flow

Execution loop:

1. Fetch next eligible job from storage
2. Validate execution constraints:

   * deadline
   * retry eligibility
   * scheduling constraints
3. Create execution scope
4. Execute job
5. Persist result and transition state

---

# Dispatcher Responsibilities

The dispatcher is responsible for:

* fetching jobs from storage
* enforcing runtime constraints
* managing execution concurrency
* creating DI scopes
* invoking job handlers
* persisting execution results

### Non-responsibilities

The dispatcher:

* is NOT the source of truth
* does NOT own job state
* does NOT cache authoritative data

It is fully stateless between iterations.

---

# Dependency Injection and Execution Scope

Each job execution runs inside an isolated DI scope.

Implications:

* scoped services behave correctly
* no shared state across executions
* job instances are never reused

---

# Wake-Up Channel

The wake-up channel enables immediate dispatch for local enqueue.

Characteristics:

* bounded channel (capacity = 1)
* collapses multiple signals
* never blocks producers

Behavior:

* local enqueue → immediate wake-up
* no signal → fallback to polling

---

# Deadline Model

Deadline is defined via:

```csharp
deadlineAfter: TimeSpan
```

Behavior:

* calculated at enqueue time
* stored as `ExpiresAt`
* evaluated after fetch and before execution

If expired:

* job is marked as `Expired`
* execution is skipped

---

# Retry Model

Retry is controlled by:

* global policy
* per-job override (`[Retry]`)

Retry includes:

* attempt counting
* scheduling of next execution
* backoff strategy

---

# Error Handling Model

When a job throws:

1. attempt is recorded
2. retry policy is evaluated
3. if retry remains → job is rescheduled
4. if no retries → job becomes dead-letter
5. dead-letter handler is invoked (if registered)

---

# Dead-letter Handling

Triggered when:

* all retries are exhausted

Behavior:

* handler resolved via DI
* executed in isolated scope
* exceptions are logged and swallowed

---

# Type Resolution

Job types are resolved using runtime metadata.

Implications:

* types must exist at runtime
* renames can break execution
* versioning must be handled carefully

---

# Recurring Jobs

Recurring jobs are schedule definitions, not continuous executions.

Behavior:

* each occurrence creates a new job instance
* concurrency is controlled per schedule
* guarantees depend on storage capabilities

---

# Concurrency Model

Controlled by:

* worker count
* queue configuration
* throttling rules (`[Throttle]`)
* execution windows

Properties:

* parallel execution
* bounded concurrency
* no global locks

---

# Storage

Storage is responsible for:

* job state
* scheduling metadata
* retries and attempts
* deadlines
* failure tracking

---

# Storage Authority

Storage is the single source of truth.

This includes:

* current state
* execution history
* scheduling decisions

### Rule

No in-memory optimization may override storage truth.

---

# Provider Capability Differences

Different providers may vary in:

* dequeue fairness
* locking guarantees
* recurring coordination
* scheduling precision

Architecture must tolerate these differences.

---

# Observability

NexJob exposes signals for:

* job execution rate
* failures
* retries
* expired jobs
* queue depth
* dispatcher health

Observability must reflect persisted state whenever possible.

---

# Performance Model

Optimizations:

* wake-up signaling (eliminates idle delay)
* bounded concurrency
* minimal locking

Expected behavior:

* local enqueue → immediate execution attempt
* idle system → no CPU waste

---

# Invariants

The following invariants must always hold:

* a job must never execute after expiration
* dead-letter handlers must never crash dispatcher
* wake-up signaling must never block
* storage is the authoritative state
* retries must always be persisted
* dispatcher must remain stateless
* execution must respect cancellation

---

# Tradeoffs

* polling fallback introduces latency in distributed scenarios
* runtime type resolution reduces compile-time safety
* storage determines consistency guarantees
* strict correctness may reduce raw throughput

---

# Design Philosophy

NexJob is designed to be:

* simple for common use
* powerful for complex workflows
* predictable in behavior
* reliable in production

### Rule of thumb

If a feature makes the system harder to reason about, it is likely the wrong design.

