---
title: "NexJob Salesforce Streaming API Trigger: CometD Events to Jobs"
sidebarTitle: "Salesforce Streaming API"
description: "Use NexJob.Trigger.SalesforceStreaming to turn Salesforce PushTopic, Change Data Capture and Platform Events, read over CometD long polling, into NexJob jobs, with its options, replay ids and failure behavior."
---

The Salesforce Streaming trigger reads a channel of the Salesforce **Streaming API** (CometD / Bayeux over HTTP long polling) and enqueues each event as a NexJob job. Use it when gRPC and HTTP/2 are not an option, or for PushTopic channels (`/topic/...`); for Platform Events and Change Data Capture over gRPC, see the [Salesforce Pub/Sub API trigger](salesforce.md). For what all triggers have in common, see the [overview](triggers.md).

## Install

```bash
dotnet add package NexJob.Trigger.SalesforceStreaming
```

## Register the trigger

```csharp
using NexJob.Trigger.SalesforceStreaming;

builder.Services.AddSalesforceStreamingTrigger<OrderEventJob>(options =>
{
    options.Channel = "/event/OrderEvent__e";
    options.TargetQueue = "default";
    options.Authentication.AuthType = SalesforceStreamingAuthType.OAuth2ClientCredentials;
    options.Authentication.ClientId = builder.Configuration["Salesforce:ClientId"]!;
    options.Authentication.ClientSecret = builder.Configuration["Salesforce:ClientSecret"]!;
});
```

Unlike the other triggers, the job does not receive a `string`: it implements **`IJob<SalesforceStreamingEventInput>`**, a record with the event already parsed:

```csharp
public sealed class OrderEventJob : IJob<SalesforceStreamingEventInput>
{
    public Task ExecuteAsync(SalesforceStreamingEventInput input, CancellationToken ct)
    {
        // input.Channel, input.ReplayId, input.CreatedDate, input.EventId, input.Payload (a JsonElement)
        return Task.CompletedTask;
    }
}
```

Without the generic argument the trigger enqueues the built-in `SalesforceStreamingEventJob`, which only logs the event: use it to check the connection. The registration is also available on the builder, as `AddNexJob().AddSalesforceStreamingTrigger<OrderEventJob>(...)`, and as `AddNexJobSalesforceStreamingTrigger` on the service collection. There is one job type per trigger.


## Authentication

Set `Authentication.AuthType`. The default is `OAuth2UsernamePassword`, so set it explicitly.

| `AuthType` | Required settings |
|---|---|
| `OAuth2UsernamePassword` (default) | `ClientId`, `ClientSecret`, `Username`, `Password`; `SecurityToken` when your org needs it. |
| `OAuth2ClientCredentials` | `ClientId`, `ClientSecret`. |
| `SessionId` | `InstanceUrl` and `AccessToken` (an existing session; `SessionId` is an alias for `AccessToken`). The trigger cannot renew it. |

`AuthEndpoint` defaults to `https://login.salesforce.com/services/oauth2/token`. It, and `InstanceUrl`, must be `https` (plain `http` only for a loopback address), because credentials and tokens are sent to them. The settings of the chosen type are validated when the host starts. Keep secrets in a secret store.

## Behind an API gateway

Every CometD call carries `Authorization: Bearer <token>`. When the calls go through an API gateway that expects the credential in another header, rewrite it with a `DelegatingHandler` on the trigger's `HttpClient`: it removes `Authorization` and sends the token under the gateway's header name. Nothing else about the Bayeux exchange changes.

```csharp
public sealed class GatewayApiKeyHandler : DelegatingHandler
{
    private readonly string _headerName;

    public GatewayApiKeyHandler(string headerName) => _headerName = headerName;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is { } auth)
        {
            request.Headers.Authorization = null;
            request.Headers.TryAddWithoutValidation(_headerName, auth.Parameter);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
```

```csharp
builder.Services.AddSalesforceStreamingTrigger<ProcessCdcJob>(options =>
{
    options.Channel = "/data/SomeObjectChangeEvent";
    options.Authentication.AuthType = SalesforceStreamingAuthType.SessionId;
    options.Authentication.InstanceUrl = gatewayBaseUrl;
    options.Authentication.AccessToken = gatewayApiKey; // the handler moves it out of Authorization
});

// The same typed client the trigger registers, so the handler joins its pipeline.
// Call it after AddSalesforceStreamingTrigger.
builder.Services
    .AddHttpClient<ISalesforceBayeuxClient, SalesforceBayeuxClient>()
    .AddHttpMessageHandler(() => new GatewayApiKeyHandler("apiKey"));
```

The same handler is the place for any other header the gateway wants (a correlation id, a route key). The handler is exercised against the Bayeux client in `SalesforceBayeuxClientTests`; the registration against a real gateway was not.

Replace `ISalesforceBayeuxClient` itself only when the protocol behaviour or the transport must differ, not just the HTTP headers.

## Options

| Option | Default | Meaning |
|---|---|---|
| `Channel` | required | The channel, starting with `/`: `/event/OrderEvent__e`, `/data/AccountChangeEvent`, `/topic/MyPushTopic`. |
| `Authentication` | required | See above. |
| `TargetQueue` | `"default"` | The NexJob queue. A queue you name is polled only when it is in `NexJobOptions.Queues`. |
| `JobPriority` | `Normal` | The priority of the jobs. |
| `ReplayPreset` | `Latest` | Where to start when no replay id is saved: `Latest`, `Earliest` or `Custom`. |
| `CustomReplayId` | `null` | Required when the preset is `Custom`. |
| `ReplayIdStore` | `null` | Your own `IStreamingReplayIdStore`. Without one, the file store is used. |
| `ReplayStoreDirectory` | `./.nexjob/salesforce-streaming` | Where the file store keeps the replay id. |
| `ReconnectDelay` | 5 seconds | The first wait before reconnecting after an error. |
| `MaxReconnectDelay` | 1 minute | The cap of the growing wait. Must not be below `ReconnectDelay`. |
| `ConnectTimeout` | 120 seconds | How long one long-polling request stays open. The HTTP client timeout is this plus 30 seconds. |
| `CometdVersion` | `"60.0"` | The API version used in the CometD URL. |
| `DeadLetterQueue` | `null` | A queue that receives a copy of an event that could not be enqueued. |
| `JobType` | `null` (uses `SalesforceStreamingEventJob`) | The job class, an `IJob<SalesforceStreamingEventInput>`. Unlike the other triggers, where `JobType` is a `string?` (assembly-qualified name), here it is a `Type?`: `options.JobType = typeof(MyEventJob);`. |
| `MaxRetries` | `5` | **Has no effect.** It is validated (1 to 100) but nothing reads it. |

## How an event becomes a job

- **Input:** a `SalesforceStreamingEventInput` with the replay id, channel, payload, creation date, event id and schema.
- **Idempotency key:** `{channel}:{eventId}`, or `{channel}:{replayId}` when the event has no id. It deduplicates only against jobs that are still active; see [the overview](triggers.md#idempotency-keys-by-broker).
- **Trace:** this trigger does **not** read a trace header from the event. The job's trace parent is the ambient `Activity.Current`, which in a hosted service is usually empty.
- **Tags:** `trigger:salesforce-streaming` and `channel:{Channel}`.

Events are handled one at a time, in order.

## Replay ids

Each event carries a numeric **replay id**. After an event is enqueued the trigger saves its replay id (when it is greater than 0), and on the next connection it resumes after the saved one. With none saved, `ReplayPreset` decides: `Latest` is new events only, `Earliest` is everything Salesforce still retains, `Custom` is the id you give.

The default store writes one file per channel under `ReplayStoreDirectory`, relative to the process's working directory, so **in a container it vanishes with the container** and the trigger starts from `ReplayPreset` again. Keep the directory on a persistent volume, or pass your own `IStreamingReplayIdStore` (two methods: get and save a `long` for a channel) through `ReplayIdStore`.

Unlike the Pub/Sub API trigger, there is no `FallbackPolicy`: a saved replay id that Salesforce no longer accepts is handled like any other error, with a reconnect. What Salesforce answers in that case was not tried.

## What happens when it fails

| What happened | What the trigger does |
|---|---|
| The event was enqueued | The replay id is saved. |
| The enqueue failed | A warning is logged and the replay id is **not** saved; with `DeadLetterQueue` set, a copy of the job is enqueued there (low priority, key `dlq:{key}`). The trigger goes on to the next event, so the failed one is not tried again while the process runs. |
| The session expired or was refused (`401::`, `402::`, `403::`, `Unknown client`) | The token is discarded and the trigger authenticates and subscribes again after `ReconnectDelay`. |
| Any other error (network, a failed handshake, a timeout) | A warning is logged and the trigger reconnects after `ReconnectDelay`, doubling up to `MaxReconnectDelay`. A received event resets the wait. |
| The host is stopping | The trigger leaves the channel and stops. |

If the enqueue fails because the storage is down, the copy in `DeadLetterQueue` goes to the same storage and fails too, so do not rely on it for that case.

## Listener status

The trigger registers with the [listener registry](triggers.md#live-listener-registry) and appears on the dashboard's `/listeners` page: `Listening` after each successful subscribe (so it returns to `Listening` after a reconnect), `Reconnecting` after an error and `Stopped` on shutdown.

## Not verified

This page was written from the source and then run against a fake: a test server in `tests/NexJob.Trigger.Salesforce.IntegrationTests` that answers the OAuth token endpoint and the CometD handshake, subscribe and connect calls. The run confirmed that an event becomes a job and its replay id is saved, that `Latest` and `Earliest` subscribe with `-1` and `-2`, that a saved replay id wins over the preset, that a `Custom` preset without `CustomReplayId` is refused when the host starts, that a `403::Unknown client` on connect makes the trigger handshake again and resume from the saved replay id, and that refused handshakes are retried. The fake is our reading of the protocol, not Salesforce: no org was used, so the real OAuth flows (username-password, client credentials and a ready-made session id are coded), the real long-polling timings, what Salesforce answers to a replay id it no longer keeps, and the retention window were not exercised.

## Try it

The [`NexJob.Sample.CloudTriggers`](https://github.com/oluciano/NexJob/tree/develop/samples/NexJob.Sample.CloudTriggers) sample covers the other brokers; check its `Program.cs` for how a trigger is wired.
