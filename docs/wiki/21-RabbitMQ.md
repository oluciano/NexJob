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
builder.Services.AddNexJob()
    .UsePostgreSqlStorage(...)
    .AddNexJobRabbitMqTrigger<ProcessOrderJob>(options =>
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
If multiple job types share the same queue, register the trigger without generic arguments. Each message must include the `nexjob.job_type` header (or have `options.JobType` configured):

```csharp
builder.Services.AddNexJob()
    .AddRabbitMqTrigger(options =>
    {
        options.HostName = "localhost";
        options.QueueName = "incoming-events";
        // options.JobType = typeof(DefaultEventJob).AssemblyQualifiedName;
    });
```

*(Legacy `AddNexJobRabbitMqTrigger` remains supported for backward compatibility).*

### Inbound Guarantees
- **Never Silently Drop:** If `IScheduler.EnqueueAsync` fails, the message is not lost and is redelivered or routed according to broker policy.
- **Idempotency:** The broker message ID is used as `JobRecord.IdempotencyKey` to prevent duplicate execution.
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
builder.Services.AddNexJob()
    .UsePostgreSqlStorage(...)
    .AddRabbitMqProducer(options =>
    {
        options.HostName = builder.Configuration["RABBITMQ_HOST"] 
            ?? Environment.GetEnvironmentVariable("RABBITMQ_HOST") 
            ?? "localhost";
        options.Port = 5672;
        options.UserName = builder.Configuration["RABBITMQ_USER"] ?? "guest";
        options.Password = builder.Configuration["RABBITMQ_PASS"] ?? "guest";
        options.DefaultExchange = "orders.events";
        options.ConfirmTimeout = TimeSpan.FromSeconds(5);
    });
```

### Publishing Messages

Inject `IScheduler` into any service, controller, or handler:

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
- If the broker returns a NACK or the operation times out, the producer throws an exception, and NexJob retries the job using exponential backoff.
- This eliminates silent message loss on unroutable or unpersisted messages.

---

## 4. Configuration & 12-Factor App (Docker / Kubernetes)

`NexJob.RabbitMQ` natively supports configuration from `appsettings.json` or environment variables:

| Variable | Description | Default |
|---|---|---|
| `RABBITMQ_HOST` | Hostname or IP address | `localhost` |
| `RABBITMQ_PORT` | Port number | `5672` |
| `RABBITMQ_USER` | Username | `guest` |
| `RABBITMQ_PASSWORD` | Password | `guest` |
| `RABBITMQ_VIRTUAL_HOST` | Virtual host | `/` |

---

## 5. Reliability & Failure Handling

| Failure Scenario | NexJob Producer Behavior |
|---|---|
| **RabbitMQ Broker Down** | The job throws a connection/socket exception and triggers NexJob's retry policy with exponential backoff. Messages remain durable in NexJob storage. |
| **Broker NACK** | The broker rejection throws `InvalidOperationException`, triggering retry. |
| **Confirm Timeout** | Throws `TimeoutException`, triggering retry. |
| **Retries Exhausted** | The message is dispatched to the Dead-Letter pipeline (`IDeadLetterHandler`) and surfaced on the Dashboard. |
| **Host Shutdown** | Active connections and channels are closed cleanly without dropping acknowledged messages. |
