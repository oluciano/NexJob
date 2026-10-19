---
title: "NexJob Message Brokers: Triggers and Outbox"
sidebarTitle: "Overview"
description: "Use NexJob triggers to automatically enqueue jobs from Azure Service Bus, AWS SQS, Google Pub/Sub, Salesforce, RabbitMQ, and Kafka."
---

NexJob triggers bridge external message brokers and your background job pipeline. Each trigger package subscribes to a broker source — a queue, topic, or channel — and converts every incoming message into a persisted NexJob job, delivering it to the dispatcher for execution. The pipeline is: **broker → trigger → `JobRecordFactory` → `IScheduler.EnqueueAsync` → dispatcher**. Your job code receives the raw message body as a string; it decides how to parse it.


## Brokers at a glance

| Broker | Publishes (outbox) | Idempotency key | The job type comes from | When the enqueue fails |
|---|---|---|---|---|
| [**Kafka**](kafka.md) | Yes | `kafka:{topic}:{partition}:{offset}` | `JobType`, else header `nexjob.job_type` | Transient: retried in place with a growing pause (1 s up to 30 s) without consuming the next record. Permanent: copied to the dead-letter topic if one is configured, otherwise skipped with an error log. |
| [**RabbitMQ**](rabbitmq.md) | Yes | `MessageId` (none when blank) | `JobType`, else header `nexjob.job_type` | Transient: `nack` with requeue after 1 s. Permanent: `nack` without requeue. |
| [**Azure Service Bus**](azure-service-bus.md) | No | `MessageId` | `JobType`, else application property `nexjob.job_type` | Transient: the message is abandoned and redelivered. Permanent: dead-lettered. |
| [**AWS SQS**](aws-sqs.md) | No | `MessageId` | only the `JobName` option | Any failure: the visibility timeout is reset to 0, so the message returns at once. A redrive policy on the queue is what parks it. |
| [**Google Pub/Sub**](google-pubsub.md) | No | message ID | `JobType`, else attribute `nexjob.job_type` | Transient: `Nack`. Permanent: `Nack` after 2 s, so a dead-letter policy on the subscription can park it. |
| [**Salesforce Pub/Sub API**](salesforce.md) | No | event id (replay id as hex) | the `JobType` option, else the built-in `SalesforceEventJob` | The event is logged and, when `DeadLetterQueue` is set, copied there as a job. Its replay id is not committed. An event that can never be decoded is logged, kept in `DeadLetterQueue` as Base64 when one is set, and skipped with its replay id committed. |
| [**Salesforce Streaming API**](salesforce-streaming.md) | No | `{channel}:{eventId}` | the `JobType` option, else the built-in `SalesforceStreamingEventJob` | The same as the Pub/Sub API trigger. |

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

Each broker can be registered in two equivalent ways: on the service collection (`services.AddNexJob{Broker}Trigger<TJob>(...)`, and `AddSalesforceTrigger` or `AddSalesforceStreamingTrigger` for Salesforce) or chained on the builder returned by `AddNexJob()` (`AddNexJob().Add{Broker}Trigger<TJob>(...)`).

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

1. **Subscriber's configured job type** — `options.JobType`, or the generic overload `Add{Broker}Trigger<TJob>()` which sets it for you. The AWS SQS option is called `JobName`, and SQS reads **no** message attribute: its job type is always the configured one. The two Salesforce triggers fall back to a built-in job when no type is configured. A configured value always wins: the message header/attribute below is never even read when it is set, so a broker publisher cannot override the operator's choice of job.
2. **`nexjob.job_type` header/attribute** on the message, used only when no job type was configured — set by the publisher as the assembly-qualified name (e.g. `MyApp.Jobs.ProcessOrderJob, MyApp`).
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
| Salesforce Pub/Sub API | Event ID (the Replay ID as hex when the event has none) |
| Salesforce Streaming API | `{channel}:{eventId}` (or `{channel}:{replayId}` when absent) |

Every trigger enqueues with `DuplicatePolicy.AllowAfterFailed` (see [Idempotency](../concepts/idempotency.md#duplicatepolicy)), so the key deduplicates only against **active** jobs. A message redelivered after its job has finished, for example a Kafka offset replayed after a consumer group reset, or an SQS message redelivered after a slow acknowledgment, creates a new job. Checked with the same key and the same policy: while the first job was active the second call returned the same job, and after it finished it created another, so the job ran twice. Treat triggers as at-least-once and make the jobs idempotent.

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
