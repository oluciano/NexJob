---
title: "NexJob Message Brokers: Triggers and Outbox"
sidebarTitle: "Overview"
description: "Use NexJob triggers to automatically enqueue jobs from Azure Service Bus, AWS SQS, Google Pub/Sub, Salesforce, RabbitMQ, and Kafka."
---

NexJob triggers bridge external message brokers and your background job pipeline. Each trigger package subscribes to a broker source — a queue, topic, or channel — and converts every incoming message into a persisted NexJob job, delivering it to the dispatcher for execution. The pipeline is: **broker → trigger → `JobRecordFactory` → `IScheduler.EnqueueAsync` → dispatcher**. Your job code receives the raw message body as a string; it decides how to parse it.


## Brokers at a glance

| Broker | Publishes (outbox) | Idempotency key | The job type comes from | When the enqueue fails |
|---|---|---|---|---|
| **Kafka** | Yes | `kafka:{topic}:{partition}:{offset}` | header `nexjob.job_type`, or `JobType` | Transient: retried in place with a growing pause (1 s up to 30 s) without consuming the next record. Permanent: copied to the dead-letter topic if one is configured, otherwise skipped with an error log. |
| **RabbitMQ** | Yes | `MessageId` (none when blank) | header `nexjob.job_type`, or `JobType` | Transient: `nack` with requeue after 1 s. Permanent: `nack` without requeue. |
| [**Azure Service Bus**](azure-service-bus.md) | No | `MessageId` | application property `nexjob.job_type`, or `JobType` | Transient: the message is abandoned and redelivered. Permanent: dead-lettered. |
| **AWS SQS** | No | `MessageId` | only the `JobName` option | Any failure: the visibility timeout is reset to 0, so the message returns at once. A redrive policy on the queue is what parks it. |
| **Google Pub/Sub** | No | message ID | attribute `nexjob.job_type`, or `JobType` | Transient: `Nack`. Permanent: `Nack` after 2 s, so a dead-letter policy on the subscription can park it. |
| **Salesforce Pub/Sub API** | No | event id (replay id as hex) | the `JobType` option, else the built-in `SalesforceEventJob` | The event is logged and, when `DeadLetterQueue` is set, copied there as a job. Its replay id is not committed. |
| **Salesforce Streaming API** | No | `{channel}:{eventId}` | the `JobType` option, else the built-in `SalesforceStreamingEventJob` | The same as the Pub/Sub API trigger. |

The Salesforce Streaming trigger does not read a trace header from the event; the others do (see [broker guarantees](#broker-guarantees)). Kafka and RabbitMQ can also **publish** messages from your application through a resilient outbox producer (the "Publishes" column); the other brokers only consume. Each broker has its own page with the options, the defaults and how it fails.


### Packages

| Trigger | NuGet package |
|---|---|
| Kafka | `NexJob.Kafka` |
| RabbitMQ | `NexJob.RabbitMQ` |
| Azure Service Bus | `NexJob.Trigger.AzureServiceBus` |
| AWS SQS | `NexJob.Trigger.AwsSqs` |
| Google Pub/Sub | `NexJob.Trigger.GooglePubSub` |
| Salesforce Pub/Sub API | `NexJob.Trigger.Salesforce` |
| Salesforce Streaming API | `NexJob.Trigger.SalesforceStreaming` |

---

## Broker guarantees

All NexJob triggers satisfy five core guarantees regardless of the broker:

1. **At-least-once delivery** — Messages are never silently dropped before enqueue.
2. **Idempotency key** — The broker's native message identity is used as the `idempotencyKey`, so a redelivery **while the first job is still active** (`Enqueued`, `Scheduled`, `Processing` or `AwaitingContinuation`) returns that job instead of creating another. See [what the key does and does not cover](#idempotency-keys-by-broker).
3. **Trace propagation** — W3C `traceparent` is extracted from the message headers or attributes and attached to the job record for end-to-end distributed tracing (all triggers except Salesforce Streaming).
4. **Signal after enqueue** — Enqueueing a job automatically signals the dispatcher; no manual wake-up is needed.
5. **Ack only after success** — Messages are acknowledged only after `IScheduler.EnqueueAsync` completes successfully. This means the job is visible before the acknowledgement (or the Salesforce replay id) is written. A stop or crash in that short window delivers the message again. If the first job is still active, the idempotency key keeps the redelivery from becoming a second job; if it has already finished, a second job is created, so jobs must be safe to run twice. A Salesforce commit that follows a successful enqueue is not cancelled by a stop.

---

## Message contract

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

## Job type resolution

Most triggers resolve which job to run using the same three-step precedence:

1. **`nexjob.job_type` header/attribute** on the message — set by the publisher as the assembly-qualified name (e.g. `MyApp.Jobs.ProcessOrderJob, MyApp`).
2. **Subscriber's configured job type** — `options.JobType`, or the generic overload `Add{Broker}Trigger<TJob>()` which sets it for you. The AWS SQS option is called `JobName`, and SQS reads **no** message attribute: its job type is always the configured one. The two Salesforce triggers fall back to a built-in job when no type is configured.
3. **Neither present** — permanent failure: the message can never become a job and is dead-lettered or committed without redelivery (see *Error Handling* below).

---

## Idempotency keys by broker

| Trigger | Idempotency Key |
|---|---|
| Kafka | `kafka:{topic}:{partition}:{offset}` |
| RabbitMQ | `MessageId` (no deduplication when blank) |
| Azure Service Bus | `MessageId` |
| Google Pub/Sub | Message ID |
| AWS SQS | Message ID |
| Salesforce Pub/Sub API | Event ID (Replay ID as hex; timestamp when absent) |
| Salesforce Streaming API | `{channel}:{eventId}` (or `{channel}:{replayId}` when absent) |

Every trigger enqueues with `DuplicatePolicy.AllowAfterFailed` (see [Idempotency](../concepts/idempotency.md#duplicatepolicy)), so the key deduplicates only against **active** jobs. A message redelivered after its job has finished, for example a Kafka offset replayed after a consumer group reset, or an SQS message redelivered after a slow acknowledgment, creates a new job. Checked with the same key and the same policy: while the first job was active the second call returned the same job, and after it finished it created another, so the job ran twice. Treat triggers as at-least-once and make the jobs idempotent.

---

## Supported triggers

Each broker can be registered in two equivalent ways: on the service collection (`services.AddNexJob{Broker}Trigger<TJob>(...)`, and `AddSalesforceTrigger` for Salesforce) or chained on the builder returned by `AddNexJob()` (`AddNexJob().Add{Broker}Trigger<TJob>(...)`). The tabs below show whichever form each package's own examples use.


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

## Error handling

Trigger enqueue failures fall into three categories:

**Permanent failure** (missing `nexjob.job_type`, malformed payload): The message can never become a job and redelivery is pointless. No job is created. The trigger dead-letters the message (or commits without redelivery if no dead-letter destination is configured) so the consumer does not stall.

**A job type that does not resolve at execution time**: the trigger enqueues the job record successfully, and what happens next depends on why. If the type name cannot be **loaded** in the process that fetches the job, it is treated as a [foreign job](../guides/multi-service.md): deferred every few seconds with no attempt used, a warning logged, and it never fails. If the type loads but is **not registered** in DI, the execution fails with an error and the normal retry policy applies.

**Transient failure** (storage unavailable, network error, timeout): The message is never lost and never dead-lettered. Kafka retries in place with 1 s → 2 s → 5 s → 10 s → 20 s → 30 s backoff without consuming the next record; RabbitMQ nacks with `requeue: true` after a one-second pause; Azure Service Bus abandons the message for redelivery; SQS resets the message's visibility timeout to 0 so it returns at once (it relies on the queue's redrive policy to park a message that never succeeds); Pub/Sub sends a `Nack`. A message can therefore be delivered more than once; the idempotency key only covers redeliveries that arrive while the first job is still active (see above).

---

## Live listener registry

NexJob maintains a centralized, thread-safe `IListenerRegistry` that tracks the real-time operational status of every registered trigger. Each trigger updates its status throughout its lifecycle:

| Status | Meaning |
|---|---|
| `Starting` | Initializing the broker connection and subscribing. |
| `Listening` | Successfully connected and actively consuming messages. |
| `Reconnecting` | Transient connection loss; undergoing automatic reconnect. |
| `Faulted` | Fatal unrecoverable broker error (includes error message). |
| `Stopped` | Host shutdown or graceful deregistration. |

All registered triggers and their live states are visible in the dashboard at the `/listeners` route and summarized in the **Cluster Pipeline Topology Map** on the overview page.

### Reporting from your own listener

A background service of yours can show up on the same page. `IListenerRegistry` is registered by `AddNexJob()`, so inject it, register the listener once and update its status as the connection changes:

```csharp
registry.Register(new ListenerRegistration(
    Id: "billing-events",
    Broker: "MyBroker",
    Endpoint: "mybroker://billing",
    TargetJobType: nameof(ProcessBillingEventJob),
    JobTag: "trigger:mybroker",       // optional
    ConsumerGroup: "billing-workers")); // optional

registry.UpdateStatus("billing-events", ListenerStatus.Listening);
// later, on a connection loss:
registry.UpdateStatus("billing-events", ListenerStatus.Reconnecting, "connection reset");
```

`Get(id)` and `GetAll()` return a snapshot with the status, the description, and when the listener started and last changed state. The registry only reports status: it does not consume messages or enqueue jobs.

## Try it

The [`NexJob.Sample.CloudTriggers`](https://github.com/oluciano/NexJob/tree/develop/samples/NexJob.Sample.CloudTriggers) sample wires AWS SQS, Azure Service Bus, Google Pub/Sub and Salesforce triggers, with `/simulate/*` endpoints to produce messages without the real services.
