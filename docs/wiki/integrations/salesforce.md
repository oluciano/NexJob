---
title: "NexJob Salesforce Pub/Sub API Trigger: Platform Events and CDC to Jobs"
sidebarTitle: "Salesforce Pub/Sub API"
description: "Use NexJob.Trigger.Salesforce to turn Salesforce Platform Events and Change Data Capture events, read over the gRPC Pub/Sub API, into NexJob jobs, with its options, replay ids and failure behavior."
---

The Salesforce trigger subscribes to a topic of the Salesforce **Pub/Sub API** (gRPC, Avro) and enqueues each event as a NexJob job. It reads Platform Events (`/event/...`) and Change Data Capture (`/data/...`) topics. For the older CometD protocol, see [Salesforce Streaming API](salesforce-streaming.md). For what all triggers have in common, see the [overview](triggers.md).

!!! warning
    Known bug: the jobs this trigger enqueues fail when they run ([#481](https://github.com/oluciano/NexJob/issues/481)). The trigger stores the decoded event as a JSON object, but declares the job input as a `string`, so deserializing it throws and every attempt fails, for `SalesforceEventJob` and for any `AddSalesforceTrigger<TJob>` job. Reproduced with a fake Pub/Sub client; not against Salesforce. Until it is fixed, prefer the [Streaming API trigger](salesforce-streaming.md), or treat this one as not ready.

## Install

```bash
dotnet add package NexJob.Trigger.Salesforce
```

## Register the trigger

```csharp
using NexJob.Trigger.Salesforce;

builder.Services.AddSalesforceTrigger<OrderEventJob>(options =>
{
    options.Topic = "/event/OrderEvent__e";
    options.ClientId = builder.Configuration["Salesforce:ClientId"]!;
    options.ClientSecret = builder.Configuration["Salesforce:ClientSecret"]!;
    options.TargetQueue = "default";
});
```

`OrderEventJob` must implement `IJob<string>`. Without the generic argument the trigger enqueues the built-in `SalesforceEventJob`, which does nothing: set `JobType` (an assembly-qualified name) or use the generic overload. There is one job type per trigger; the job type is not read from the event.

The builder form is `AddNexJob().AddSalesforceTrigger<OrderEventJob>(...)`. Keep the client id and secret in a secret store.

!!! warning
    `TargetQueue` defaults to `"salesforce-events"`, not `"default"`. A queue you name is polled only when it is in `NexJobOptions.Queues`. Checked: with the default `Queues`, the jobs were enqueued and no worker ever fetched them; once `salesforce-events` was listed, they were fetched. Either set `TargetQueue = "default"` or add the queue to `Queues`.

## Authentication

The trigger uses the OAuth 2.0 **client credentials** flow of a Connected App (an External Client App with the same flow works the same way, not tried). There is no username-password or JWT option. The token is cached and renewed a minute before it expires, and renewed again when the API answers `Unauthenticated`. The tenant id is read from the token response unless you set `TenantId`.

`AuthEndpoint` must be an `https` URL (plain `http` only for `localhost` or a loopback address), because the credentials are sent to it. For a My Domain or a sandbox, set it to your org's `.../services/oauth2/token` URL.

## Options

| Option | Default | Meaning |
|---|---|---|
| `Topic` | required | The topic, starting with `/`: `/event/OrderEvent__e` or `/data/AccountChangeEvent`. |
| `ClientId`, `ClientSecret` | required | The Connected App's consumer key and secret. |
| `AuthEndpoint` | `https://login.salesforce.com/services/oauth2/token` | The OAuth token URL. |
| `PubSubEndpoint` | `api.pubsub.salesforce.com:7443` | The gRPC endpoint. |
| `TenantId` | `null` | The 18-character org id. Taken from the token response when omitted. |
| `JobType` | `null` | The assembly-qualified job type. Without it, the built-in no-op `SalesforceEventJob` runs. |
| `TargetQueue` | `"salesforce-events"` | The NexJob queue. See the warning above. |
| `JobPriority` | `Normal` | The priority of the jobs. |
| `BatchSize` | `100` | Events requested per batch (1 to 500). |
| `ReplayPreset` | `Latest` | Where to start when no replay id is saved: `Latest`, `Earliest` or `Custom`. |
| `CustomReplayId` | `null` | The replay id bytes. Required when the preset is `Custom`. |
| `FallbackPolicy` | `FailFast` | What to do when Salesforce rejects the saved replay id. See below. |
| `ReplayStoreDirectory` | `./.nexjob/salesforce` | Where the default store keeps the replay id. |
| `DeadLetterQueue` | `null` | A queue that receives a copy of an event that could not be enqueued. |

The options are validated when the host starts: a wrong `Topic`, a missing credential, an unsafe `AuthEndpoint` or a `Custom` preset without an id fail the start.

## How an event becomes a job

- **Input:** the Avro payload, decoded with the event's schema (fetched and cached) into JSON. This is the shape [#481](https://github.com/oluciano/NexJob/issues/481) is about.
- **Idempotency key:** the event's `Id`, or the hex replay id when it has none. It deduplicates only against jobs that are still active; see [the overview](triggers.md#idempotency-keys-by-broker).
- **Trace:** a `traceparent` event header, when present.
- **Tags:** `trigger:salesforce` and `topic:{Topic}`.

Events are handled one at a time, in order.

## Replay ids

Salesforce numbers every event with an opaque **replay id**. After each event is enqueued, the trigger saves its replay id; on the next start it resumes after the saved one, so a restart does not lose or repeat events. Salesforce keeps events for a limited window (the trigger's own documentation says about 24 hours), and an older replay id is rejected.

The default store, `FileReplayIdStore`, writes a file per topic under `ReplayStoreDirectory`, replacing it atomically. The path is relative to the process's working directory, so **in a container it vanishes with the container**, and the trigger then falls back to `ReplayPreset`. For production, keep the directory on a persistent volume, or register your own `IReplayIdStore` (two methods: get and save a replay id for a topic) before calling `AddSalesforceTrigger`:

```csharp
builder.Services.AddSingleton<IReplayIdStore, MyDatabaseReplayIdStore>();
builder.Services.AddSalesforceTrigger<OrderEventJob>(...);
```

`InMemoryReplayIdStore` is also provided; it loses the position on every restart.

When Salesforce rejects the saved replay id (an expired one, an invalid argument), `FallbackPolicy` decides:

| Policy | What happens |
|---|---|
| `FailFast` (default) | A critical log, the status becomes `Faulted`, and the exception ends the trigger's background service. With the default .NET host settings an unhandled background-service exception stops the whole application (not run here). Events published in the gap are not read. |
| `ResetToLatest` | The saved position is dropped and the trigger resumes from the tip. Events in the gap are skipped. |
| `ResetToEarliest` | The saved position is dropped and the trigger resumes from the oldest event Salesforce still keeps. Events may repeat. |

## What happens when it fails

| What happened | What the trigger does |
|---|---|
| The event was enqueued | The replay id is saved, even if the host is stopping at that moment. |
| The enqueue failed | A warning is logged and the replay id is **not** saved, and with `DeadLetterQueue` set, a copy of the job is enqueued there (low priority, key `dlq:{key}`). The trigger then goes on to the next event, so the failed one is not tried again while the process runs. |
| The event cannot be decoded (a schema that cannot be fetched or parsed) | The stream is dropped and the trigger reconnects from the previous replay id, which delivers the same event again, in a loop with a pause that doubles up to 30 seconds. The events behind it do not advance. |
| The stream ends or errors | The trigger reconnects, waiting 1 second, doubling up to 30 seconds. A received event resets the wait. |
| The host is stopping | The trigger stops; the saved replay id is the last enqueued event. |

The failed-enqueue and the decode rows were read from the code; the decode loop was seen with a fake client. If the enqueue fails because the storage is down, the copy in `DeadLetterQueue` goes to the same storage and fails too, so do not count on it for that case.

## Listener status

The trigger registers with the [listener registry](triggers.md#live-listener-registry) and appears on the dashboard's `/listeners` page: `Listening` when it starts, `Reconnecting` after an error, `Faulted` when `FailFast` stops it and `Stopped` on shutdown. Read from the source: the status is not set back to `Listening` after a reconnect.

## Not verified

This page was written from the source and a fake Pub/Sub client. No Salesforce org was used, so the OAuth flow, the real gRPC stream, the Avro decoding of real events and the 24-hour window were not exercised.

## Try it

The [`NexJob.Sample.CloudTriggers`](https://github.com/oluciano/NexJob/tree/develop/samples/NexJob.Sample.CloudTriggers) sample wires this trigger, with a `/simulate/salesforce` endpoint to produce an event without Salesforce.
