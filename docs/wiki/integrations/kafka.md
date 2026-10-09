---
title: "Kafka Integration: Triggers and Resilient Outbox for NexJob"
sidebarTitle: "Kafka"
description: "Use NexJob.Kafka to consume Kafka topic messages as background job triggers and publish events reliably via the transactional outbox pattern."
---

`NexJob.Kafka` unifies two complementary capabilities into a single package: a **Kafka Trigger** that transforms incoming topic messages into persisted background jobs, and a **Resilient Outbox Producer** that publishes messages to Kafka through NexJob's storage layer so they survive broker outages and are retried automatically. Together they let you build event-driven pipelines where every message is either processed or reliably recovered — no silent drops, no manual retry loops.

---

## Installation

```bash
dotnet add package NexJob.Kafka
```

---

## Kafka Trigger (Consumer)

The Kafka Trigger subscribes to a topic and enqueues a NexJob job for each message it receives. Job execution happens asynchronously on a NexJob worker; the trigger only persists the record and commits the offset after the enqueue succeeds.

### Option A: Strongly-typed consumer (recommended)

Bind a topic directly to one job handler class. The job type is automatically registered in DI as `Transient`:

```csharp
// Program.cs
builder.Services.AddNexJob()
    .AddKafkaTrigger<ProcessOrderJob>(options =>
    {
        options.BootstrapServers = builder.Configuration["KAFKA_BOOTSTRAP_SERVERS"] ?? "localhost:9092";
        options.Topic = "incoming-orders";
        options.GroupId = "nexjob-order-consumer";
        options.TargetQueue = "orders";
    });
```

```csharp
// Job handler — receives the raw message body as a string
public sealed class ProcessOrderJob : IJob<string>
{
    public async Task ExecuteAsync(string messagePayload, CancellationToken ct)
    {
        var order = JsonSerializer.Deserialize<OrderDto>(messagePayload);
        // Process order...
    }
}
```

### Option B: Dynamic routing via `nexjob.job_type` header

When multiple job types share the same topic, omit the generic argument. Each message must carry a `nexjob.job_type` header with the assembly-qualified job name. Set `options.JobType` as a fallback for messages that don't carry the header:

```csharp
builder.Services.AddNexJob()
    .AddKafkaTrigger(options =>
    {
        options.BootstrapServers = "localhost:9092";
        options.Topic = "incoming-events";
        options.GroupId = "events-consumer";
        // Fallback for messages without the nexjob.job_type header:
        // options.JobType = typeof(DefaultEventJob).AssemblyQualifiedName;
    });
```

### Inbound guarantees

- **Never silently drop**: Transient failures (storage down, network error) retry the same record in place with a 1 s → 2 s → 5 s → 10 s → 20 s → 30 s backoff. The next record is not consumed until the current one succeeds, preserving partition order. Permanent failures (unresolvable job type, malformed payload) are produced to a `DeadLetterTopic` if configured; without one they are logged at `Error` and committed so the partition is not blocked.
- **Idempotency**: The idempotency key is `kafka:{topic}:{partition}:{offset}`. Redelivery of the same record never creates a second job.
- **Trace propagation**: W3C `traceparent` headers are extracted and attached to the job record.
- **Manual commit only**: Offsets are committed to Kafka strictly after the job has been persisted to NexJob storage.

---

## Resilient Outbox Producer

The outbox producer persists messages to NexJob storage before publishing them to Kafka. If the broker is temporarily unreachable, the message stays safe in storage and is retried with exponential backoff on the next worker cycle.

### Architecture

```text
[Application Service]
       │
       ▼ scheduler.EnqueueKafkaAsync(...)
[NexJob Storage]
       │
       ▼ Worker dequeues KafkaProducerJob
[KafkaProducerJob]
       │
       ▼ ProduceAsync + W3C traceparent
[Apache Kafka Broker]
```

### Registration

```csharp
builder.Services.AddNexJob()
    .AddKafkaProducer(options =>
    {
        options.BootstrapServers = builder.Configuration["KAFKA_BOOTSTRAP_SERVERS"]
            ?? "localhost:9092";
        options.Acks = Confluent.Kafka.Acks.All;
        options.EnableIdempotence = true;
        options.FlushTimeout = TimeSpan.FromSeconds(10);
    });
```

### Publishing messages

Inject `IScheduler` and call `EnqueueKafkaAsync`. Pass a `string` or `byte[]` to send the value as-is; pass any other object to have it serialized as JSON:

```csharp
public class OrderService(IScheduler scheduler)
{
    // Strongly typed object — serialized as JSON
    public async Task CreateOrderAsync(OrderCreatedEvent order, CancellationToken ct)
    {
        await scheduler.EnqueueKafkaAsync(
            topic: "orders-topic",
            key: order.OrderId.ToString(),
            value: order,
            cancellationToken: ct);
    }

    // Raw JSON string — sent verbatim
    public async Task PublishRawJsonAsync(string orderId, string jsonPayload, CancellationToken ct)
    {
        await scheduler.EnqueueKafkaAsync(
            topic: "orders-topic",
            key: orderId,
            value: jsonPayload,
            cancellationToken: ct);
    }

    // Binary payload
    public async Task PublishBinaryAsync(string key, byte[] data, CancellationToken ct)
    {
        await scheduler.EnqueueKafkaAsync(
            topic: "events-topic",
            key: key,
            value: data,
            cancellationToken: ct);
    }
}
```

---

## Security: SASL and SSL Configuration

Use the `ConfigureConsumer` and `ConfigureProducer` delegates to set SASL/SCRAM credentials, SSL/TLS certificates, and custom broker timeouts without environment variables:

```csharp
// Consumer — SASL/SCRAM-SHA-512 with SSL
builder.Services.AddNexJob()
    .AddKafkaTrigger(options =>
    {
        options.BootstrapServers = "kafka.prod:9092";
        options.Topic = "incoming-orders";
        options.GroupId = "order-consumers";

        options.ConfigureConsumer = config =>
        {
            config.SecurityProtocol = SecurityProtocol.SaslSsl;
            config.SaslMechanism = SaslMechanism.ScramSha512;
            config.SaslUsername = "kafka-user";
            config.SaslPassword = "vault-secret-password";
            config.SslCaPem = "-----BEGIN CERTIFICATE-----\n...\n-----END CERTIFICATE-----";
            config.SessionTimeoutMs = 45000;
        };
    });

// Producer — SASL/PLAIN with linger
builder.Services.AddNexJob()
    .AddKafkaProducer(options =>
    {
        options.BootstrapServers = "kafka.prod:9092";

        options.ConfigureProducer = config =>
        {
            config.SecurityProtocol = SecurityProtocol.SaslSsl;
            config.SaslMechanism = SaslMechanism.Plain;
            config.SaslUsername = "producer-user";
            config.SaslPassword = "producer-password";
            config.LingerMs = 20;
        };
    });
```

---

## Configuration from `appsettings.json`

`NexJob.Kafka` does not read a fixed `NexJob:Kafka` configuration section. Bind from whichever section you choose:

```json
{
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "Topic": "orders",
    "GroupId": "orders-consumer"
  }
}
```

```csharp
builder.Services.AddNexJob()
    .AddKafkaTrigger(options => builder.Configuration.GetSection("Kafka").Bind(options));
```

---

## Partition Ordering and Worker Concurrency

Kafka guarantees message ordering within a partition using the message `Key`. When strict FIFO ordering per partition key is required, publish with a dedicated queue and run that queue on a single worker:

```csharp
// Publish to a dedicated single-worker queue
await scheduler.EnqueueKafkaAsync(
    topic: "orders-topic",
    key: order.TenantId.ToString(),
    value: order,
    queue: "kafka-ordered",
    cancellationToken: ct);
```

```csharp
// Configure the node that processes this queue with one worker
builder.Services.AddNexJob(options =>
{
    options.Workers = 1;
    options.Queues = ["kafka-ordered"];
});
```

For high-throughput scenarios where eventual delivery is acceptable, use the default worker concurrency to publish multiple messages in parallel.

---

## Forwarding exhausted jobs

Set `ExhaustedJobsTopic` on the trigger to copy a job to another topic once it exhausts its retries. The copy goes through the Outbox producer (`AddKafkaProducer` is required) and the job stays `Failed` in NexJob. Only the message body is forwarded. See [Forwarding a dead-lettered job](../concepts/retries-and-dead-letter.md#forwarding-a-dead-lettered-job) for the full behaviour and its limits.

---

## Reliability and Failure Handling

| Failure Scenario | Behavior |
|---|---|
| **Kafka broker down** | `ProduceException` is thrown; NexJob's retry policy kicks in. The message remains safe in storage. |
| **Retries exhausted** | The message is dispatched to the dead-letter pipeline (`IDeadLetterHandler`) and surfaced in the dashboard. |
| **Host shutdown** | The registered `IKafkaProducerClient` automatically calls `Flush()` to drain all in-flight messages before the process exits. |
| **Serialization error** | Fails fast and routes to dead-letter without crashing the worker host. |

!!! note
    `NexJob.Kafka` is tested against Kafka clusters running in **KRaft mode** (no ZooKeeper). It is also compatible with ZooKeeper-based clusters, but KRaft is recommended for new deployments.

## Try it

The [`NexJob.Sample.Kafka`](https://github.com/oluciano/NexJob/tree/develop/samples/NexJob.Sample.Kafka) sample publishes events through the partitioned outbox and consumes them with the trigger, tracking offsets. It uses the Kafka (KRaft) container from the Docker Compose stack in `samples/`.
