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

> [!NOTE]
> RabbitMQ capabilities have evolved from a simple trigger into a dedicated, unified package: **`NexJob.RabbitMQ`**.
> It provides both **RabbitMQ Triggers (Consumers)** and a **Resilient Outbox Producer** with Publisher Confirms.
> For full configuration, outbox producer examples, and environment variables, see the dedicated guide: **[RabbitMQ Integration (21-RabbitMQ.md)](21-RabbitMQ.md)**.

Installation:
```bash
dotnet add package NexJob.RabbitMQ
```

Usage (Consumer / Trigger):
```csharp
using NexJob;

builder.Services.AddNexJob()
    .AddRabbitMqTrigger(options =>
    {
        options.HostName = "localhost";
        options.QueueName = "nexjob-trigger";
        options.UserName = "guest";
        options.Password = "guest";
    });
```
*(Legacy `AddNexJobRabbitMqTrigger` remains supported for backward compatibility).*

---

## Kafka

> [!NOTE]
> Kafka capabilities have evolved from a simple trigger into a dedicated, unified package: **`NexJob.Kafka`**.
> It provides both **Kafka Triggers (Consumers)** and a **Resilient Outbox Producer**.
> For full configuration, environment variables, and producer patterns, see the dedicated guide: **[Kafka Integration (20-Kafka.md)](20-Kafka.md)**.

Installation:
```bash
dotnet add package NexJob.Kafka
```

Usage (Consumer / Trigger):
```csharp
using NexJob.Kafka;

builder.Services.AddNexJob()
    .AddKafkaTrigger(options =>
    {
        options.BootstrapServers = "localhost:9092";
        options.Topic = "nexjob-jobs";
        options.GroupId = "nexjob-consumer-group";
    });
```
*(Legacy `AddNexJobKafkaTrigger` remains supported for backward compatibility).*

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

## Salesforce Pub/Sub API

Installation:
```bash
dotnet add package NexJob.Trigger.Salesforce
```

Consumes Salesforce Change Data Capture (CDC) events and custom Platform Events over high-throughput bidirectional gRPC streams, automatically decodes Apache Avro binary payloads to JSON, manages Replay ID checkpointing, and enqueues background jobs with zero message loss.

Usage:
```csharp
using NexJob;
using NexJob.Trigger.Salesforce;

// Register trigger with default SalesforceEventJob
builder.Services.AddNexJob()
    .AddSalesforceTrigger(options =>
    {
        options.Topic = "/data/ChangeEvents"; // Standard CDC or Platform Event topic
        options.ClientId = "3MVG9...";
        options.ClientSecret = "secret...";
        options.TargetQueue = "salesforce-events";
        options.ReplayPreset = SalesforceReplayPreset.Latest;
        options.FallbackPolicy = ReplayFallbackPolicy.ResetToLatest;
    });

// Or register with a strongly typed custom job
builder.Services.AddNexJob()
    .AddSalesforceTrigger<ProcessAccountChangeJob>(options =>
    {
        options.Topic = "/data/AccountChangeEvent";
        options.ClientId = "3MVG9...";
        options.ClientSecret = "secret...";
    });
```

Key features:
- **Bi-directional gRPC Streaming**: Uses official Salesforce Pub/Sub API protobufs and flow control.
- **Apache Avro binary decoding**: In-memory schema caching (`ISalesforceSchemaService`) and decoding into JSON payloads.
- **Replay ID Checkpointing**: `IReplayIdStore` with atomic file-based persistence (`FileReplayIdStore`) and in-memory store (`InMemoryReplayIdStore`).
- **Resilient Fallback Policies**: `ReplayFallbackPolicy.FailFast`, `ResetToLatest`, and `ResetToEarliest` handle expired offsets gracefully.
- **OAuth2 Token Caching**: Automatic token acquisition, caching, and refresh ahead of expiration.
- **Distributed Tracing**: Extracts W3C `traceparent` from event headers into `JobRecord.TraceParent`.

---

## Salesforce Streaming API (Legacy / CometD)

Installation:
```bash
dotnet add package NexJob.Trigger.SalesforceStreaming
```

Consumes Salesforce PushTopic events, Change Data Capture (CDC), and Platform Events over HTTP long-polling using the CometD/Bayeux protocol. Designed for legacy environments and orgs connecting via standard Bayeux endpoints without gRPC/HTTP2 requirements.

Usage:
```csharp
using NexJob;
using NexJob.Trigger.SalesforceStreaming;

// Register trigger with default SalesforceStreamingEventJob
builder.Services.AddNexJob()
    .AddSalesforceStreamingTrigger(options =>
    {
        options.Channel = "/data/Order__ChangeEvent";
        options.Authentication.AuthType = SalesforceStreamingAuthType.OAuth2ClientCredentials;
        options.Authentication.AuthEndpoint = "https://login.salesforce.com/services/oauth2/token";
        options.Authentication.ClientId = "3MVG9...";
        options.Authentication.ClientSecret = "secret...";
        options.TargetQueue = "salesforce-events";
        options.ReplayPreset = SalesforceStreamingReplayPreset.Latest;
    });

// Or register with a custom strongly-typed job
builder.Services.AddNexJob()
    .AddSalesforceStreamingTrigger<ProcessSalesforceOrderJob>(options =>
    {
        options.Channel = "/topic/InvoiceUpdates";
        options.Authentication.AuthType = SalesforceStreamingAuthType.OAuth2UsernamePassword;
        options.Authentication.ClientId = "3MVG9...";
        options.Authentication.ClientSecret = "secret...";
        options.Authentication.Username = "integration@company.com";
        options.Authentication.Password = "Password123";
        options.Authentication.SecurityToken = "TokenXYZ";
        options.DeadLetterQueue = "salesforce-dlq";
    });
```

Key features:
- **CometD/Bayeux Protocol**: Standard Bayeux handshake, subscription with replay extension, long-polling connect loop, and graceful disconnect.
- **Multi-Authentication Support**: OAuth 2.0 Username-Password, OAuth 2.0 Client Credentials, and direct Session ID / Bearer token.
- **Replay ID Checkpointing**: `IStreamingReplayIdStore` with atomic file-based persistence (`FileStreamingReplayIdStore`) and memory store (`InMemoryStreamingReplayIdStore`).
- **Session Expiry Resilience**: Automatic token invalidation and re-handshake upon `403::Unknown client` session expiry.
- **Exponential Backoff**: Resilient reconnection loop with configurable delays and backoff multipliers.
- **All 5 Trigger Guarantees**: Guaranteed at-least-once enqueue, broker-native idempotency keys, W3C traceparent propagation, dispatcher signal, and commit replay ID only after enqueue.

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
