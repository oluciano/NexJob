# NexJob.Trigger.SalesforceStreaming

Salesforce Streaming API (CometD/Bayeux protocol) trigger for NexJob. Connects to Salesforce Streaming API channels (PushTopic, Change Data Capture, Platform Events, and Generic Streaming) over HTTP long-polling, automatically manages Bayeux handshakes and subscriptions, checkpoints Replay IDs, and enqueues background jobs with zero message loss.

## Features

- **Salesforce Streaming API (CometD/Bayeux)**: Standard Bayeux protocol client over HTTP long-polling compatible with PushTopic (`/topic/*`), CDC (`/data/*`), Platform Events (`/event/*`), and Generic Streaming (`/u/*`).
- **Flexible Multi-Authentication**:
  - **OAuth 2.0 Username-Password Flow**: Integration user credentials with optional security token.
  - **OAuth 2.0 Client Credentials Flow**: Modern server-to-server Connected App authorization.
  - **Direct Session ID / Pre-generated Bearer Token**: Seamless integration with existing session tokens.
- **Resilient Replay ID Checkpointing**: `IStreamingReplayIdStore` with atomic file-based persistence (`FileStreamingReplayIdStore`) and memory-only storage (`InMemoryStreamingReplayIdStore`).
- **Replay Presets**:
  - `SalesforceStreamingReplayPreset.Latest` (`-1`): Receives new events created after subscription.
  - `SalesforceStreamingReplayPreset.Earliest` (`-2`): Replays all available retained events in the 24/72-hour window.
  - `SalesforceStreamingReplayPreset.Custom`: Starts from a specific historic Replay ID.
- **Automatic Session Recovery & Exponential Backoff**: Automatically detects Bayeux session expirations (`403::Unknown client`), invalidates cached tokens, and executes fresh handshakes.
- **W3C Distributed Tracing**: Extracted traceparent headers mapped directly to `JobRecord.TraceParent`.
- **Operational Visibility**: Integrates with `IListenerRegistry` to report connection status (`Starting`, `Listening`, `Reconnecting`, `Stopped`) directly to Dashboard `/listeners` and Cluster Topology Map.
- **All 5 Trigger Guarantees**:
  1. *Never silently drop*: Enqueue failure routes to dead-letter queue if configured.
  2. *Idempotency*: Broker-native event keys (`{channel}:{eventId}` or `{channel}:{replayId}`).
  3. *Trace propagation*: Distributed trace context preserved across execution boundaries.
  4. *Signal after enqueue*: Handled automatically by `IScheduler.EnqueueAsync`.
  5. *Ack only after enqueue*: Replay ID committed to storage strictly *after* successful job enqueue.

---

## Installation

```bash
dotnet add package NexJob.Trigger.SalesforceStreaming
```

---

## Quick Start

### 1. Basic Registration with Default Job

```csharp
using NexJob;
using NexJob.Trigger.SalesforceStreaming;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNexJob();

builder.Services.AddSalesforceStreamingTrigger(options =>
{
    options.Channel = "/data/Order__ChangeEvent";
    options.Authentication.AuthType = SalesforceStreamingAuthType.OAuth2ClientCredentials;
    options.Authentication.AuthEndpoint = "https://login.salesforce.com/services/oauth2/token";
    options.Authentication.ClientId = builder.Configuration["Salesforce:ClientId"];
    options.Authentication.ClientSecret = builder.Configuration["Salesforce:ClientSecret"];
    options.TargetQueue = "salesforce-events";
});
```

### 2. Custom Strongly-Typed Job Handler

```csharp
using System.Text.Json;
using NexJob;
using NexJob.Trigger.SalesforceStreaming;

builder.Services.AddSalesforceStreamingTrigger<ProcessSalesforceOrderJob>(options =>
{
    options.Channel = "/data/Order__ChangeEvent";
    options.Authentication.AuthType = SalesforceStreamingAuthType.OAuth2UsernamePassword;
    options.Authentication.ClientId = builder.Configuration["Salesforce:ClientId"];
    options.Authentication.ClientSecret = builder.Configuration["Salesforce:ClientSecret"];
    options.Authentication.Username = builder.Configuration["Salesforce:Username"];
    options.Authentication.Password = builder.Configuration["Salesforce:Password"];
    options.Authentication.SecurityToken = builder.Configuration["Salesforce:SecurityToken"];
    options.ReplayPreset = SalesforceStreamingReplayPreset.Earliest;
    options.DeadLetterQueue = "salesforce-dlq";
});

public sealed class ProcessSalesforceOrderJob : IJob<SalesforceStreamingEventInput>
{
    public async Task ExecuteAsync(SalesforceStreamingEventInput input, CancellationToken cancellationToken)
    {
        // Access raw JSON payload or structured properties
        var payloadJson = input.GetRawJson();
        var replayId = input.ReplayId;
        var eventId = input.EventId;

        // Process event...
        await Task.Yield();
    }
}
```

### 3. Fluent NexJobBuilder API

```csharp
builder.Services.AddNexJob(options =>
{
    options.Queues = ["default", "salesforce-events", "salesforce-dlq"];
})
.AddSalesforceStreamingTrigger<ProcessSalesforceOrderJob>(options =>
{
    options.Channel = "/event/InvoiceNotification__e";
    options.Authentication.AuthType = SalesforceStreamingAuthType.SessionId;
    options.Authentication.InstanceUrl = "https://mycompany.my.salesforce.com";
    options.Authentication.AccessToken = "00D...";
});
```

---

## Configuration Reference

### `SalesforceStreamingTriggerOptions`

| Property | Type | Description | Default |
|---|---|---|---|
| `Channel` | `string` | Salesforce streaming channel starting with `/` (e.g. `/data/AccountChangeEvent`, `/topic/Invoices`, `/event/Order__e`) | *Required* |
| `Authentication` | `SalesforceStreamingAuthOptions` | Authentication credentials and flow configuration | *Required* |
| `ReplayPreset` | `SalesforceStreamingReplayPreset` | Starting position when no Replay ID is stored (`Latest` = -1, `Earliest` = -2, `Custom` = 0) | `Latest` (-1) |
| `CustomReplayId` | `long?` | Historic Replay ID to start from when `ReplayPreset` is `Custom` | `null` |
| `ReplayIdStore` | `IStreamingReplayIdStore?` | Custom Replay ID store implementation | `null` (uses `FileStreamingReplayIdStore`) |
| `ReplayStoreDirectory` | `string` | Local disk directory for storing Replay ID files | `./.nexjob/salesforce-streaming` |
| `TargetQueue` | `string` | Target NexJob queue name | `"salesforce-streaming"` |
| `JobPriority` | `JobPriority` | Job priority for enqueued events | `JobPriority.Normal` |
| `DeadLetterQueue` | `string?` | Queue for events that failed to enqueue | `null` |
| `CometdVersion` | `string` | Salesforce CometD API version | `"60.0"` |
| `ConnectTimeout` | `TimeSpan` | Timeout for CometD long-polling connect HTTP requests | `120 seconds` |
| `ReconnectDelay` | `TimeSpan` | Initial delay before reconnecting after connection drops | `5 seconds` |
| `MaxReconnectDelay` | `TimeSpan` | Maximum delay between reconnection attempts under exponential backoff | `1 minute` |
| `MaxRetries` | `int` | Maximum consecutive retries before backing off | `5` |

### `SalesforceStreamingAuthOptions`

| Property | Type | Description | Default |
|---|---|---|---|
| `AuthType` | `SalesforceStreamingAuthType` | Auth mechanism (`OAuth2UsernamePassword`, `OAuth2ClientCredentials`, `SessionId`) | `OAuth2UsernamePassword` |
| `AuthEndpoint` | `string` | Salesforce OAuth2 token endpoint URL | `https://login.salesforce.com/services/oauth2/token` |
| `ClientId` | `string?` | Connected App Consumer Key | `null` |
| `ClientSecret` | `string?` | Connected App Consumer Secret | `null` |
| `Username` | `string?` | Integration user username | `null` |
| `Password` | `string?` | Integration user password | `null` |
| `SecurityToken` | `string?` | Integration user security token | `null` |
| `InstanceUrl` | `string?` | Salesforce base instance URL (e.g. `https://na1.salesforce.com`) | `null` |
| `AccessToken` (or `SessionId`) | `string?` | Direct bearer access token or Session ID | `null` |

---

## Architecture & Guarantees

```
┌────────────────────────────────────────────────────────┐
│               Salesforce Streaming API                 │
│         (PushTopic / CDC / Platform Events)            │
└──────────────────────────┬─────────────────────────────┘
                           │ CometD / Bayeux Long-Polling
                           ▼
┌────────────────────────────────────────────────────────┐
│          SalesforceStreamingTriggerHandler             │
│                                                        │
│  1. Handshake (/meta/handshake)                        │
│  2. Subscribe (/meta/subscribe) + Replay Extension     │
│  3. Connect Loop (/meta/connect)                       │
│  4. Extract W3C Traceparent & Idempotency Key          │
│  5. Enqueue Job to NexJob Pipeline                     │
│  6. Checkpoint ReplayId to IStreamingReplayIdStore     │
└──────────────────────────┬─────────────────────────────┘
                           │ EnqueueAsync()
                           ▼
┌────────────────────────────────────────────────────────┐
│                   NexJob Storage                       │
│      (PostgreSQL / SQL Server / Redis / Mongo)         │
└────────────────────────────────────────────────────────┘
```

## License

MIT
