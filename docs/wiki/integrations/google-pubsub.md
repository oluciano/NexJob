---
title: "NexJob Google Pub/Sub Trigger: Subscription Messages to Jobs"
sidebarTitle: "Google Pub/Sub"
description: "Use NexJob.Trigger.GooglePubSub to turn the messages of a Google Cloud Pub/Sub subscription into NexJob jobs, with its options, defaults, the local emulator and how failures are handled."
---

The Google Pub/Sub trigger reads a subscription and enqueues each message as a NexJob job. The message data becomes the job's input, as a `string`. For what all triggers have in common (the message contract, how the job type is resolved, the idempotency key and the error model), see the [overview](triggers.md).

## Install

```bash
dotnet add package NexJob.Trigger.GooglePubSub
```

## Register the trigger

Bind every message of the subscription to one job type with the generic overload:

```csharp
using NexJob.Trigger.GooglePubSub;

builder.Services.AddNexJobGooglePubSubTrigger<ProcessOrderJob>(options =>
{
    options.ProjectId = "my-project";
    options.SubscriptionId = "orders-nexjob";
});
```

`ProcessOrderJob` must implement `IJob<string>`: it receives the message data as UTF-8 text and decides how to parse it. The subscription must already exist; the trigger does not create it.

To route messages to different job types, omit the generic argument and have the publisher set the `nexjob.job_type` message attribute to the assembly-qualified name of the job (for example `MyApp.Jobs.ProcessOrderJob, MyApp`). You can also set `JobType` as a default for messages without the attribute; when both exist, the attribute wins:

```csharp
builder.Services.AddNexJobGooglePubSubTrigger(options =>
{
    options.ProjectId = "my-project";
    options.SubscriptionId = "orders-nexjob";
    options.JobType = typeof(DefaultOrderJob).AssemblyQualifiedName; // optional
});
```

The same registration is available on the builder as `AddNexJob().AddGooglePubSubTrigger<ProcessOrderJob>(...)`.

Credentials come from the Google client library's default chain (Application Default Credentials: the `GOOGLE_APPLICATION_CREDENTIALS` file, or the service account of the environment). The trigger has no option for them. The account needs the Pub/Sub Subscriber role on the subscription.

## Options

| Option | Default | Meaning |
|---|---|---|
| `ProjectId` | required | The Google Cloud project that owns the subscription. |
| `SubscriptionId` | required | The subscription to read. |
| `JobType` | `null` | The job type for messages without a `nexjob.job_type` attribute. The attribute wins when both exist. |
| `TargetQueue` | `"default"` | The NexJob queue the jobs go to. `"default"` is the application's `{prefix}.default` queue ([details](../concepts/default-queue.md)). |
| `JobPriority` | `Normal` | The priority of the jobs. |
| `EmulatorHost` | `null` | A host such as `localhost:8085`. When set, the trigger connects to the Pub/Sub emulator without TLS or credentials. |

A missing `ProjectId` or `SubscriptionId` fails when the host starts. There is no concurrency option: the trigger uses the Google client's own flow-control defaults.

## How a message becomes a job

- **Input:** the message data, read as UTF-8 text.
- **Job type:** the `nexjob.job_type` attribute, else `JobType`.
- **Idempotency key:** the Pub/Sub `MessageId`. It deduplicates only against jobs that are still active; see [the overview](triggers.md#idempotency-keys-by-broker).
- **Trace:** a `traceparent` message attribute, when present, becomes the job's trace parent.

## What happens when it fails

The trigger answers every message with an acknowledgement or a negative acknowledgement (nack).

| What happened | What the trigger does |
|---|---|
| The job was enqueued | The message is acknowledged, after the enqueue succeeded. |
| The message has no job type (no attribute and no `JobType`) | An error is logged, the trigger waits 2 seconds and nacks. |
| Any other enqueue failure (storage down, a timeout) | A warning is logged and the message is nacked at once. |
| The host is stopping | The message is nacked, so it is delivered again. |

Pub/Sub delivers a nacked message again, so a message that can never become a job comes back about every 2 seconds, and an error is logged each time. Measured against the emulator: a message without a job type was received and logged 5 times in 9 seconds. What stops it is a **dead-letter policy** on the subscription. Configure one, with a maximum delivery attempts, or such a message loops forever.

!!! warning
    Set a dead-letter policy on the subscription. The trigger nacks a message it can never enqueue and relies on Pub/Sub to park it.

## Listener status

The trigger registers with the [listener registry](triggers.md#live-listener-registry) and appears on the dashboard's `/listeners` page: `Listening` once it starts and `Stopped` when it stops. A start that fails at once (for example invalid credentials) is shown as `Faulted`. Read from the source: a failure after the start does not change the status.

## Run it locally with the emulator

Start the emulator and create a topic and a subscription (names of at least 3 characters, and a project id of at least 6):

```bash
docker run -d -p 8085:8085 gcr.io/google.com/cloudsdktool/google-cloud-cli:emulators \
  gcloud beta emulators pubsub start --host-port=0.0.0.0:8085
```

Then set `EmulatorHost = "localhost:8085"`. This page was checked this way: with `JobType` from the generic overload, a message became one job whose input was the message data; with the dynamic registration, a message with the `nexjob.job_type` attribute became a job, and one without it was nacked in the loop described above. The behavior against the real service, and the dead-letter policy, were not run.

## Try it

The [`NexJob.Sample.CloudTriggers`](https://github.com/oluciano/NexJob/tree/develop/samples/NexJob.Sample.CloudTriggers) sample wires this trigger next to the Azure Service Bus, AWS SQS and Salesforce ones, with `/simulate/*` endpoints to produce messages without the real services.
