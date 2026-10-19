---
title: "NexJob Azure Service Bus Trigger: Queues and Topics to Jobs"
sidebarTitle: "Azure Service Bus"
description: "Use NexJob.Trigger.AzureServiceBus to turn the messages of an Azure Service Bus queue or topic subscription into NexJob jobs, with its options, defaults and how failures are handled."
---

The Azure Service Bus trigger receives the messages of a queue, or of a topic subscription, and enqueues each one as a NexJob job. The message body becomes the job's input, as a `string`. For what all triggers have in common (the message contract, how the job type is resolved, the idempotency key and the error model), see the [overview](triggers.md).

## Install

```bash
dotnet add package NexJob.Trigger.AzureServiceBus
```

## Register the trigger

Bind every message of the entity to one job type with the generic overload:

```csharp
using NexJob.Trigger.AzureServiceBus;

builder.Services.AddNexJobAzureServiceBusTrigger<ProcessOrderJob>(options =>
{
    options.ConnectionString = builder.Configuration["ServiceBus:ConnectionString"]!;
    options.QueueOrTopicName = "orders";
});
```

`ProcessOrderJob` must implement `IJob<string>`: it receives the message body as text and decides how to parse it.

For a **topic**, name the subscription:

```csharp
builder.Services.AddNexJobAzureServiceBusTrigger<ProcessOrderJob>(options =>
{
    options.ConnectionString = builder.Configuration["ServiceBus:ConnectionString"]!;
    options.QueueOrTopicName = "orders-topic";
    options.SubscriptionName = "nexjob";
});
```

To route messages to different job types, omit the generic argument and have the publisher set the `nexjob.job_type` application property to the assembly-qualified name of the job (for example `MyApp.Jobs.ProcessOrderJob, MyApp`):

```csharp
builder.Services.AddNexJobAzureServiceBusTrigger(options =>
{
    options.ConnectionString = builder.Configuration["ServiceBus:ConnectionString"]!;
    options.QueueOrTopicName = "orders";
    options.JobType = typeof(DefaultOrderJob).AssemblyQualifiedName; // used when a message has no property
});
```

The same registration is available on the builder as `AddNexJob().AddAzureServiceBusTrigger<ProcessOrderJob>(...)`. Keep the connection string in a secret store: it is a credential.

## Options

| Option | Default | Meaning |
|---|---|---|
| `ConnectionString` | required | The Service Bus connection string. |
| `QueueOrTopicName` | required | The queue, or the topic when you also set `SubscriptionName`. |
| `SubscriptionName` | `null` | The subscription to read. Required for a topic, leave `null` for a queue. |
| `MaxConcurrentMessages` | `1` | How many messages the processor handles at the same time on this node. |
| `TargetQueue` | `"default"` | The NexJob queue the jobs go to. `"default"` is the application's `{prefix}.default` queue ([details](../concepts/queues.md#the-default-queue)). |
| `JobPriority` | `Normal` | The priority of the jobs. |
| `JobType` | `null` | The job type to use for every message. When set, it always wins over the `nexjob.job_type` property — a publisher cannot override it. When unset, the property is read instead. |

A missing `ConnectionString` or `QueueOrTopicName` fails when the host starts, not when the first message arrives.

!!! note
    The trigger builds its client from the connection string only. There is no overload that takes a `TokenCredential`, so Microsoft Entra authentication is not available. A session-enabled queue or subscription is not handled either (the trigger uses a plain processor, not a session processor); neither case was tried.

## How a message becomes a job

- **Input:** the message body, read as text.
- **Job type:** `JobType` when configured, else the `nexjob.job_type` application property. With neither, the message can never become a job.
- **Idempotency key:** the Service Bus `MessageId`. It deduplicates only against jobs that are still active; see [the overview](triggers.md#idempotency-keys-by-broker).
- **Trace:** a `traceparent` application property, when present, becomes the job's trace parent.

## What happens when it fails

| What happened | What the trigger does |
|---|---|
| The job was enqueued | The message is completed, only after the enqueue succeeded. |
| The message can never become a job (no job type, a malformed payload) | It is dead-lettered with the reason `EnqueueFailed` and the exception message as the description. |
| A transient failure (storage down, network, timeout) | The message is abandoned, so Service Bus delivers it again. The entity's `MaxDeliveryCount` decides when Service Bus dead-letters it if the failure never clears. |
| Abandoning also fails | The message's lock expires and it is delivered again. |
| The host is stopping | The message is not completed and is delivered again after its lock expires. |

Because the message is completed after the enqueue and not before, a crash in between delivers it again; the job must be safe to run twice (see the [overview](triggers.md#idempotency-keys-by-broker)).

## Concurrency

`MaxConcurrentMessages` defaults to `1`, so one node handles one message at a time. Each message only needs a fast enqueue, but at high volume raise it (the `CloudTriggers` sample uses `5`). The messages of one entity are not ordered by NexJob once they are jobs, so do not rely on message order.

## Listener status

The trigger registers with the [listener registry](triggers.md#live-listener-registry) and appears on the dashboard's `/listeners` page: `Listening` once the processor starts, `Reconnecting` when the Service Bus processor reports an error, and `Stopped` when it stops. Read from the source: the status is not set back to `Listening` after an error, so it can stay `Reconnecting` until the trigger restarts, even if the processor has recovered.

## What was checked

Against the official Azure Service Bus emulator (Microsoft's `servicebus-emulator` image with SQL Server 2022), with queues set to a `MaxDeliveryCount` of 3: a message with a job type became a job and was completed after the enqueue; a message without `nexjob.job_type` was dead-lettered with the reason `EnqueueFailed` and a description naming the property; a storage that always failed made the trigger abandon the message three times, after which Service Bus dead-lettered it with `MaxDeliveryCountExceeded`; and `MaxConcurrentMessages` of 1 and of 3 held exactly that many messages in the enqueue at once. The emulator is not the cloud service: limits, timings and topic subscriptions with rules were not run, and the listener status after a processor error is read from the source only. The tests are in `tests/NexJob.Trigger.AzureServiceBus.IntegrationTests` and are not part of the pull request pipeline: they need the emulator image and a SQL Server container.

## Try it

The [`NexJob.Sample.CloudTriggers`](https://github.com/oluciano/NexJob/tree/develop/samples/NexJob.Sample.CloudTriggers) sample wires this trigger next to the AWS SQS, Google Pub/Sub and Salesforce ones, with `/simulate/*` endpoints to produce messages without the real services.
