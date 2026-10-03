# NexJob.RabbitMQ

End-to-end RabbitMQ integration for NexJob, unifying **RabbitMQ Triggers (Consumers)** and a **Resilient Outbox Producer** with Publisher Confirms in a single package.

---

## Installation

```bash
dotnet add package NexJob.RabbitMQ
```

---

## 1. Resilient Outbox Producer

Publishes messages to RabbitMQ exchanges and queues backed by NexJob's persistent storage, retries with backoff and jitter, dead-letter dispatch, Publisher Confirms, and OpenTelemetry trace propagation.

### Registration

```csharp
using NexJob;
using NexJob.RabbitMQ;

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

Messages consumed by the trigger can carry the following headers:
- `nexjob.job_type`: Assembly-qualified name of the `IJob<string>` to execute. When it is absent, `options.JobType` is used; a message with neither can never become a job and is nacked without requeue.
- `traceparent`: W3C distributed trace header (optional).

The message payload body is passed directly as the job input string. The job idempotency key is the message's `MessageId` (`CorrelationId` and the body are not used); a message published without a `MessageId` is not deduplicated.

Enqueue failures are classified: a transient failure (storage or network error) is nacked with `requeue: true` after a one-second pause; a permanent failure (missing job type, malformed payload) is nacked with `requeue: false`, which routes it to the queue's dead-letter exchange if configured.

---

## 3. Configuration & 12-Factor App (Docker / Kubernetes)

`NexJob.RabbitMQ` is configured through the option delegates shown above. It does not read a `NexJob:RabbitMq` section or any `RABBITMQ_*` environment variable by itself. Read the values you choose and assign them (environment variables are available through `builder.Configuration` with the default host builders):

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

Or bind a whole section of your own: `.AddRabbitMqTrigger(options => builder.Configuration.GetSection("RabbitMQ").Bind(options))`.

## Forwarding failed jobs

A job created from a consumed message that exhausts its retries stays **only inside NexJob** (`Failed` in the dashboard), because the broker message was acknowledged when it became a job. To copy the original message body to another queue, set `ExhaustedJobsRoutingKey` on the trigger (the Outbox producer, `AddRabbitMqProducer`, is required):

```csharp
.AddRabbitMqTrigger<ProcessOrderJob>(opt =>
{
    opt.QueueName = "orders";
    opt.TargetQueue = "orders";
    opt.ExhaustedJobsRoutingKey = "orders.exhausted";   // default exchange: the queue name; optional: opt.ExhaustedJobsExchange
});
```

Only the body is forwarded, the job stays `Failed` in NexJob, and only jobs of this trigger are forwarded. Details and limits: [Forwarding a dead-lettered job](https://github.com/oluciano/NexJob/blob/main/docs/wiki/concepts/retries-and-dead-letter.md#forwarding-a-dead-lettered-job).
