# NexJob.Trigger.AwsSqs

AWS SQS trigger for NexJob. Receives messages from an SQS queue and automatically enqueues them as NexJob jobs.

## Installation

```bash
dotnet add package NexJob.Trigger.AwsSqs
```

## Usage

### Basic Setup

```csharp
using NexJob.Trigger.AwsSqs;

builder.Services.AddNexJobAwsSqsTrigger(options =>
{
    options.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/my-queue";
    options.JobName = typeof(MyJob).AssemblyQualifiedName!;
});
```

### With Custom SQS Client

```csharp
var sqsClient = new AmazonSQSClient(RegionEndpoint.USEast1);

builder.Services.AddNexJobAwsSqsTrigger(sqsClient, options =>
{
    options.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/my-queue";
    options.JobName = typeof(MyJob).AssemblyQualifiedName!;
    options.MaxMessages = 5;
    options.WaitTimeSeconds = 20;
});
```

## Configuration Options

| Option | Description | Default |
|--------|-------------|---------|
| `QueueUrl` | SQS queue URL (required) | - |
| `JobName` | Assembly-qualified job type name (required) | - |
| `MaxMessages` | Max messages per batch (1–10) | 10 |
| `WaitTimeSeconds` | Long polling wait time (0–20) | 20 |
| `VisibilityTimeoutSeconds` | Message visibility timeout | 30 |
| `VisibilityExtensionIntervalSeconds` | Extension loop interval | 15 |
| `TargetQueue` | NexJob target queue name | "default" |
| `JobPriority` | Job execution priority | Normal |

## Broker Guarantees

This trigger satisfies all 5 NexJob trigger guarantees:

1. **At-least-once delivery** — messages are never lost before enqueue
2. **Visibility extension** — messages stay invisible while processing via `ChangeMessageVisibilityAsync`
3. **Explicit ack** — messages deleted only after `EnqueueAsync` succeeds
4. **Dead-letter on poison** — failed messages reappear and eventually route to DLQ via SQS redrive policy
5. **Graceful shutdown** — `CancellationToken` respected, in-flight messages not abandoned

## Trace Propagation

The trigger extracts W3C `traceparent` from SQS message attributes and propagates it to the `JobRecord`. Set the `traceparent` message attribute when sending to SQS:

```csharp
await sqsClient.SendMessageAsync(new SendMessageRequest
{
    QueueUrl = queueUrl,
    MessageBody = payload,
    MessageAttributes = new Dictionary<string, MessageAttributeValue>
    {
        ["traceparent"] = new MessageAttributeValue
        {
            DataType = "String",
            StringValue = Activity.Current?.Id,
        },
    },
});
```

## Known Limitations

- **Fixed input type:** The `inputType` is fixed to `string` because broker triggers receive the message body as text (JSON, XML or plain text). Deserializing to a concrete type is the responsibility of the job handler. Support for custom `inputType` is planned for v2.2.
- **SQS FIFO queues:** Deduplication relies on SQS `MessageId`. For FIFO queues, `MessageDeduplicationId` can also be used — this is not yet exposed as an option.
- **No batching:** Messages are processed one at a time to ensure visibility extension and proper ordering.
