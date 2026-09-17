# NexJob.Kafka

End-to-end Apache Kafka integration for NexJob, unifying **Kafka Triggers (Consumers)** and a **Resilient Outbox Producer** in a single package.

---

## Installation

```bash
dotnet add package NexJob.Kafka
```

---

## 1. Resilient Outbox Producer

Publishes messages to Kafka topics backed by NexJob's persistent storage, exponential retry with jitter, dead-letter dispatch, and OpenTelemetry trace propagation.

### Registration

```csharp
using NexJob;
using NexJob.Kafka;

builder.Services.AddNexJob()
    .UsePostgreSqlStorage(...)
    .AddKafkaProducer(options =>
    {
        options.BootstrapServers = builder.Configuration["KAFKA_BOOTSTRAP_SERVERS"] 
            ?? Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS") 
            ?? "localhost:9092";
        options.Acks = Confluent.Kafka.Acks.All;
        options.EnableIdempotence = true;
        options.FlushTimeout = TimeSpan.FromSeconds(10);
    });
```

### Publishing Messages

Inject `IScheduler` into your services or controllers:

```csharp
public class OrderService(IScheduler scheduler)
{
    // 1. Strongly typed object (automatically serialized to JSON)
    public async Task CreateOrderAsync(OrderCreatedEvent order, CancellationToken ct)
    {
        await scheduler.EnqueueKafkaAsync(
            topic: "orders-topic",
            key: order.OrderId.ToString(),
            value: order,
            cancellationToken: ct);
    }

    // 2. Raw string / JSON payload
    public async Task PublishRawJsonAsync(string orderId, string rawJson, CancellationToken ct)
    {
        await scheduler.EnqueueKafkaRawAsync(
            topic: "orders-topic",
            key: orderId,
            value: rawJson,
            cancellationToken: ct);
    }

    // 3. Raw binary payload
    public async Task PublishBinaryAsync(string key, byte[] bytes, CancellationToken ct)
    {
        await scheduler.EnqueueKafkaRawAsync(
            topic: "events-topic",
            key: key,
            value: bytes,
            cancellationToken: ct);
    }
}
```

---

## 2. Kafka Trigger (Consumer)

Consumes incoming messages from Kafka topics and automatically enqueues them as background jobs.

### Registration

```csharp
builder.Services.AddNexJob()
    .AddKafkaTrigger(options =>
    {
        options.BootstrapServers = builder.Configuration["KAFKA_BOOTSTRAP_SERVERS"] ?? "localhost:9092";
        options.Topic = "incoming-orders";
        options.GroupId = "nexjob-consumer-group";
        options.TargetQueue = "orders";
    });
```

### Inbound Message Contract

Messages consumed by the trigger expect the following headers:
- `nexjob.job_type`: Assembly-qualified name of the `IJob<string>` to execute (required).
- `traceparent`: W3C distributed trace header (optional).

The message value is passed as the string input to the resolved job.

---

## 3. Configuration & 12-Factor App (Docker / Kubernetes)

`NexJob.Kafka` does not require settings in `appsettings.json`. It fully supports containerized environments via environment variables:

```bash
# Set environment variables in Docker / Kubernetes
export KAFKA_BOOTSTRAP_SERVERS="kafka-broker.prod:9092"
export KAFKA_TOPIC="orders"
```

Read seamlessly in C#:
```csharp
builder.Services.AddNexJob()
    .AddKafkaProducer(options =>
    {
        options.BootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS")
            ?? throw new InvalidOperationException("KAFKA_BOOTSTRAP_SERVERS is not set.");
    });
```

---

## 4. Producer Configuration Options

| Option | Description | Default |
|---|---|---|
| `BootstrapServers` | Comma-separated list of Kafka broker endpoints (required) | `""` |
| `Acks` | Acknowledgment guarantee (`Acks.All`, `Acks.Leader`, `Acks.None`) | `Acks.All` |
| `EnableIdempotence` | Controls producer idempotence on the broker | `true` |
| `FlushTimeout` | Timeout for flushing messages on application shutdown | `10 seconds` |
| `Queue` | NexJob queue name used for publishing jobs | `"kafka-producer"` |
| `DefaultPriority` | Default execution priority for publishing jobs | `JobPriority.Normal` |

---

## 5. Architectural Guarantees

1. **Transactional Outbox / Resilient Publish:** Messages are first durably persisted to NexJob storage. If the Kafka broker is down, messages wait safely in storage and are retried automatically.
2. **Dead-Letter Handling:** Permanent publishing failures trigger NexJob's dead-letter pipeline (`IDeadLetterHandler`) and surface in the dashboard.
3. **Trace Propagation:** Injects W3C `traceparent` headers into outgoing messages and extracts them on consumer triggers.
4. **Graceful Shutdown:** Unflushed in-flight messages are flushed before the application process exits.
