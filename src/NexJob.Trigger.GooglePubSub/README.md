# NexJob.Trigger.GooglePubSub

Google Cloud Pub/Sub trigger for NexJob. Receives messages from a Pub/Sub subscription and automatically enqueues them as NexJob jobs.

## Installation

```bash
dotnet add package NexJob.Trigger.GooglePubSub
```

## Usage

### Subscription Setup

```csharp
using NexJob.Trigger.GooglePubSub;

builder.Services.AddNexJobGooglePubSubTrigger(options =>
{
    options.ProjectId = "my-gcp-project";
    options.SubscriptionId = "my-subscription";
    options.TargetQueue = "default";
});
```

### Local Development with Pub/Sub Emulator

```csharp
builder.Services.AddNexJobGooglePubSubTrigger(options =>
{
    options.ProjectId = "local-project";
    options.SubscriptionId = "local-subscription";
    options.EmulatorHost = "localhost:8085";
});
```

## Configuration Options

| Option | Description | Default |
|---|---|---|
| `ProjectId` | Google Cloud Project ID (required) | `""` |
| `SubscriptionId` | Pub/Sub Subscription ID (required) | `""` |
| `TargetQueue` | Target NexJob queue name | `"default"` |
| `JobPriority` | Execution priority for enqueued jobs | `JobPriority.Normal` |
| `EmulatorHost` | Optional emulator endpoint (e.g. `localhost:8085`) | `null` |

## Message Contract

The trigger expects messages with the following properties:

- **Data:** The job input payload as a UTF-8 string (JSON).
- **Attributes:**
  - `nexjob.job_type`: Assembly-qualified name of the job implementation type (required).
  - `traceparent`: W3C traceparent header for distributed tracing (optional).

## Broker Guarantees

This trigger satisfies all canonical NexJob trigger guarantees:

1. **At-least-once delivery** — messages are delivered via pull subscription and acknowledged only after successful enqueue.
2. **Explicit Ack / Nack** — returns `SubscriberClient.Reply.Ack` strictly after `IScheduler.EnqueueAsync` completes. On enqueue failure, returns `SubscriberClient.Reply.Nack` to allow reprocessing.
3. **Trace propagation** — extracts W3C `traceparent` from message attributes and assigns it to `JobRecord.TraceParent`.
4. **Idempotency** — uses native Pub/Sub `MessageId` as the `JobRecord.IdempotencyKey`.
5. **Graceful shutdown** — respects cancellation token during host shutdown, nacking any in-flight messages so they can be picked up after restart.

## Publishing Example

```csharp
var publisher = await PublisherClient.CreateAsync(TopicName.FromProjectTopic("my-gcp-project", "my-topic"));

var message = new PubsubMessage
{
    Data = ByteString.CopyFromUtf8(jsonPayload),
    Attributes =
    {
        ["nexjob.job_type"] = typeof(OrderProcessingJob).AssemblyQualifiedName,
        ["traceparent"] = Activity.Current?.Id ?? string.Empty,
    },
};

await publisher.PublishAsync(message);
```
