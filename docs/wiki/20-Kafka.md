# Kafka Integration

`NexJob.Kafka` provides end-to-end, resilient Apache Kafka integration for NexJob, unifying **Kafka Triggers (Consumers)** and a **Resilient Outbox Producer** into a single cohesive package.

---

## Installation

```bash
dotnet add package NexJob.Kafka
```

---

## Capabilities Overview

1. **Kafka Trigger (Consumer):** Ingests messages from Kafka topics and automatically enqueues them as background jobs with guaranteed delivery, deduplication, and OpenTelemetry trace extraction.
2. **Resilient Outbox Producer:** Durably publishes messages from your domain logic to Kafka topics, leveraging NexJob's persistence, retries with backoff and jitter, dead-letter dispatch, and OpenTelemetry trace injection.

---

## 1. Kafka Trigger (Consumer)

The Kafka Trigger listens to a configured topic and transforms incoming messages into NexJob jobs.

### Registration

There are two ways to register which job is triggered when a message arrives:

#### Option A: Strongly-Typed Consumer (Recommended)
Bind a specific topic directly to a job handler class (`IJob<string>`). The job is automatically registered in DI as `Transient`:

```csharp
// Program.cs
builder.Services.AddNexJobPostgres(builder.Configuration.GetConnectionString("NexJobConnection")!); // or any other storage provider
builder.Services.AddNexJob()
    .AddKafkaTrigger<ProcessOrderJob>(options =>
    {
        options.BootstrapServers = builder.Configuration["KAFKA_BOOTSTRAP_SERVERS"] ?? "localhost:9092";
        options.Topic = "incoming-orders";
        options.GroupId = "nexjob-order-consumer";
        options.TargetQueue = "orders";
    });

// Job Handler
public sealed class ProcessOrderJob : IJob<string>
{
    public async Task ExecuteAsync(string messagePayload, CancellationToken ct)
    {
        // messagePayload contains the raw message body (e.g. JSON)
        var order = JsonSerializer.Deserialize<OrderDto>(messagePayload);
        // Process order...
    }
}
```

#### Option B: Dynamic Message Header (`nexjob.job_type`)
If multiple job types share the same topic, register the trigger without generic arguments. Each incoming Kafka message carries a `nexjob.job_type` header with the assembly-qualified name of the target job. When a message has no such header, `options.JobType` (the fallback) is used; a message with neither can never become a job and is handled as a permanent failure (see *Inbound Guarantees* below):

```csharp
builder.Services.AddNexJob()
    .AddKafkaTrigger(options =>
    {
        options.BootstrapServers = "localhost:9092";
        options.Topic = "incoming-events";
        options.GroupId = "events-consumer";
        // Fallback for messages without the header:
        // options.JobType = typeof(DefaultEventJob).AssemblyQualifiedName;
    });
```

### Inbound Guarantees
- **Never Silently Drop:** Enqueue failures are classified. A **transient** failure (storage or network error, timeout) retries the *same* record in place with a backoff of 1 s, 2 s, 5 s, 10 s, 20 s and then 30 s, until it succeeds or the host stops; the next record is not consumed meanwhile, so order is kept and no later offset can be committed past it. A **permanent** failure (missing `nexjob.job_type`, malformed payload) can never succeed: with `DeadLetterTopic` configured the record is produced there and committed; without it the record is logged at `Error` and committed, because skipping a poison message is better than blocking the partition forever. Configure a dead-letter topic if you cannot afford to lose such messages.
- **Idempotency:** The idempotency key is the record position (`kafka:{topic}:{partition}:{offset}`), so redelivery of the same record never creates a second job. Two different records with the same message key produce two jobs.
- **Trace Propagation:** W3C `traceparent` headers are extracted and attached to the job trace.
- **Manual Commit Only:** Offsets are committed to Kafka strictly *after* the job has been persisted to NexJob storage.

---

## 2. Resilient Outbox Producer

The Producer allows applications to publish messages to Kafka backed by NexJob storage. If the Kafka cluster is temporarily unreachable or slow, messages are persisted and retried automatically.

### Architecture

```
[Application Service]
       │
       ▼ (scheduler.EnqueueKafkaAsync)
[NexJob Storage]
       │
       ▼ (Worker Dequeue)
[KafkaProducerJob]
       │
       ▼ (ProduceAsync + traceparent)
[Apache Kafka Broker]
```

### Registration

```csharp
// Program.cs
builder.Services.AddNexJobPostgres(builder.Configuration.GetConnectionString("NexJobConnection")!); // or any other storage provider
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

### Publishing Messages

Inject `IScheduler` into any service, controller, or handler. Messages go to the `"kafka-producer"` queue unless you pass `queue:`. `EnqueueKafkaAsync` sends a `string` or `byte[]` value as it is and serializes any other object as JSON (`EnqueueKafkaRawAsync` is an alias for the `string`/`byte[]` cases):

```csharp
public class OrderService(IScheduler scheduler)
{
    // Strongly typed object (serialized as JSON)
    public async Task CreateOrderAsync(OrderCreatedEvent order, CancellationToken ct)
    {
        await scheduler.EnqueueKafkaAsync(
            topic: "orders-topic",
            key: order.OrderId.ToString(),
            value: order,
            cancellationToken: ct);
    }

    // Raw string / JSON payload
    public async Task PublishRawJsonAsync(string orderId, string jsonPayload, CancellationToken ct)
    {
        await scheduler.EnqueueKafkaAsync(
            topic: "orders-topic",
            key: orderId,
            value: jsonPayload,
            cancellationToken: ct);
    }

    // Raw binary byte array
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

## 3. Configuration & 12-Factor App (Docker / Kubernetes)

`NexJob.Kafka` is designed for cloud-native environments. Configuration can come from any .NET configuration source or directly from environment variables.

### Option A: Via `IConfiguration` (Standard in ASP.NET Core / Worker Services)
In Docker / Kubernetes, set environment variables:
```bash
export KAFKA_BOOTSTRAP_SERVERS="kafka-cluster.prod.internal:9092"
export KAFKA_TOPIC="orders"
```
In `Program.cs`:
```csharp
builder.Services.AddNexJob()
    .AddKafkaProducer(options =>
    {
        options.BootstrapServers = builder.Configuration["KAFKA_BOOTSTRAP_SERVERS"];
    });
```

### Advanced Security & Tuning (`ConfigureConsumer` and `ConfigureProducer`)

You can customize the underlying Confluent.Kafka `ConsumerConfig` and `ProducerConfig` delegates for SASL/SCRAM, SSL/TLS certificates (via file path or raw PEM strings), and custom broker timeouts without relying on global environment variables:

```csharp
// Consumer configuration
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

// Producer configuration
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

### Option B: Direct `Environment.GetEnvironmentVariable`
```csharp
builder.Services.AddNexJob()
    .AddKafkaProducer(options =>
    {
        options.BootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS")
            ?? throw new InvalidOperationException("KAFKA_BOOTSTRAP_SERVERS environment variable is missing.");
    });
```

### Option C: Bind from a configuration section you choose

`NexJob.Kafka` does not read a `NexJob:Kafka` section or any `KAFKA_*` variable by itself. To keep the settings in `appsettings.json`, bind them yourself:

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

## 4. Partitioning, Ordering & Worker Concurrency

In Apache Kafka, message ordering is guaranteed **per partition** via the message `Key`.

When producing via NexJob:
- If strict FIFO ordering per partition key is required, run the producer queue on a single worker: publish with a dedicated queue (`queue: "kafka-ordered"`) and process that queue on a node with `options.Workers = 1` and `options.Queues = ["kafka-ordered"]`. NexJob does not guarantee execution order across workers, so ordering per key also depends on a single job running at a time.
- If high throughput with eventual delivery is sufficient, default worker concurrency publishes multiple messages concurrently across available workers.

---

## 5. Reliability & Failure Handling

| Failure Scenario | NexJob Producer Behavior |
|---|---|
| **Kafka Broker Down** | The job throws `ProduceException` and NexJob's retry policy is triggered. The message remains safe in NexJob storage. |
| **Retries Exhausted** | The message is dispatched to the Dead-Letter pipeline (`IDeadLetterHandler`) and surfaced on the Dashboard. |
| **Host Shutdown** | The registered `IKafkaProducerClient` automatically invokes `Flush()` to ensure all in-flight messages are delivered before exit. |
| **Serialization Error** | Serialization errors fail fast and route to dead-letter without crashing the worker host. |

> A job that came from the trigger and exhausted its retries stays **only inside NexJob** (the broker message was acknowledged when it became a job). To also send a copy to a Kafka topic, see [Forwarding a dead-lettered job](06-Retry-And-Dead-Letter.md#forwarding-a-dead-lettered-job-to-kafka-or-rabbitmq).

---

## 6. Sagas & Advanced Stream Orchestration (qKafka)

NexJob is designed for background job processing, resilient polling/wake-up loops, and reliable transactional outbox publishing.

If your architecture requires complex **Event-Driven Choreographies**, **Distributed Sagas with Compensating Transactions**, and state-machine transitions over Kafka topics, consider pairing NexJob with **[qKafka](https://github.com/oluciano/QKafka)**. NexJob and qKafka complement each other naturally: NexJob manages local task deadlines, retries, and persistence, while qKafka handles distributed stream correlations and compensations.
