---
title: "External Triggers: Enqueue Jobs from Message Brokers"
sidebarTitle: "Triggers"
description: "Use NexJob triggers to automatically enqueue jobs from Azure Service Bus, AWS SQS, Google Pub/Sub, Salesforce, RabbitMQ, and Kafka."
---

NexJob triggers bridge external message brokers and your background job pipeline. Each trigger package subscribes to a broker source — a queue, topic, or channel — and converts every incoming message into a persisted NexJob job, delivering it to the dispatcher for execution. The pipeline is: **broker → trigger → `JobRecordFactory` → `IScheduler.EnqueueAsync` → dispatcher**. Your job code receives the raw message body as a string; it decides how to parse it.

---

## Broker Guarantees

All NexJob triggers satisfy five core guarantees regardless of the broker:

1. **At-least-once delivery** — Messages are never silently dropped before enqueue.
2. **Idempotency** — The broker's native message identity is used as the `idempotencyKey`, preventing duplicate jobs on redelivery.
3. **Trace propagation** — W3C `traceparent` is extracted from message headers or attributes and attached to the job record for end-to-end distributed tracing.
4. **Signal after enqueue** — Enqueueing a job automatically signals the dispatcher; no manual wake-up is needed.
5. **Ack only after success** — Messages are acknowledged only after `IScheduler.EnqueueAsync` completes successfully. This means the job is visible before the acknowledgement (or the Salesforce replay id) is written. A stop or crash in that short window delivers the message again, and the idempotency key keeps it from becoming a second job. A Salesforce commit that follows a successful enqueue is not cancelled by a stop.

---

## Message Contract

The message **body** is the job input. Because broker triggers are generic, the input type is always `string`. Your job handler implements `IJob<string>` and deserializes the body as needed:

```csharp
public sealed class ProcessOrderJob : IJob<string>
{
    public async Task ExecuteAsync(string messageBody, CancellationToken ct)
    {
        // messageBody contains the raw broker payload (JSON, XML, CSV, or plain text)
        var order = JsonSerializer.Deserialize<OrderDto>(messageBody);
        // Process...
    }
}
```

!!! note
    Non-JSON message bodies (XML, CSV, plain text) are passed verbatim to your job. The trigger does not attempt to parse or transform the payload.


An optional **`traceparent`** header or attribute carries the W3C trace context for distributed tracing.

---

## Job Type Resolution

Most triggers resolve which job to run using the same three-step precedence:

1. **`nexjob.job_type` header/attribute** on the message — set by the publisher as the assembly-qualified name (e.g. `MyApp.Jobs.ProcessOrderJob, MyApp`).
2. **Subscriber's configured job type** — `options.JobType`, or the generic overload `Add{Broker}Trigger<TJob>()` which sets it for you.
3. **Neither present** — permanent failure: the message can never become a job and is dead-lettered or committed without redelivery (see *Error Handling* below).

---

## Idempotency Keys by Broker

| Trigger | Idempotency Key |
|---|---|
| Kafka | `kafka:{topic}:{partition}:{offset}` |
| RabbitMQ | `MessageId` (no deduplication when blank) |
| Azure Service Bus | `MessageId` |
| Google Pub/Sub | Message ID |
| AWS SQS | Message ID |
| Salesforce Pub/Sub API | Event ID (Replay ID as hex; timestamp when absent) |
| Salesforce Streaming API | `{channel}:{eventId}` (or `{channel}:{replayId}` when absent) |

---

## Supported Triggers


=== "Azure Service Bus"

    #### Install the package

    ```bash
    dotnet add package NexJob.Trigger.AzureServiceBus
    ```

    #### Register the trigger

    Use the generic overload to bind all messages on a queue or topic to a single job type:

    ```csharp
    using NexJob.Trigger.AzureServiceBus;

    builder.Services.AddNexJobAzureServiceBusTrigger<ProcessOrderJob>(options =>
    {
        options.ConnectionString = "Endpoint=sb://...";
        options.QueueOrTopicName = "orders";
    });
    ```

    To route messages to different job types based on the `nexjob.job_type` application property, omit the generic argument:

    ```csharp
    builder.Services.AddNexJobAzureServiceBusTrigger(options =>
    {
        options.ConnectionString = "Endpoint=sb://...";
        options.QueueOrTopicName = "my-topic";
        options.SubscriptionName = "my-sub"; // required for topics
    });
    ```

    The idempotency key is the Service Bus `MessageId`. Transient failures abandon the message so it is redelivered; `MaxDeliveryCount` on the entity controls when it is dead-lettered. Permanent failures (unresolvable job type) dead-letter the message immediately.

=== "AWS SQS"

    #### Install the package

    ```bash
    dotnet add package NexJob.Trigger.AwsSqs
    ```

    #### Register the trigger

    SQS message attributes are not read for the job type — you must configure it explicitly:

    ```csharp
    using NexJob.Trigger.AwsSqs;

    // Preferred: generic overload binds queue to one job type
    builder.Services.AddNexJobAwsSqsTrigger<ProcessOrderJob>(options =>
    {
        options.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/my-queue";
    });

    // Equivalent explicit form:
    // builder.Services.AddNexJobAwsSqsTrigger(options =>
    // {
    //     options.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/my-queue";
    //     options.JobName = typeof(ProcessOrderJob).AssemblyQualifiedName!;
    // });
    ```

    The idempotency key is the SQS message ID. Unacknowledged messages become visible again after the queue's visibility timeout, so transient failures are retried automatically by SQS.

=== "Google Pub/Sub"

    #### Install the package

    ```bash
    dotnet add package NexJob.Trigger.GooglePubSub
    ```

    #### Register the trigger

    ```csharp
    using NexJob.Trigger.GooglePubSub;

    // Preferred: generic overload
    builder.Services.AddNexJobGooglePubSubTrigger<ProcessOrderJob>(options =>
    {
        options.ProjectId = "my-project";
        options.SubscriptionId = "my-subscription";
    });

    // Dynamic routing via nexjob.job_type attribute:
    // builder.Services.AddNexJobGooglePubSubTrigger(options =>
    // {
    //     options.ProjectId = "my-project";
    //     options.SubscriptionId = "my-subscription";
    //     options.JobType = typeof(DefaultEventJob).AssemblyQualifiedName;
    // });
    ```

    The idempotency key is the Pub/Sub message ID assigned by the service. Messages remain unacknowledged on transient failures and are redelivered by Pub/Sub.

=== "Salesforce Pub/Sub API"

    #### Install the package

    ```bash
    dotnet add package NexJob.Trigger.Salesforce
    ```

    #### Register the trigger

    The Salesforce Pub/Sub trigger consumes Change Data Capture (CDC) events and custom Platform Events over bidirectional gRPC streams. It decodes Apache Avro binary payloads to JSON automatically:

    ```csharp
    using NexJob.Trigger.Salesforce;

    // Use the built-in SalesforceEventJob handler
    builder.Services.AddNexJob()
        .AddSalesforceTrigger(options =>
        {
            options.Topic = "/data/ChangeEvents";
            options.ClientId = "3MVG9...";
            options.ClientSecret = "secret...";
            options.TargetQueue = "salesforce-events";
            options.ReplayPreset = SalesforceReplayPreset.Latest;
            options.FallbackPolicy = ReplayFallbackPolicy.ResetToLatest;
        });

    // Or bind to a strongly typed custom job
    builder.Services.AddNexJob()
        .AddSalesforceTrigger<ProcessAccountChangeJob>(options =>
        {
            options.Topic = "/data/AccountChangeEvent";
            options.ClientId = "3MVG9...";
            options.ClientSecret = "secret...";
        });
    ```

    Key capabilities:
    - **Bi-directional gRPC streaming** using the official Salesforce Pub/Sub API protobufs with flow control.
    - **Apache Avro decoding** with in-memory schema caching via `ISalesforceSchemaService`.
    - **Replay ID checkpointing** via `IReplayIdStore` (atomic file-based or in-memory).
    - **Resilient fallback policies**: `FailFast`, `ResetToLatest`, and `ResetToEarliest` for expired offsets.
    - **OAuth2 token caching** with automatic refresh ahead of expiration.

=== "Salesforce Streaming API"

    #### Install the package

    ```bash
    dotnet add package NexJob.Trigger.SalesforceStreaming
    ```

    #### Register the trigger

    The Salesforce Streaming trigger consumes PushTopic events, CDC, and Platform Events over HTTP long-polling using the CometD/Bayeux protocol. Use this for legacy environments without gRPC/HTTP2:

    ```csharp
    using NexJob.Trigger.SalesforceStreaming;

    // Use the built-in SalesforceStreamingEventJob handler
    builder.Services.AddNexJob()
        .AddSalesforceStreamingTrigger(options =>
        {
            options.Channel = "/data/Order__ChangeEvent";
            options.Authentication.AuthType = SalesforceStreamingAuthType.OAuth2ClientCredentials;
            options.Authentication.AuthEndpoint = "https://login.salesforce.com/services/oauth2/token";
            options.Authentication.ClientId = "3MVG9...";
            options.Authentication.ClientSecret = "secret...";
            options.TargetQueue = "salesforce-events";
            options.ReplayPreset = SalesforceStreamingReplayPreset.Latest;
        });

    // Bind to a custom job with username/password auth
    builder.Services.AddNexJob()
        .AddSalesforceStreamingTrigger<ProcessSalesforceOrderJob>(options =>
        {
            options.Channel = "/topic/InvoiceUpdates";
            options.Authentication.AuthType = SalesforceStreamingAuthType.OAuth2UsernamePassword;
            options.Authentication.ClientId = "3MVG9...";
            options.Authentication.ClientSecret = "secret...";
            options.Authentication.Username = "integration@company.com";
            options.Authentication.Password = "Password123";
            options.Authentication.SecurityToken = "TokenXYZ";
            options.DeadLetterQueue = "salesforce-dlq";
        });
    ```

    Key capabilities:
    - **CometD/Bayeux protocol** with replay extension and graceful disconnect.
    - **Multi-auth**: OAuth 2.0 Username-Password, OAuth 2.0 Client Credentials, and direct Session ID / Bearer token.
    - **Replay ID checkpointing** via `IStreamingReplayIdStore` (file-based or in-memory).
    - **Session expiry resilience**: automatic token invalidation and re-handshake on `403::Unknown client`.
    - **Exponential backoff** reconnection loop with configurable delays and multipliers.




---

## Error Handling

Trigger enqueue failures fall into three categories:

**Permanent failure** (missing `nexjob.job_type`, malformed payload): The message can never become a job and redelivery is pointless. No job is created. The trigger dead-letters the message (or commits without redelivery if no dead-letter destination is configured) so the consumer does not stall.

**Job type not found in DI**: The trigger enqueues the job record successfully. The dispatcher fails the job on execution with a clear error. NexJob's normal retry policy applies.

**Transient failure** (storage unavailable, network error, timeout): The message is never lost and never dead-lettered. Kafka retries in place with 1 s → 2 s → 5 s → 10 s → 20 s → 30 s backoff without consuming the next record; RabbitMQ nacks with `requeue: true` after a one-second pause; Azure Service Bus abandons the message for redelivery; SQS and Pub/Sub leave the message unacknowledged. Combined with idempotency keys, this prevents duplicate jobs even under partial failures.

---

## Live Listener Registry

NexJob maintains a centralized, thread-safe `IListenerRegistry` that tracks the real-time operational status of every registered trigger. Each trigger updates its status throughout its lifecycle:

| Status | Meaning |
|---|---|
| `Starting` | Initializing the broker connection and subscribing. |
| `Listening` | Successfully connected and actively consuming messages. |
| `Reconnecting` | Transient connection loss; undergoing automatic reconnect. |
| `Faulted` | Fatal unrecoverable broker error (includes error message). |
| `Stopped` | Host shutdown or graceful deregistration. |

All registered triggers and their live states are visible in the dashboard at the `/listeners` route and summarized in the **Cluster Pipeline Topology Map** on the overview page.

## Try it

The [`NexJob.Sample.CloudTriggers`](https://github.com/oluciano/NexJob/tree/develop/samples/NexJob.Sample.CloudTriggers) sample wires AWS SQS, Azure Service Bus, Google Pub/Sub and Salesforce triggers, with `/simulate/*` endpoints to produce messages without the real services.
