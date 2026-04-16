# External Triggers

NexJob v2 supports external triggers — broker messages that automatically
enqueue NexJob jobs. This enables event-driven job scheduling from various message brokers.

---

## How Triggers Work

Triggers follow a standard pipeline:
`[broker] → trigger package → JobRecordFactory → IScheduler.EnqueueAsync → dispatcher`

1. The **Trigger Package** consumes a message from the broker.
2. It extracts the **Job Type** and **Trace Context** from headers/attributes.
3. It uses `JobRecordFactory` to build a `JobRecord` using the message body as input.
4. It calls `IScheduler.EnqueueAsync` to persist the job.
5. It **Acknowledge (Ack)** the message only after a successful enqueue.

---

## Message Contract

All triggers expect two headers/attributes in the broker message:
- **`nexjob.job_type`**: Assembly-qualified name of the job type (e.g., `MyApp.Jobs.ProcessOrderJob, MyApp`).
- **`traceparent`**: W3C trace context for distributed tracing (optional).

The message **Body** is used as the job input. Since broker triggers are generic, the input type is always `string` (usually JSON). Your job handler should deserialize the body as needed.

---

## Broker Guarantees

All NexJob triggers satisfy 5 core guarantees:
1. **At-least-once delivery**: Messages are never silently dropped before enqueue.
2. **Idempotency**: Uses the broker's native message ID as `idempotencyKey` to prevent duplicate jobs.
3. **Trace propagation**: Extracts `traceparent` from headers to maintain the trace across systems.
4. **Signal after enqueue**: Enqueueing a job automatically signals the dispatcher (no manual wake-up needed).
5. **Ack only after success**: Messages are acknowledged only after `IScheduler.EnqueueAsync` completes successfully.

---

## Azure Service Bus

Installation:
```bash
dotnet add package NexJob.Trigger.AzureServiceBus
```

Usage:
```csharp
using NexJob.Trigger.AzureServiceBus;

builder.Services.AddNexJobAzureServiceBusTrigger(options =>
{
    options.ConnectionString = "Endpoint=sb://...";
    options.QueueOrTopicName = "my-queue"; // or my-topic
    options.SubscriptionName = "my-sub";    // required for topics
});
```

---

## AWS SQS

Installation:
```bash
dotnet add package NexJob.Trigger.AwsSqs
```

Usage:
```csharp
using NexJob.Trigger.AwsSqs;

builder.Services.AddNexJobAwsSqsTrigger(options =>
{
    options.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/my-queue";
});
```

---

## RabbitMQ

Installation:
```bash
dotnet add package NexJob.Trigger.RabbitMQ
```

Usage:
```csharp
using NexJob.Trigger.RabbitMQ;

builder.Services.AddNexJobRabbitMqTrigger(options =>
{
    options.HostName = "localhost";
    options.QueueName = "nexjob-trigger";
    options.UserName = "guest";
    options.Password = "guest";
});
```

---

## Kafka

Installation:
```bash
dotnet add package NexJob.Trigger.Kafka
```

Usage:
```csharp
using NexJob.Trigger.Kafka;

builder.Services.AddNexJobKafkaTrigger(options =>
{
    options.BootstrapServers = "localhost:9092";
    options.Topic = "nexjob-jobs";
    options.GroupId = "nexjob-consumer-group";
});
```

---

## Google Pub/Sub

Installation:
```bash
dotnet add package NexJob.Trigger.GooglePubSub
```

Usage:
```csharp
using NexJob.Trigger.GooglePubSub;

builder.Services.AddNexJobGooglePubSubTrigger(options =>
{
    options.ProjectId = "my-project";
    options.SubscriptionId = "my-subscription";
});
```

---

## Error handling

**Malformed message (missing `nexjob.job_type`):**
The trigger logs a warning and acknowledges (or nacks, depending on broker)
the message. No job is created. The message will not be redelivered.

**Job type not found in DI:**
The trigger enqueues the job record. The dispatcher will fail the job on
execution with a clear error. Retries apply normally.

**Enqueue fails (storage unavailable):**
The message is NOT acknowledged. It will be redelivered by the broker
when the trigger recovers. Combined with idempotency keys, this prevents
duplicate jobs even under partial failures.

---

## Next Steps

- [Idempotency](17-Idempotency.md) — Learn about `DuplicatePolicy`
- [OpenTelemetry](12-OpenTelemetry.md) — Trace jobs across brokers
- [Storage Providers](09-Storage-Providers.md) — Where jobs are persisted
