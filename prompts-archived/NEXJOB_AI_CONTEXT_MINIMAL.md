NEXJOB — AI CONTEXT (EXECUTABLE)

Core Invariants (NON-NEGOTIABLE)

- Storage is the single source of truth
- Dispatcher is stateless
- All state transitions MUST be persisted
- No in-memory state can override storage
- Jobs MUST respect deadline BEFORE execution
- Expired jobs MUST NEVER execute
- Dead-letter handlers MUST NEVER crash dispatcher

---

Execution Model

States:
Enqueued → Processing → Succeeded
Processing → Failed → Retry OR Dead-letter
Enqueued → Expired (never executed)

Rules:
- Every transition MUST be persisted
- Failed = retryable
- Dead-letter = terminal
- Expired = terminal (no execution)

---

Job Model

- IJob → internal jobs
- IJob<T> → external input

Rule:
- Input MUST be minimal (identity + intent)
- DO NOT introduce unnecessary DTOs

---

Dispatcher Rules

- MUST be stateless
- MUST NOT cache state
- MUST enforce:
  - deadline
  - retry policy
  - scheduling constraints

---

Storage Rules

Storage owns:
- state
- retries
- scheduling
- deadlines
- history

Rule:
- NEVER bypass storage

---

Retry Rules

- On failure:
  - record attempt
  - evaluate retry
  - reschedule OR dead-letter

---

Observability Rules

- MUST reflect persisted state
- MUST NOT create fake or derived state

---

Engineering Constraints

- NO NotImplementedException
- NO placeholders
- Production-ready code only
- async/await only
- NEVER use .Result or .Wait()
- ALWAYS propagate CancellationToken
- Classes MUST be sealed by default
- Public APIs MUST have XML docs

---

Golden Rule

If behavior is not explicitly defined:
→ DO NOT IMPLEMENT
→ ASK instead
