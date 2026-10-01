# RabbitMQ Integration

`NexJob.RabbitMQ` provides end-to-end, resilient RabbitMQ integration for NexJob, unifying **RabbitMQ Triggers (Consumers)** and a **Resilient Outbox Producer** with Publisher Confirms into a single cohesive package.

---

## Installation

```bash
dotnet add package NexJob.RabbitMQ
```

---

## Capabilities Overview

1. **RabbitMQ Trigger (Consumer):** Ingests messages from RabbitMQ queues and automatically enqueues them as background jobs with guaranteed delivery, deduplication, and OpenTelemetry trace extraction.
2. **Resilient Outbox Producer:** Durably publishes messages from your application to RabbitMQ exchanges and queues, backed by NexJob's persistence, exponential retries with jitter, dead-letter dispatch, Publisher Confirms, and OpenTelemetry trace injection.

---

## 1. RabbitMQ Trigger (Consumer)

The RabbitMQ Trigger listens to a configured queue and transforms incoming messages into NexJob background jobs.

### Registration

There are two ways to register which job is triggered when a message arrives:

#### Option A: Strongly-Typed Consumer (Recommended)
Bind a queue directly to a job handler class (`IJob<string>`). The job is automatically registered in DI as `Transient`:

```csharp
// Program.cs
builder.Services.AddNexJobPostgres(builder.Configuration.GetConnectionString("NexJobConnection")!); // or any other storage provider
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

// Job Handler
public sealed class ProcessOrderJob : IJob<string>
{
    public async Task ExecuteAsync(string messagePayload, CancellationToken ct)
    {
        // messagePayload contains raw string/JSON from the RabbitMQ queue
        var order = JsonSerializer.Deserialize<OrderDto>(messagePayload);
        // Process order...
    }
}
```

#### Option B: Dynamic Message Header (`nexjob.job_type`)
If multiple job types share the same queue, register the trigger without generic arguments. Each message carries a `nexjob.job_type` header with the assembly-qualified name of the target job. When a message has no such header, `options.JobType` (the fallback) is used; a message with neither can never become a job and is nacked without requeue (see *Inbound Guarantees* below):

```csharp
builder.Services.AddNexJob()
    .AddRabbitMqTrigger(options =>
    {
        options.HostName = "localhost";
        options.QueueName = "incoming-events";
        // Fallback for messages without the header:
        // options.JobType = typeof(DefaultEventJob).AssemblyQualifiedName;
    });
```

*(Legacy `AddNexJobRabbitMqTrigger` remains supported for backward compatibility).*

### Inbound Guarantees
- **Never Silently Drop:** Enqueue failures are classified. A **transient** failure (storage or network error, timeout) is nacked with `requeue: true` after a one-second pause, so the broker redelivers it without a hot loop and nothing is lost. A **permanent** failure (missing `nexjob.job_type`, malformed payload) is nacked with `requeue: false`, which routes it to the queue's dead-letter exchange if one is configured.
- **Idempotency:** The `MessageId` property is used as `JobRecord.IdempotencyKey`, so redelivery of the same message never creates a second job. `CorrelationId` is not used (many messages can share one). Messages published without a `MessageId` are not deduplicated: every delivery creates a job (at-least-once), so set a unique `MessageId` when you need deduplication.
- **Trace Propagation:** W3C `traceparent` headers are extracted from `IBasicProperties.Headers` and attached to the job trace.
- **Ack Only After Success:** Messages are acknowledged (`BasicAck`) strictly *after* the job record is committed to NexJob storage.

---

## 2. Resilient Outbox Producer

The Producer allows applications to publish messages to RabbitMQ backed by NexJob storage. If RabbitMQ is temporarily unreachable, down for maintenance, or experiencing cluster failover, messages remain safe in NexJob storage and are retried automatically.

### Architecture

```
[Application Service]
       │
       ▼ (scheduler.EnqueueRabbitMqAsync)
[NexJob Storage]
       │
       ▼ (Worker Dequeue)
[RabbitMqProducerJob]
       │
       ▼ (BasicPublish + ConfirmSelect + traceparent)
[RabbitMQ Broker]
```

### Registration

```csharp
// Program.cs
builder.Services.AddNexJobPostgres(builder.Configuration.GetConnectionString("NexJobConnection")!); // or any other storage provider
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

### Publishing Messages

Inject `IScheduler` into any service, controller, or handler. Set `messageId:` when you publish: the consuming NexJob trigger uses it to deduplicate redeliveries (see *Inbound Guarantees*).

```csharp
public class OrderService(IScheduler scheduler)
{
    // Strongly typed object (serialized as JSON)
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

    // Publish to default exchange with routing key
    public async Task SendNotificationAsync(NotificationEvent notification, CancellationToken ct)
    {
        await scheduler.EnqueueRabbitMqAsync(
            routingKey: "notifications",
            value: notification,
            cancellationToken: ct);
    }

    // Raw string / JSON payload
    public async Task PublishRawJsonAsync(string routingKey, string jsonPayload, CancellationToken ct)
    {
        await scheduler.EnqueueRabbitMqRawAsync(
            exchange: "orders.events",
            routingKey: routingKey,
            value: jsonPayload,
            cancellationToken: ct);
    }

    // Raw binary byte array
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

## 3. Publisher Confirms

RabbitMQ channels in `NexJob.RabbitMQ` operate in **Publisher Confirms** mode (`ConfirmSelect()`).
- When a message is published, the producer awaits an acknowledgment (ACK) from the RabbitMQ broker.
- If the broker returns a NACK or the operation times out, the producer throws an exception, and NexJob retries the job according to its retry policy.
- This eliminates silent message loss on unroutable or unpersisted messages.

---

## 4. Configuration & 12-Factor App (Docker / Kubernetes)

`NexJob.RabbitMQ` is configured through the `Action<...Options>` delegates shown above. It does not read a `NexJob:RabbitMQ` section or any `RABBITMQ_*` environment variable by itself; read the values from the configuration source you prefer and assign them (environment variables are available through `builder.Configuration` with the default host builders):

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

Or bind a whole section: `.AddRabbitMqTrigger(options => builder.Configuration.GetSection("RabbitMQ").Bind(options))`.

---

## 5. Reliability & Failure Handling

| Failure Scenario | NexJob Producer Behavior |
|---|---|
| **RabbitMQ Broker Down** | The job throws a connection/socket exception and triggers NexJob's retry policy. Messages remain durable in NexJob storage. |
| **Broker NACK** | The broker rejection throws `InvalidOperationException`, triggering retry. |
| **Confirm Timeout** | Throws `TimeoutException`, triggering retry. |
| **Retries Exhausted** | The message is dispatched to the Dead-Letter pipeline (`IDeadLetterHandler`) and surfaced on the Dashboard. |
| **Host Shutdown** | Active connections and channels are closed cleanly without dropping acknowledged messages. |

> A job that came from the trigger and exhausted its retries stays **only inside NexJob** (the broker message was acknowledged when it became a job). To also send a copy to a RabbitMQ exchange, see [Forwarding a dead-lettered job](06-Retry-And-Dead-Letter.md#forwarding-a-dead-lettered-job-to-kafka-or-rabbitmq).
