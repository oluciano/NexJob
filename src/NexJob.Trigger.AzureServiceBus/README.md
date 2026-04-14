# NexJob.Trigger.AzureServiceBus

Azure Service Bus trigger for NexJob. Receives messages from an ASB queue or topic and automatically enqueues them as NexJob jobs.

## Installation

```bash
dotnet add package NexJob.Trigger.AzureServiceBus
```

## Usage

### Queue Setup

```csharp
using NexJob.Trigger.AzureServiceBus;

builder.Services.AddNexJobAzureServiceBusTrigger(options =>
{
    options.ConnectionString = "Endpoint=sb://...";
    options.QueueOrTopicName = "my-queue";
});
```

### Topic and Subscription Setup

```csharp
builder.Services.AddNexJobAzureServiceBusTrigger(options =>
{
    options.ConnectionString = "Endpoint=sb://...";
    options.QueueOrTopicName = "my-topic";
    options.SubscriptionName = "my-sub";
    options.MaxConcurrentMessages = 5;
});
```

## Configuration Options

| Option | Description | Default |
|--------|-------------|---------|
| `ConnectionString` | Azure Service Bus connection string (required) | - |
| `QueueOrTopicName` | Name of the queue or topic (required) | - |
| `SubscriptionName` | Subscription name (required for topics) | null |
| `MaxConcurrentMessages` | Max concurrent messages | 1 |
| `TargetQueue` | NexJob target queue name | "default" |
| `JobPriority` | Job execution priority | Normal |

## Message Contract

The trigger expects messages with the following properties:

- **Body:** The job input as a UTF-8 string (JSON).
- **ApplicationProperties:**
  - `nexjob.job_type`: Assembly-qualified name of the job type (required).
  - `traceparent`: W3C traceparent for distributed tracing (optional).

## Broker Guarantees

This trigger satisfies all 5 NexJob trigger guarantees adapted for Service Bus:

1. **At-least-once delivery** — messages are never lost before enqueue. Uses PeekLock mode.
2. **Lock renewal** — messages stay locked while processing. The SDK handles lock renewal automatically.
3. **Explicit ack** — messages completed (`CompleteAsync`) only after `EnqueueAsync` succeeds.
4. **Dead-letter on failure** — failed enqueues result in `AbandonAsync`, eventually routing to DLQ after max delivery count.
5. **Graceful shutdown** — `CancellationToken` respected, processor stops accepting new messages and waits for in-flight ones.

## Trace Propagation

The trigger extracts W3C `traceparent` from `ApplicationProperties` and propagates it to the `JobRecord`.

```csharp
var message = new ServiceBusMessage(payload);
message.ApplicationProperties["nexjob.job_type"] = typeof(MyJob).AssemblyQualifiedName;
message.ApplicationProperties["traceparent"] = Activity.Current?.Id;

await sender.SendMessageAsync(message);
```

## Known Limitations

- **Fixed input type:** The `inputType` is fixed to `string` because broker triggers receive the message body as text (JSON). Deserializing to a concrete type is the responsibility of the job handler.
