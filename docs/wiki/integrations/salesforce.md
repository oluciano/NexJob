---
title: "NexJob Salesforce Pub/Sub API Trigger: Platform Events and CDC to Jobs"
sidebarTitle: "Salesforce Pub/Sub API"
description: "Use NexJob.Trigger.Salesforce to turn Salesforce Platform Events and Change Data Capture events, read over the gRPC Pub/Sub API, into NexJob jobs, with its options, replay ids and failure behavior."
---

The Salesforce trigger subscribes to a topic of the Salesforce **Pub/Sub API** (gRPC, Avro) and enqueues each event as a NexJob job. It reads Platform Events (`/event/...`) and Change Data Capture (`/data/...`) topics. For the older CometD protocol, see [Salesforce Streaming API](salesforce-streaming.md). For what all triggers have in common, see the [overview](triggers.md).

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
| `TargetQueue` | `"default"` | The NexJob queue. A queue you name is polled only when it is in `NexJobOptions.Queues`. |
| `JobPriority` | `Normal` | The priority of the jobs. |
| `BatchSize` | `100` | Events requested per batch (1 to 500). |
| `ReplayPreset` | `Latest` | Where to start when no replay id is saved: `Latest`, `Earliest` or `Custom`. |
| `CustomReplayId` | `null` | The replay id bytes. Required when the preset is `Custom`. |
| `FallbackPolicy` | `FailFast` | What to do when Salesforce rejects the saved replay id. See below. |
| `ReplayStoreDirectory` | `./.nexjob/salesforce` | Where the default store keeps the replay id. |
| `DeadLetterQueue` | `null` | A queue that receives a copy of an event that could not be enqueued. |

The options are validated when the host starts: a wrong `Topic`, a missing credential, an unsafe `AuthEndpoint` or a `Custom` preset without an id fail the start.

## How an event becomes a job

- **Input:** the Avro payload, decoded with the event's schema (fetched and cached) into JSON, handed to the job as a **string** with that JSON text (like every other trigger), so the job is an `IJob<string>` that parses it.
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
| `FailFast` (default) | A critical log, the status becomes `Faulted`, and the exception ends the trigger's background service. With the default .NET host settings an unhandled background-service exception stops the whole application: run against the fake server below, the host was asked to stop and no job was created. Events published in the gap are not read. |
| `ResetToLatest` | The saved position is dropped and the trigger resumes from the tip. Events in the gap are skipped. |
| `ResetToEarliest` | The saved position is dropped and the trigger resumes from the oldest event Salesforce still keeps. Events may repeat. |

## What happens when it fails

| What happened | What the trigger does |
|---|---|
| The event was enqueued | The replay id is saved, even if the host is stopping at that moment. |
| The enqueue failed | A warning is logged and the replay id is **not** saved, and with `DeadLetterQueue` set, a copy of the job is enqueued there (low priority, key `dlq:{key}`). The trigger then goes on to the next event, so the failed one is not tried again while the process runs. |
| The event can never be decoded (corrupted Avro, or no schema id) | An error is logged and, with `DeadLetterQueue` set, the raw payload is enqueued there as a Base64 string (low priority, key `dlq:{key}`, tags `dead-letter` and `decode-failed`). The replay id is then saved, so the event is not delivered again and the events behind it advance. If the copy in `DeadLetterQueue` cannot be enqueued, the replay id is not saved. |
| The schema cannot be fetched (a network or registry failure) | The stream is dropped and the trigger reconnects from the previous replay id, which delivers the same event again, with a pause that doubles up to 30 seconds. The events behind it wait until the schema can be fetched. |
| The stream ends or errors | The trigger reconnects, waiting 1 second, doubling up to 30 seconds. A received event resets the wait. |
| The host is stopping | The trigger stops; the saved replay id is the last enqueued event. |

The rows were checked with a fake Pub/Sub client and against the mock Pub/Sub server of the integration tests. A custom `ISalesforceSchemaService` makes an event skippable by throwing `SalesforcePayloadDecodeException`; any other exception is treated as retryable. If the enqueue fails because the storage is down, the copy in `DeadLetterQueue` goes to the same storage and fails too, so do not count on it for that case.

## Listener status

The trigger registers with the [listener registry](triggers.md#live-listener-registry) and appears on the dashboard's `/listeners` page: `Listening` when it starts, `Reconnecting` after an error, `Faulted` when `FailFast` stops it and `Stopped` on shutdown. Read from the source: the status is not set back to `Listening` after a reconnect.

## Not verified

This page was written from the source, a fake Pub/Sub client and a fake Pub/Sub server in `tests/NexJob.Trigger.Salesforce.IntegrationTests` (OAuth token endpoint and a gRPC stream with Avro events, built from the public proto). The fake server confirmed the end-to-end path, the saved replay id, and the three fallback policies for a replay id answered with `InvalidArgument`: `ResetToLatest` and `ResetToEarliest` resubscribed with an empty replay id and the matching preset and the event became a job, and `FailFast` stopped the host. The fake is our reading of the protocol, not Salesforce: no org was used, so the real OAuth flow, the real gRPC stream, the Avro decoding of real events, what Salesforce actually answers to an expired replay id, and the 24-hour window were not exercised.

## Try it

The [`NexJob.Sample.CloudTriggers`](https://github.com/oluciano/NexJob/tree/develop/samples/NexJob.Sample.CloudTriggers) sample wires this trigger, with a `/simulate/salesforce` endpoint to produce an event without Salesforce.
