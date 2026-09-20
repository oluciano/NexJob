# Grooming & Architectural Debate Guide

This guide outlines how to conduct an effective **Phase 0 Technical Grooming** session for tasks in NexJob.
The goal is to proactively surface ambiguities, trade-offs, and boundary constraints before any implementation begins.

---

## 1. Trigger Packages (`NexJob.Trigger.*`)

When grooming any trigger-related task (e.g., SQS, RabbitMQ, Kafka, Azure Service Bus, Pub/Sub), evaluate the **5 Golden Guarantees**:

| Checkpoint | Architectural Question to Ask | Canonical NexJob Answer |
| :--- | :--- | :--- |
| **1. Never Drop** | What happens if `IScheduler.EnqueueAsync` throws or times out? | Must route the broker message to Dead-Letter (DLQ) with a diagnostic reason. Never drop or swallow. |
| **2. Idempotency** | What is used as the `idempotencyKey`? | Must use the broker's native unique message ID (e.g., `MessageId`, `sqsMessage.MessageId`). |
| **3. Tracing** | How is distributed tracing extracted? | Must extract `traceparent` from headers and assign to `JobRecord.TraceParent`. |
| **4. Signaling** | Does the trigger notify the dispatcher? | `IScheduler.EnqueueAsync` already signals internally. The trigger must NEVER call `_wakeUpChannel.Signal()` directly. |
| **5. Ack Timing** | When is the broker message acknowledged (ack)? | Strictly AFTER `IScheduler.EnqueueAsync` completes successfully. Never ack before enqueue. |

---

## 2. Dashboard & API Tasks (`NexJob.Dashboard*`)

When grooming dashboard or endpoint changes:

1. **Read Replica vs Primary:**
   - Does this query read heavy history or logs?
   - Should it route through `IDashboardStorage` or check for `UseDashboardReadReplica()`?
2. **Authorization & Security:**
   - Is this endpoint exposed publicly or secured via `IDashboardAuthorizationHandler`?
   - Are sensitive job arguments or connection strings redacted in the UI?
3. **Cardinality & Pagination:**
   - Does the query support strict paging (`limit` / `offset` or cursor)?
   - Avoid unbounded queries that load thousands of `JobRecord` instances into memory.

---

## 3. General Backend & Utility Tasks

When grooming general backend or filter tasks:

1. **State Mutation:**
   - Is storage the single source of truth?
   - Ensure the dispatcher remains stateless.
2. **Cancellation & Deadlines:**
   - Are deadlines evaluated before execution begins?
   - Is `CancellationToken` passed through to the underlying job?
3. **Exception Safety:**
   - Do handlers or filters wrap exceptions cleanly?
   - Dead-letter handlers must NEVER crash the polling dispatcher loop.

---

## 4. Grooming Dialogue Structure

When engaging with the user, structure questions clearly and provide recommendations:

```markdown
### 📋 Technical Grooming: [Feature/Task Name]

Antes de iniciar a implementação, identifiquei [N] decisões de design essenciais:

1. **[Aspecto de Arquitetura / Resiliência]**:
   - Opção A (Recomendada): [Descrição do comportamento resiliente]
   - Opção B: [Descrição de alternativa com trade-off]

2. **[Tratamento de Exceção / Edge Case]**:
   - Opção A: [Dead-letter imediato]
   - Opção B: [Reenfileiramento com backoff]

3. **[Critérios de Aceitação (DoD)]**:
   - [ ] Teste N1: [Cenário feliz]
   - [ ] Teste N2: [Cenário de falha]
   - [ ] Teste N3: [Entrada inválida]
```
