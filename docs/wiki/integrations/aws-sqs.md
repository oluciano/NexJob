---
title: "NexJob AWS SQS Trigger: SQS Queue Messages to Jobs"
sidebarTitle: "AWS SQS"
description: "Use NexJob.Trigger.AwsSqs to turn the messages of an Amazon SQS queue into NexJob jobs, with its options, defaults, visibility timeout handling and how failures are handled."
---

The AWS SQS trigger long-polls an Amazon SQS queue and enqueues each message as a NexJob job. The message body becomes the job's input, as a `string`. For what all triggers have in common (the message contract, the idempotency key and the error model), see the [overview](triggers.md).

## Install

```bash
dotnet add package NexJob.Trigger.AwsSqs
```

## Register the trigger

Bind every message of the queue to one job type with the generic overload:

```csharp
using NexJob.Trigger.AwsSqs;

builder.Services.AddNexJobAwsSqsTrigger<ProcessOrderJob>(options =>
{
    options.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/orders";
});
```

`ProcessOrderJob` must implement `IJob<string>`: it receives the message body as text and decides how to parse it.

Without the generic argument you must set `JobName` yourself, to the assembly-qualified name of the job:

```csharp
builder.Services.AddNexJobAwsSqsTrigger(options =>
{
    options.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/orders";
    options.JobName = typeof(ProcessOrderJob).AssemblyQualifiedName!;
});
```

Unlike the other brokers, this trigger has **one job type per trigger**: it does not read the job type from a message attribute. To send different messages to different jobs, register one trigger per queue.

The same registration is available on the builder as `AddNexJob().AddAwsSqsTrigger<ProcessOrderJob>(...)`.

### AWS credentials and region

By default the trigger creates `new AmazonSQSClient()`, so credentials and region come from the AWS SDK's default chain (environment variables, the shared credentials file, an instance or task role). To control the client yourself, pass it as the first argument:

```csharp
builder.Services.AddNexJobAwsSqsTrigger<ProcessOrderJob>(
    new AmazonSQSClient(RegionEndpoint.SAEast1),
    options => options.QueueUrl = "https://sqs.sa-east-1.amazonaws.com/123456789012/orders");
```

The credentials need `sqs:ReceiveMessage`, `sqs:DeleteMessage` and `sqs:ChangeMessageVisibility` on the queue.

## Options

| Option | Default | Meaning |
|---|---|---|
| `QueueUrl` | required | The URL of the queue. |
| `JobName` | required | The assembly-qualified job type. Set for you by the generic overload. |
| `MaxMessages` | `10` | Messages requested per receive call (1 to 10). |
| `WaitTimeSeconds` | `20` | How long a receive call waits for a message (long polling, 0 to 20). |
| `VisibilityTimeoutSeconds` | `30` | How long a received message is hidden from other consumers (0 to 43200). |
| `VisibilityExtensionIntervalSeconds` | `15` | How often the trigger renews the visibility while it handles a message. Keep it below `VisibilityTimeoutSeconds`. |
| `TargetQueue` | `"default"` | The NexJob queue the jobs go to. `"default"` is the application's `{prefix}.default` queue ([details](../concepts/default-queue.md)). |
| `JobPriority` | `Normal` | The priority of the jobs. |

The ranges and the two required options are validated when the host starts, so a wrong value fails the start and not the first message.

## How a message becomes a job

- **Input:** the message body, read as text.
- **Job type:** always `JobName`.
- **Idempotency key:** the SQS `MessageId`. It deduplicates only against jobs that are still active; see [the overview](triggers.md#idempotency-keys-by-broker).
- **Trace:** a `traceparent` message attribute, when present, becomes the job's trace parent. The trigger asks SQS for all message attributes.

The messages of one receive call are handled one after the other, and there is no concurrency option: a node enqueues them sequentially. Enqueueing is fast, so this is rarely the limit; the jobs themselves run on the NexJob workers.

## What happens when it fails

| What happened | What the trigger does |
|---|---|
| The job was enqueued | The message is deleted, only after the enqueue succeeded. |
| The enqueue failed, for **any** reason | The message is not deleted. The trigger sets its visibility timeout to `0`, so SQS delivers it again right away. |
| Deleting the message failed | A warning is logged. The message becomes visible again after the visibility timeout; if the first job is still active, the same `MessageId` is not enqueued twice. |
| The receive call failed (network, credentials, a wrong URL) | A warning is logged and the trigger waits 5 seconds before it tries again. |
| The host is stopping | The message in hand is not deleted and becomes visible again after its visibility timeout. |

The trigger does **not** tell a permanent failure from a transient one. A message that can never become a job (for example a `JobName` that this application cannot load) is retried at once, forever, until SQS stops it. What stops it is the **redrive policy** of the queue: configure a dead-letter queue with a `maxReceiveCount` on the SQS queue, or a poison message will be received again in a loop.

!!! warning
    Always set a redrive policy with a dead-letter queue on the source queue. The trigger relies on it to stop a message that always fails.

## Visibility timeout

While a message is being handled, the trigger renews its visibility every `VisibilityExtensionIntervalSeconds` to `VisibilityTimeoutSeconds`, so a slow enqueue is not delivered to another consumer in the meantime. If the renewal itself fails (the receipt handle expired, the message was already deleted), a warning is logged and the renewal stops.

## Listener status

The trigger registers with the [listener registry](triggers.md#live-listener-registry) and appears on the dashboard's `/listeners` page: `Listening` once it starts, `Reconnecting` after a failed receive call, and `Stopped` when it stops. Read from the source: the status is not set back to `Listening` after a failure, so it can stay `Reconnecting` until the trigger restarts, even when the polling has recovered.

This page was written from the source code and then run against LocalStack, an SQS emulator, not against AWS. The run confirmed: a failed enqueue makes the message come back at once (not after the visibility timeout), a poison message is received again in a loop without a redrive policy, a dead-letter queue with `maxReceiveCount` stops it, and the visibility renewal keeps a message hidden while a slow enqueue runs. The listener status after a failure is read from the code only. Real SQS timing and limits are those documented by AWS.

## Try it

The [`NexJob.Sample.CloudTriggers`](https://github.com/oluciano/NexJob/tree/develop/samples/NexJob.Sample.CloudTriggers) sample wires this trigger next to the Azure Service Bus, Google Pub/Sub and Salesforce ones, with `/simulate/*` endpoints to produce messages without the real services.
