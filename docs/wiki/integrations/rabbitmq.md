---
title: "NexJob RabbitMQ: Triggers and Reliable Outbox Producer"
sidebarTitle: "RabbitMQ"
description: "Use NexJob.RabbitMQ to consume RabbitMQ messages as NexJob job triggers and publish events reliably with the transactional outbox pattern."
---

`NexJob.RabbitMQ` unifies two complementary capabilities into a single package: a **RabbitMQ Trigger** that transforms incoming queue messages into persisted background jobs, and a **Resilient Outbox Producer** that publishes messages to RabbitMQ exchanges through NexJob's storage layer — backed by Publisher Confirms — so messages survive broker outages and are retried automatically. Together they let you build reliable event-driven pipelines without managing broker reconnect loops or silent publish failures yourself.

---

## Installation

```bash
dotnet add package NexJob.RabbitMQ
```

---

## RabbitMQ Trigger (Consumer)

The RabbitMQ Trigger subscribes to a queue and enqueues a NexJob job for each message it receives. The broker message is acknowledged only after the job record is committed to NexJob storage.

### Option A: Strongly-typed consumer (recommended)

Bind a RabbitMQ queue directly to one job handler class. The job type is automatically registered in DI as `Transient`:

```csharp
// Program.cs
builder.Services.AddNexJob()
    .AddRabbitMqTrigger<ProcessOrderJob>(options =>
    {
        options.HostName = builder.Configuration["RABBITMQ_HOST"] ?? "localhost";
        options.Port = 5672;
        options.UserName = "guest";
        options.Password = "guest";
        options.QueueName = "incoming-orders";
        options.TargetQueue = "orders";
        options.PrefetchCount = 10;
    });
```

```csharp
// Job handler — receives the raw message body as a string
public sealed class ProcessOrderJob : IJob<string>
{
    public async Task ExecuteAsync(string messagePayload, CancellationToken ct)
    {
        // messagePayload contains the raw string/JSON from the RabbitMQ queue
        var order = JsonSerializer.Deserialize<OrderDto>(messagePayload);
        // Process order...
    }
}
```

### Option B: Dynamic routing via `nexjob.job_type` header

When multiple job types share the same queue, omit the generic argument. Each message must carry a `nexjob.job_type` header with the assembly-qualified job name. Set `options.JobType` as a fallback for messages without the header:

```csharp
builder.Services.AddNexJob()
    .AddRabbitMqTrigger(options =>
    {
        options.HostName = "localhost";
        options.QueueName = "incoming-events";
        // Fallback for messages without the nexjob.job_type header:
        // options.JobType = typeof(DefaultEventJob).AssemblyQualifiedName;
    });
```

### Inbound guarantees

- **Never silently drop**: Transient failures (storage down, network error) nack with `requeue: true` after a one-second pause, so the broker redelivers the message without a hot loop. Permanent failures (unresolvable job type, malformed payload) nack with `requeue: false`, routing to the queue's dead-letter exchange if one is configured.
- **Idempotency**: The `MessageId` AMQP property is used as the `JobRecord.IdempotencyKey`. Redelivery of the same message never creates a second job. Messages published without a `MessageId` are not deduplicated — every delivery creates a job (at-least-once).
- **Trace propagation**: W3C `traceparent` is extracted from `IBasicProperties.Headers` and attached to the job record.
- **Ack only after success**: `BasicAck` is sent strictly after the job record is committed to NexJob storage.

!!! tip
    Always set a unique `MessageId` on messages you publish to RabbitMQ queues that feed NexJob triggers. Without it, a redelivered message creates a duplicate job because there is no key to deduplicate on.


---

## Resilient Outbox Producer

The outbox producer persists messages to NexJob storage before publishing them to RabbitMQ. If the broker is temporarily unreachable, messages remain safe in storage and are retried with exponential backoff. Publisher Confirms mode is used on every channel — the producer waits for a broker ACK before marking the job succeeded.

### Architecture

```
[Application Service]
       │
       ▼ scheduler.EnqueueRabbitMqAsync(...)
[NexJob Storage]
       │
       ▼ Worker dequeues RabbitMqProducerJob
[RabbitMqProducerJob]
       │
       ▼ BasicPublish + ConfirmSelect + W3C traceparent
[RabbitMQ Broker]
```

### Registration

```csharp
builder.Services.AddNexJob()
    .AddRabbitMqProducer(options =>
    {
        options.HostName = builder.Configuration["RABBITMQ_HOST"]
            ?? Environment.GetEnvironmentVariable("RABBITMQ_HOST")
            ?? "localhost";
        options.Port = 5672;
        options.UserName = builder.Configuration["RABBITMQ_USER"] ?? "guest";
        options.Password = builder.Configuration["RABBITMQ_PASSWORD"] ?? "guest";
        options.DefaultExchange = "orders.events";
        options.ConfirmTimeout = TimeSpan.FromSeconds(5);
    });
```

### Publishing messages

Inject `IScheduler` and call `EnqueueRabbitMqAsync`. Set `messageId:` on every publish so the consuming NexJob trigger can deduplicate redeliveries:

```csharp
public class OrderService(IScheduler scheduler)
{
    // Strongly typed object — serialized as JSON
    public async Task CreateOrderAsync(OrderCreatedEvent order, CancellationToken ct)
    {
        await scheduler.EnqueueRabbitMqAsync(
            exchange: "orders.events",
            routingKey: "order.created",
            value: order,
            correlationId: order.OrderId.ToString(),
            messageId: Guid.NewGuid().ToString(),
            cancellationToken: ct);
    }

    // Publish to the default exchange with a routing key
    public async Task SendNotificationAsync(NotificationEvent notification, CancellationToken ct)
    {
        await scheduler.EnqueueRabbitMqAsync(
            routingKey: "notifications",
            value: notification,
            cancellationToken: ct);
    }

    // Raw JSON string — sent verbatim
    public async Task PublishRawJsonAsync(string routingKey, string jsonPayload, CancellationToken ct)
    {
        await scheduler.EnqueueRabbitMqRawAsync(
            exchange: "orders.events",
            routingKey: routingKey,
            value: jsonPayload,
            cancellationToken: ct);
    }

    // Binary payload
    public async Task PublishBinaryAsync(string exchange, string routingKey, byte[] data, CancellationToken ct)
    {
        await scheduler.EnqueueRabbitMqRawAsync(
            exchange: exchange,
            routingKey: routingKey,
            value: data,
            cancellationToken: ct);
    }
}
```

---

## Publisher Confirms

Every RabbitMQ channel opened by `NexJob.RabbitMQ` operates in Publisher Confirms mode (`ConfirmSelect()`). After each publish the producer awaits an ACK from the broker. If the broker returns a NACK or the operation times out, the producer throws and NexJob's retry policy reschedules the job. This eliminates silent message loss on unroutable or unpersisted messages.

---

## Configuration from Environment Variables

`NexJob.RabbitMQ` does not read fixed environment variable names automatically. Read the values from `IConfiguration` (which surfaces environment variables through the default host builders) and assign them:

```csharp
builder.Services.AddNexJob()
    .AddRabbitMqTrigger(options =>
    {
        options.HostName = builder.Configuration["RABBITMQ_HOST"] ?? "localhost";
        options.Port = int.TryParse(builder.Configuration["RABBITMQ_PORT"], out var port) ? port : 5672;
        options.UserName = builder.Configuration["RABBITMQ_USER"] ?? "guest";
        options.Password = builder.Configuration["RABBITMQ_PASSWORD"] ?? "guest";
        options.VirtualHost = builder.Configuration["RABBITMQ_VIRTUAL_HOST"] ?? "/";
        options.QueueName = "incoming-events";
    });
```

Or bind an entire configuration section:

```csharp
builder.Services.AddNexJob()
    .AddRabbitMqTrigger(options =>
        builder.Configuration.GetSection("RabbitMQ").Bind(options));
```

---

## Forwarding exhausted jobs

Set `ExhaustedJobsRoutingKey` (and, if you do not use the default exchange, `ExhaustedJobsExchange`) on the trigger to copy a job to another queue once it exhausts its retries. The copy goes through the Outbox producer (`AddRabbitMqProducer` is required) and the job stays `Failed` in NexJob. Only the message body is forwarded. See [Forwarding a dead-lettered job](../concepts/retries-and-dead-letter.md#forwarding-a-dead-lettered-job) for the full behaviour and its limits.

## Reliability and Failure Handling

| Failure Scenario | Behavior |
|---|---|
| **RabbitMQ broker down** | Connection/socket exception is thrown; NexJob's retry policy reschedules the job. Messages remain durable in storage. |
| **Broker NACK** | `InvalidOperationException` is thrown, triggering retry. |
| **Confirm timeout** | `TimeoutException` is thrown, triggering retry. |
| **Retries exhausted** | The message is dispatched to the dead-letter pipeline (`IDeadLetterHandler`) and surfaced in the dashboard. |
| **Host shutdown** | Active connections and channels are closed cleanly without dropping acknowledged messages. |
