# NexJob.RabbitMQ

End-to-end RabbitMQ integration for NexJob, unifying **RabbitMQ Triggers (Consumers)** and a **Resilient Outbox Producer** with Publisher Confirms in a single package.

---

## Installation

```bash
dotnet add package NexJob.RabbitMQ
```

---

## 1. Resilient Outbox Producer

Publishes messages to RabbitMQ exchanges and queues backed by NexJob's persistent storage, exponential retries with jitter, dead-letter dispatch, Publisher Confirms, and OpenTelemetry trace propagation.

### Registration

```csharp
using NexJob;
using NexJob.RabbitMQ;

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
        options.DefaultExchange = "events.exchange";
        options.ConfirmTimeout = TimeSpan.FromSeconds(5);
    });
```

### Publishing Messages

Inject `IScheduler` into your application services or controllers:

```csharp
public class OrderService(IScheduler scheduler)
{
    // 1. Strongly typed object (automatically serialized to JSON)
    public async Task CreateOrderAsync(OrderCreatedEvent order, CancellationToken ct)
    {
        await scheduler.EnqueueRabbitMqAsync(
            exchange: "orders.exchange",
            routingKey: "order.created",
            value: order,
            correlationId: order.OrderId.ToString(),
            cancellationToken: ct);
    }

    // 2. Publish to default exchange with routing key
    public async Task SendNotificationAsync(NotificationEvent notification, CancellationToken ct)
    {
        await scheduler.EnqueueRabbitMqAsync(
            routingKey: "notifications-queue",
            value: notification,
            cancellationToken: ct);
    }

    // 3. Raw string / JSON payload
    public async Task PublishRawJsonAsync(string routingKey, string rawJson, CancellationToken ct)
    {
        await scheduler.EnqueueRabbitMqRawAsync(
            exchange: "events.exchange",
            routingKey: routingKey,
            value: rawJson,
            cancellationToken: ct);
    }

    // 4. Raw binary payload
    public async Task PublishBinaryAsync(string exchange, string routingKey, byte[] bytes, CancellationToken ct)
    {
        await scheduler.EnqueueRabbitMqRawAsync(
            exchange: exchange,
            routingKey: routingKey,
            value: bytes,
            cancellationToken: ct);
    }
}
```

### Publisher Confirms Guarantee

`RabbitMqProducerClient` enables Publisher Confirms (`ConfirmSelect()`) on every channel. A job completes with success only after RabbitMQ acknowledges that the message was received and persisted to the broker. If the broker returns a NACK or the operation times out, the producer job throws an exception, activating NexJob's retry and backoff policy.

---

## 2. RabbitMQ Trigger (Consumer)

Consumes incoming messages from RabbitMQ queues and automatically enqueues them as background jobs.

### Registration

There are two ways to register which job is triggered when a message arrives:

#### Option A: Strongly-Typed Consumer (Recommended)
Bind a queue directly to a job handler class (`IJob<string>`). The job is automatically registered in DI as `Transient`:

```csharp
builder.Services.AddNexJob()
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
```

#### Option B: Dynamic Message Header (`nexjob.job_type`)
If multiple job types share the same queue, omit the generic argument. The incoming RabbitMQ message must include the `nexjob.job_type` header (or have `options.JobType` configured as a default):

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

### Inbound Message Contract

Messages consumed by the trigger expect the following headers:
- `nexjob.job_type`: Assembly-qualified name of the `IJob<string>` to execute (required).
- `traceparent`: W3C distributed trace header (optional).

The message payload body is passed directly as the job input string.

---

## 3. Environment Variables (12-Factor App / Docker / K8s)

Both the Producer and Trigger support configuration via environment variables:

| Variable | Description | Default |
|---|---|---|
| `RABBITMQ_HOST` | Hostname or IP of the RabbitMQ server | `localhost` |
| `RABBITMQ_PORT` | Port number | `5672` |
| `RABBITMQ_USER` | Username for authentication | `guest` |
| `RABBITMQ_PASSWORD` | Password for authentication | `guest` |
| `RABBITMQ_VIRTUAL_HOST` | Target virtual host | `/` |

Or via ASP.NET Core hierarchical configuration:
```json
{
  "NexJob": {
    "RabbitMq": {
      "HostName": "rabbitmq.internal",
      "Port": 5672,
      "UserName": "admin",
      "Password": "secretpassword",
      "VirtualHost": "/"
    }
  }
}
```
