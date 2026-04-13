# SKILL: nexjob-trigger

## Purpose

This skill defines the canonical pattern for implementing `NexJob.Trigger.*` packages.
Load this before implementing or reviewing any trigger package.

---

## What a Trigger Is

A trigger is an adapter between a message broker and NexJob.
It receives a broker message and translates it into a NexJob job via `IScheduler.EnqueueAsync`.

A trigger is NOT:
- A replacement for storage
- A modification to core (`src/NexJob`)
- A new dispatch mechanism — it feeds the existing one

---

## Package Structure

```
src/
  NexJob.Trigger.{Broker}/
    {Broker}TriggerOptions.cs        // configuration (connection string, queue/topic, prefetch, etc.)
    {Broker}TriggerHandler.cs        // IHostedService — receives messages, calls EnqueueAsync
    {Broker}NexJobExtensions.cs      // AddNexJob{Broker}Trigger() DI extension
    README.md                        // Gemini owns this
```

---

## The Canonical Flow

```csharp
// 1. Receive message from broker (do NOT ack yet)
var message = await broker.ReceiveAsync(ct);

// 2. Extract metadata
var idempotencyKey = message.MessageId;               // broker's native dedup ID
var traceParent = message.Headers["traceparent"];     // W3C trace propagation

// 3. Build the job record via factory
var job = JobRecordFactory.Build<TJob, TInput>(
    input: deserializedPayload,
    queue: options.TargetQueue,
    idempotencyKey: idempotencyKey,
    traceParent: traceParent);

// 4. Enqueue — if this throws, dead-letter the message
try
{
    await scheduler.EnqueueAsync(job, DuplicatePolicy.AllowAfterFailed, ct);
}
catch
{
    await broker.DeadLetterAsync(message, "EnqueueFailed", ct);
    return;
}

// 5. Signal the dispatcher
wakeUpChannel.Signal();

// 6. Ack ONLY after successful enqueue
await broker.AckAsync(message, ct);
```

**Order matters. Ack always comes last.**

---

## Five Guarantees — Every Trigger Must Satisfy All Five

| # | Guarantee | Violation consequence |
|---|---|---|
| 1 | Never silently drop — dead-letter on `EnqueueAsync` failure | Message lost, job never runs |
| 2 | Idempotency — use broker `MessageId` as `IdempotencyKey` | Duplicate jobs on redelivery |
| 3 | Trace propagation — extract `traceparent`, set `JobRecord.TraceParent` | Broken distributed trace |
| 4 | Signal after enqueue — call `JobWakeUpChannel.Signal()` | Latency spike until next poll |
| 5 | Ack after enqueue — never ack before `EnqueueAsync` succeeds | Message lost on crash |

---

## Broker-Specific Notes

### Azure Service Bus
- Extend message lock (`RenewMessageLockAsync`) if job processing may exceed `LockDuration`
- Dead-letter via `DeadLetterMessageAsync(message, reason, description)`
- `MessageId` → idempotency key
- `ApplicationProperties["traceparent"]` → trace parent

### AWS SQS
- Extend visibility timeout (`ChangeMessageVisibilityAsync`) for long-running jobs
- Delete message on success (`DeleteMessageAsync`)
- On failure: do NOT delete — message returns to queue after visibility timeout, then to DLQ after `maxReceiveCount`
- `MessageDeduplicationId` (FIFO) or `MessageId` → idempotency key
- `MessageAttributes["traceparent"]` → trace parent

### RabbitMQ
- `BasicAck` on success
- `BasicNack(requeue: true)` on transient failure
- `BasicNack(requeue: false)` on permanent failure (routes to dead-letter exchange if configured)
- `CorrelationId` → idempotency key
- `IBasicProperties.Headers["traceparent"]` → trace parent
- Configure `prefetchCount` via options — default 1 for safety
- Implement reconnect with exponential backoff

### Kafka
- Commit offset manually AFTER successful `EnqueueAsync` — never auto-commit
- On failure: produce to dead-letter topic, then commit original offset
- `Headers["traceparent"]` → trace parent
- Consumer group must be configurable via options

### Google Pub/Sub
- `Acknowledge` on success
- `ModifyAckDeadline` to extend if needed
- `Nack` (do not acknowledge) on failure — message redelivered by Pub/Sub
- `MessageId` → idempotency key
- `Attributes["traceparent"]` → trace parent
- Support `OrderingKey` via options

---

## DI Registration Pattern

```csharp
// Options pattern — always use IOptions<T>
services.AddOptions<AzureServiceBusTriggerOptions>()
    .BindConfiguration("NexJob:Triggers:AzureServiceBus")
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Register as IHostedService
services.AddHostedService<AzureServiceBusTriggerHandler>();
```

Extension method naming: `AddNexJob{Broker}Trigger(this IServiceCollection services, Action<{Broker}TriggerOptions> configure)`

---

## What NOT to Do

- Do not modify `IStorageProvider` — triggers use `IScheduler`, not storage directly
- Do not modify `JobRecord` struct — use `JobRecordFactory`
- Do not create a new `ActivitySource` or `Meter` — use `NexJobActivitySource` and `NexJobMetrics` from core
- Do not add `nexjob.trigger_source` tag manually — `JobRecordFactory` handles this
- Do not ack before enqueue — ever
- Do not catch `OperationCanceledException` from `EnqueueAsync` and ack — let it propagate

---

## Testcontainers Pattern

Every trigger package requires integration tests using the broker's official container image.

```csharp
// Pattern: one test class per trigger, Testcontainers fixture
public class AzureServiceBusTriggerTests : IAsyncLifetime
{
    // Use Azure Service Bus Emulator image
    // Test: message received → job enqueued → dispatcher picks up
    // Test: EnqueueAsync failure → message dead-lettered
    // Test: duplicate MessageId → DuplicatePolicy handles it
    // Test: traceparent propagated from message header to JobRecord
}
```

Available official images:
- Azure Service Bus: `mcr.microsoft.com/azure-messaging/servicebus-emulator`
- AWS SQS: `localstack/localstack`
- RabbitMQ: `rabbitmq:3-management`
- Kafka: `confluentinc/cp-kafka` or `apache/kafka`
- Google Pub/Sub: `gcr.io/google.com/cloudsdktool/cloud-sdk` (Pub/Sub emulator)
