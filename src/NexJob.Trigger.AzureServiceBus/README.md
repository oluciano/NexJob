# NexJob.Trigger.AzureServiceBus

Azure Service Bus trigger for NexJob. Receives messages from a Service Bus queue or topic subscription and automatically enqueues them as NexJob jobs.

## Installation

```bash
dotnet add package NexJob.Trigger.AzureServiceBus
```

## Usage

### Queue

```csharp
builder.Services.AddNexJob(options => { ... })
    .AddNexJobAzureServiceBusTrigger(options =>
    {
        options.ConnectionString = "Endpoint=sb://...";
        options.QueueOrTopicName = "my-queue";
        options.TargetQueue = "default";
    });
```

### Topic + Subscription

```csharp
builder.Services.AddNexJob(options => { ... })
    .AddNexJobAzureServiceBusTrigger(options =>
    {
        options.ConnectionString = "Endpoint=sb://...";
        options.QueueOrTopicName = "my-topic";
        options.SubscriptionName = "my-subscription";
        options.MaxConcurrentMessages = 5;
    });
```

## Configuration Options

| Option | Description | Default |
|--------|-------------|---------|
| `ConnectionString` | Azure Service Bus connection string (required) | - |
| `QueueOrTopicName` | Queue name or topic name (required) | - |
| `SubscriptionName` | Subscription name, required for topics (null for queues) | `null` |
| `MaxConcurrentMessages` | Max concurrent messages being processed | 1 |
| `TargetQueue` | NexJob target queue name | "default" |
| `JobPriority` | Job execution priority | Normal |

## Message Contract

The trigger expects messages to contain the assembly-qualified name of the job type in the `ApplicationProperties` and the JSON payload in the message body.

```csharp
await sender.SendMessageAsync(new ServiceBusMessage(jsonPayload)
{
    ApplicationProperties =
    {
        ["nexjob.job_type"] = typeof(MyJob).AssemblyQualifiedName,
        ["traceparent"] = Activity.Current?.Id,
    }
});
```

## Broker Guarantees

This trigger satisfies all 5 NexJob trigger guarantees:

1. **At-least-once delivery** — messages are never lost before enqueue.
2. **Lock renewal** — Service Bus handles message locking during processing.
3. **Explicit complete** — messages are completed only after `EnqueueAsync` succeeds.
4. **Dead-letter on poison** — failed enqueues result in an explicit dead-letter move or redelivery based on ASB policy.
5. **Graceful shutdown** — `CancellationToken` is respected, ensuring in-flight messages are not abandoned.

## Trace Propagation

The trigger extracts W3C `traceparent` from Service Bus `ApplicationProperties` and propagates it to the `JobRecord`. Ensure you set the `traceparent` property when publishing:

```csharp
message.ApplicationProperties["traceparent"] = Activity.Current?.Id;
```

## Known Limitations

- **Fixed input type:** The `inputType` is fixed to `string` because broker triggers receive the message body as text. Deserializing to a concrete type is the responsibility of the job handler. Support for custom `inputType` is planned for v2.x.
