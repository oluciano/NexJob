# Workflow: Trigger Implementation

**When:** Implementing, updating, or reviewing a message broker trigger package (`NexJob.Trigger.*`, `NexJob.RabbitMQ`, `NexJob.Kafka`).

---

## 1. Trigger Definition & Boundary

A trigger is an adapter between an external message broker and NexJob.
It receives a broker message and translates it into a NexJob job via `IScheduler.EnqueueAsync`.

A trigger is **NOT**:
- A replacement for storage.
- A modification to core (`src/NexJob`).
- A new dispatch mechanism — it feeds the existing storage and scheduler.

**Non-Negotiable Rule:** Trigger packages are external consumers of core.
They interact strictly through `IScheduler.EnqueueAsync(job, DuplicatePolicy.AllowAfterFailed, ct)`.
Triggers must **NEVER** reference or attempt to call `JobWakeUpChannel` (which is `internal` to core; `IScheduler.EnqueueAsync` signals the wake-up channel internally).

---

## 2. Standard Package Structure

```
src/
  NexJob.Trigger.{Broker}/
    {Broker}TriggerOptions.cs        // configuration (connection string, queue/topic, prefetch, etc.)
    {Broker}TriggerHandler.cs        // IHostedService / BackgroundService — receives messages, calls EnqueueAsync
    {Broker}NexJobExtensions.cs      // AddNexJob{Broker}Trigger() DI extension
    README.md                        // Package documentation
```

---

## 3. The Canonical Flow

```csharp
// 1. Receive message from broker (do NOT acknowledge yet)
var message = await broker.ReceiveAsync(ct).ConfigureAwait(false);

// 2. Extract metadata
var idempotencyKey = message.MessageId;               // broker's native dedup ID
var traceParent = message.Headers["traceparent"];     // W3C trace propagation

// 3. Build the job record via factory
var job = JobRecordFactory.Build<TJob, TInput>(
    input: deserializedPayload,
    queue: options.TargetQueue,
    idempotencyKey: idempotencyKey,
    traceParent: traceParent);

// 4. Enqueue — if this throws, dead-letter the broker message
try
{
    // Note: IScheduler.EnqueueAsync internally signals the JobWakeUpChannel.
    // Do NOT attempt to inject or call JobWakeUpChannel directly.
    await scheduler.EnqueueAsync(job, DuplicatePolicy.AllowAfterFailed, ct).ConfigureAwait(false);
}
catch (Exception ex)
{
    logger.LogError(ex, "Failed to enqueue job for broker message {MessageId}", idempotencyKey);
    await broker.DeadLetterAsync(message, "EnqueueFailed", ct).ConfigureAwait(false);
    return;
}

// 5. Acknowledge ONLY after successful enqueue
await broker.AckAsync(message, ct).ConfigureAwait(false);
```

**Order matters. Acknowledgment always comes last.**

---

## 4. Five Mandatory Guarantees

Every trigger must satisfy all five guarantees:

| # | Guarantee | Violation Consequence | Implementation Rule |
|---|---|---|---|
| 1 | **Never silently drop** | Message lost, job never runs | Dead-letter or nack to broker on `EnqueueAsync` failure. |
| 2 | **Idempotency** | Duplicate jobs executed on redelivery | Use broker's native message ID as `IdempotencyKey`. |
| 3 | **Trace propagation** | Broken distributed trace in OpenTelemetry | Extract `traceparent` from broker headers and set on `JobRecord.TraceParent`. |
| 4 | **Immediate dispatch** | High latency until next polling cycle | `IScheduler.EnqueueAsync` signals the wake-up channel internally; never bypass the scheduler. |
| 5 | **Ack after enqueue** | Message lost if worker crashes mid-enqueue | Never ack before `EnqueueAsync` completes successfully. |

---

## 5. Broker-Specific Guidance

### Azure Service Bus (`NexJob.Trigger.AzureServiceBus`)
- Extend message lock (`RenewMessageLockAsync`) if job ingestion requires extended time.
- Dead-letter via `DeadLetterMessageAsync(message, reason, description)`.
- `MessageId` → idempotency key.
- `ApplicationProperties["traceparent"]` → trace parent.

### AWS SQS (`NexJob.Trigger.AwsSqs`)
- Delete message on success (`DeleteMessageAsync`).
- On failure: do NOT delete — message returns to queue after visibility timeout, then routes to DLQ after `maxReceiveCount`.
- `MessageDeduplicationId` (FIFO) or `MessageId` → idempotency key.
- `MessageAttributes["traceparent"]` → trace parent.

### RabbitMQ (`NexJob.RabbitMQ`)
- `BasicAck` on success.
- `BasicNack(requeue: true)` on transient failure.
- `BasicNack(requeue: false)` on permanent failure (routes to dead-letter exchange).
- `CorrelationId` or `MessageId` → idempotency key.
- `IBasicProperties.Headers["traceparent"]` → trace parent.

### Kafka (`NexJob.Kafka`)
- Commit offset manually AFTER successful `EnqueueAsync` — never auto-commit.
- On permanent failure: produce to dead-letter topic, then commit original offset.
- `Headers["traceparent"]` → trace parent.

### Google Pub/Sub (`NexJob.Trigger.GooglePubSub`)
- `Acknowledge` on success.
- `Nack` on failure — message redelivered by Pub/Sub.
- `MessageId` → idempotency key.
- `Attributes["traceparent"]` → trace parent.

### Salesforce Pub/Sub gRPC (`NexJob.Trigger.Salesforce`)
- Process incoming event stream via gRPC.
- Commit replay ID checkpoint only after successful enqueue.
- `Event.Id` / `ReplayId` → idempotency key.

### Salesforce Streaming CometD (`NexJob.Trigger.SalesforceStreaming`)
- Bayeux/CometD client protocol.
- Track replay ID per topic, advance replay ID only after successful enqueue.

---

## 6. Dependency Injection Registration Pattern

```csharp
// Options pattern — always use IOptions<T>
services.AddOptions<BrokerTriggerOptions>()
    .BindConfiguration("NexJob:Triggers:Broker")
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Register as IHostedService
services.AddHostedService<BrokerTriggerHandler>();
```

Extension method naming standard:
`AddNexJob{Broker}Trigger(this IServiceCollection services, Action<{Broker}TriggerOptions>? configure = null)`

---

## 7. Testing Requirements (3N Matrix + Testcontainers)

Triggers require integration tests using Testcontainers or official emulators:
- **N1 (Positive):** Broker receives message → trigger ingests → job is enqueued in storage with expected payload, idempotency key, and trace parent → broker message is acked.
- **N2 (Negative):** Storage throws on `EnqueueAsync` → broker message is dead-lettered / nacked → job is NOT acknowledged.
- **N3 (Invalid Input):** Corrupt message payload, missing ID, or null metadata → handled gracefully without throwing unhandled exception in the background service loop.
