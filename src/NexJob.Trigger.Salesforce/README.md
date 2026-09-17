# NexJob.Trigger.Salesforce

Salesforce Pub/Sub API trigger for NexJob. Consumes Salesforce Change Data Capture (CDC) events and custom Platform Events over high-throughput bidirectional gRPC streams, automatically decodes Apache Avro binary payloads to JSON, manages Replay ID checkpointing, and enqueues background jobs with zero message loss.

## Features

- **Salesforce Pub/Sub API (gRPC)**: Native streaming support using official Protocol Buffers and bidirectional flow control.
- **Apache Avro Deserialization**: In-memory caching of schema fingerprints (`SchemaId`) and binary decoding to JSON.
- **Resilient Replay ID Checkpointing**: `IReplayIdStore` with atomic file-based persistence (`FileReplayIdStore`) and memory-only storage (`InMemoryReplayIdStore`).
- **Configurable Fallback Policies**: `ReplayFallbackPolicy.FailFast`, `ResetToLatest`, and `ResetToEarliest` handle expired offsets gracefully.
- **OAuth2 Token Caching**: Automatic Client Credentials flow with thread-safe token caching and refresh ahead of expiration.
- **W3C Distributed Tracing**: Automatic extraction of `traceparent` headers mapped directly to `JobRecord.TraceParent`.
- **All 5 Trigger Guarantees**: Guaranteed at-least-once processing, offset commit only after enqueue, idempotency via native event IDs, and dead-letter routing.

---

## Installation

```bash
dotnet add package NexJob.Trigger.Salesforce
```

---

## Quick Start

### 1. Basic Registration

Register the trigger to consume from a CDC topic using the default payload job or a custom job:

```csharp
using NexJob;
using NexJob.Trigger.Salesforce;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNexJob();

builder.Services.AddSalesforceTrigger(options =>
{
    options.Topic = "/data/ChangeEvents"; // Standard CDC topic
    options.ClientId = builder.Configuration["Salesforce:ClientId"]!;
    options.ClientSecret = builder.Configuration["Salesforce:ClientSecret"]!;
    options.TargetQueue = "salesforce-events";
});
```

### 2. Custom Strongly-Typed Job Handler

```csharp
using NexJob;
using NexJob.Trigger.Salesforce;

builder.Services.AddSalesforceTrigger<ProcessAccountChangeJob>(options =>
{
    options.Topic = "/data/AccountChangeEvent";
    options.ClientId = builder.Configuration["Salesforce:ClientId"]!;
    options.ClientSecret = builder.Configuration["Salesforce:ClientSecret"]!;
    options.FallbackPolicy = ReplayFallbackPolicy.ResetToLatest;
});

public sealed class ProcessAccountChangeJob : IJob<string>
{
    public async Task ExecuteAsync(string payloadJson, CancellationToken cancellationToken)
    {
        // payloadJson contains decoded Avro fields as standard JSON
        using var doc = JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;
        
        // Process account modification...
    }
}
```

### 3. Fluent NexJobBuilder API

```csharp
builder.Services.AddNexJob(options =>
{
    options.Queues = ["default", "salesforce-events"];
})
.AddSalesforceTrigger(options =>
{
    options.Topic = "/event/OrderEvent__e";
    options.ClientId = "3MVG9...";
    options.ClientSecret = "secret...";
});
```

---

## Configuration Options

| Property | Type | Description | Default |
|---|---|---|---|
| `Topic` | `string` | Salesforce topic name starting with `/` (e.g. `/data/ChangeEvents`, `/event/OrderEvent__e`) | *Required* |
| `ClientId` | `string` | Connected App OAuth2 Client ID (Consumer Key) | *Required* |
| `ClientSecret` | `string` | Connected App OAuth2 Client Secret (Consumer Secret) | *Required* |
| `AuthEndpoint` | `string` | Salesforce OAuth2 token endpoint URL | `https://login.salesforce.com/services/oauth2/token` |
| `PubSubEndpoint` | `string` | Salesforce Pub/Sub gRPC endpoint | `api.pubsub.salesforce.com:7443` |
| `TargetQueue` | `string` | Target NexJob queue for enqueued jobs | `salesforce-events` |
| `JobPriority` | `JobPriority` | Execution priority for enqueued jobs | `Normal` |
| `TenantId` | `string?` | 18-character Salesforce organization ID (derived if omitted) | `null` |
| `ReplayPreset` | `SalesforceReplayPreset` | Starting offset when no checkpoint exists (`Latest`, `Earliest`, `Custom`) | `Latest` |
| `CustomReplayId` | `byte[]?` | Specific binary Replay ID when preset is `Custom` | `null` |
| `FallbackPolicy` | `ReplayFallbackPolicy` | Action when saved Replay ID is expired (`FailFast`, `ResetToLatest`, `ResetToEarliest`) | `FailFast` |
| `BatchSize` | `int` | Flow control batch size requested per stream roundtrip | `100` |
| `ReplayStoreDirectory` | `string` | Disk directory for `FileReplayIdStore` checkpoints | `./.nexjob/salesforce` |
| `DeadLetterQueue` | `string?` | Queue to route events when initial enqueue fails | `null` |

---

## Replay ID Checkpointing

The trigger maintains your position in the Salesforce event stream by checkpointing the binary `ReplayId` received with each event:

- **`FileReplayIdStore` (Default)**: Writes Replay IDs to disk using atomic rename operations (`File.Move(..., overwrite: true)`), ensuring zero corruption across abrupt restarts.
- **`InMemoryReplayIdStore`**: Thread-safe in-memory store suitable for containerized workers using external offsets or testing.
- **Custom `IReplayIdStore`**: Implement the `IReplayIdStore` interface to persist checkpoints to PostgreSQL, Redis, DynamoDB, or any external store.

---

## Replay Fallback Policies

Salesforce event retention is typically 72 hours. If a worker is offline longer than the retention window, the saved Replay ID expires:

1. **`FailFast` (Default)**: Logs a critical alert and terminates the service to prevent silent event loss.
2. **`ResetToLatest`**: Clears the invalid checkpoint and resumes streaming from current real-time events.
3. **`ResetToEarliest`**: Clears the invalid checkpoint and replays all events still available in the retention window.

---

## Core Guarantees

1. **Never Silently Drop**: All stream errors, deserialization failures, and enqueue faults are logged and routed to the configured `DeadLetterQueue`.
2. **Idempotency**: Broker event ID (`ConsumerEvent.Event.Id`) is assigned to `JobRecord.IdempotencyKey`.
3. **Trace Propagation**: Extracts W3C `traceparent` from `ConsumerEvent.Event.Headers` directly into `JobRecord.TraceParent`.
4. **Signal After Enqueue**: Dispatcher wake-up notification is handled automatically by `IScheduler.EnqueueAsync`.
5. **Ack Only After Enqueue**: The binary Replay ID is committed to `IReplayIdStore` only after successful storage persistence.
