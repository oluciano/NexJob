---
title: "Kafka Integration: Triggers and Resilient Outbox for NexJob"
sidebarTitle: "Kafka"
description: "Use NexJob.Kafka to turn Kafka topic messages into NexJob jobs and to publish messages to Kafka through a durable outbox, with its options, defaults, ordering limits and failure behavior."
---

`NexJob.Kafka` has two independent halves in one package:

- the **trigger** reads a topic and enqueues each record as a NexJob job (a consumer);
- the **outbox producer** stores a message in NexJob's storage first and publishes it to Kafka from a job, so a broker outage does not lose it (a producer).

You can use either one alone. For what all triggers have in common (the message contract, how the job type is resolved, the idempotency key and the error model), see the [overview](triggers.md).

## Install

```bash
dotnet add package NexJob.Kafka
```

## The trigger (consumer)

### Register it

Bind a topic to one job type with the generic overload. The job is registered in DI as `Transient`:

```csharp
builder.Services.AddNexJob()
    .AddKafkaTrigger<ProcessOrderJob>(options =>
    {
        options.BootstrapServers = builder.Configuration["KAFKA_BOOTSTRAP_SERVERS"] ?? "localhost:9092";
        options.Topic = "incoming-orders";
        options.GroupId = "nexjob-order-consumer";
        options.TargetQueue = "orders";
    });

public sealed class ProcessOrderJob : IJob<string>
{
    public async Task ExecuteAsync(string messagePayload, CancellationToken ct)
    {
        var order = JsonSerializer.Deserialize<OrderDto>(messagePayload);
        // ...
    }
}
```

The job receives the record value as text and decides how to parse it. The same registration exists on the service collection as `services.AddNexJobKafkaTrigger<ProcessOrderJob>(...)`.

To route records to different job types, omit the generic argument and have the producer set the `nexjob.job_type` header to the assembly-qualified name of the job. `JobType`, when configured, always wins over the header — a producer cannot override it; the header is read only when `JobType` is unset:

```csharp
builder.Services.AddNexJob()
    .AddKafkaTrigger(options =>
    {
        options.BootstrapServers = "localhost:9092";
        options.Topic = "incoming-events";
        options.GroupId = "events-consumer";
        // options.JobType = typeof(DefaultEventJob).AssemblyQualifiedName; // optional fallback
    });
```

### Options

| Option | Default | Meaning |
|---|---|---|
| `BootstrapServers` | required | The brokers, as `host:port,host:port`. |
| `Topic` | required | The topic to read. One trigger reads one topic. |
| `GroupId` | required | The consumer group. |
| `JobType` | `null` | The job type to use for every record. When set, it always wins over the `nexjob.job_type` header — a producer cannot override it. When unset, the header is read instead. |
| `TargetQueue` | `"default"` | The NexJob queue the jobs go to. `"default"` is the application's `{prefix}.default` queue ([details](../concepts/default-queue.md)). |
| `JobPriority` | `Normal` | The priority of the jobs. |
| `DeadLetterTopic` | `null` | A topic that receives the records that can never become jobs. |
| `ExhaustedJobsTopic` | `null` | A topic that receives a copy of a job once it exhausts its retries. Needs `AddKafkaProducer`. |
| `ExhaustedJobsIncludeErrorHeader` | `false` | Adds the last error message to those copies, in a `nexjob.error` header. |
| `ConsumeTimeout` | 1 second | How long one poll of the consumer waits for a record. |
| `ConfigureConsumer` | `null` | A delegate over the Confluent `ConsumerConfig`: SASL, TLS, timeouts. |

A missing `BootstrapServers`, `Topic` or `GroupId` fails when the host starts.

The consumer is created with `AutoOffsetReset = Earliest`, then your `ConfigureConsumer` runs, then `BootstrapServers`, `GroupId` and `EnableAutoCommit = false` are forced back, so the delegate cannot change those three.

!!! warning
    A consumer group that has no committed offset starts from the **beginning of the topic**, because the default is `Earliest`. Checked: a new group read a record that had been produced before the group existed. To start from the end, set `config.AutoOffsetReset = AutoOffsetReset.Latest` in `ConfigureConsumer`.

### How a record becomes a job

- **Input:** the record value, as text.
- **Job type:** `JobType` when configured, else the `nexjob.job_type` header.
- **Idempotency key:** `kafka:{topic}:{partition}:{offset}`, the position of the record. It deduplicates only against jobs that are still active: a record read again after its job has finished (for example after a consumer group reset) creates a new job. See [the overview](triggers.md#idempotency-keys-by-broker).
- **Trace:** a `traceparent` header, when present, becomes the job's trace parent.
- **Commit:** the offset is committed only after the job was enqueued. If only the commit fails, the record is read again and the key keeps it from becoming a second job while the first is active.

Records are handled one at a time, in the order the consumer receives them.

### What happens when it fails

| What happened | What the trigger does |
|---|---|
| The job was enqueued | The offset is committed. |
| A transient failure (storage down, network, timeout) | The same record is retried in place, waiting 1, 2, 5, 10, 20 and then 30 seconds between attempts, and it does not stop. No other record is read meanwhile, so the order is kept and no later offset is committed past it. |
| The record can never become a job (no job type, a malformed payload) | With `DeadLetterTopic`: the record is copied there, then its offset is committed. Without it: an error is logged and the offset is committed, so the partition is not blocked and **the record is skipped**. |
| Copying to the dead-letter topic fails | The error is logged and the offset is **not** committed; the record is read again after the next rebalance or restart. |
| The host is stopping | The offset is not committed and the record is read again on restart. |

The dead-letter copy keeps the key, the value and the original headers, and adds `nexjob.error`, `nexjob.original_topic`, `nexjob.original_partition` and `nexjob.original_offset`. The partition and the offset are raw binary integers, not text. Checked against a real broker: a record without a job type reached the dead-letter topic with those headers and the consumer went on. The dead-letter producer is built from the consumer's configuration, so the SASL and TLS settings apply to it too.

A transient failure holds the whole consumer. Because the trigger does not poll while it waits, an outage longer than the consumer's `max.poll.interval.ms` (5 minutes by default in Kafka) gets it removed from the group, which triggers a rebalance. If your storage can be down for longer, raise `MaxPollIntervalMs` in `ConfigureConsumer`. This follows from the code and Kafka's documented default; it was not run.

### Forwarding exhausted jobs

Set `ExhaustedJobsTopic` to copy a job to another topic once it exhausts its retries. The copy goes through the outbox producer, so `AddKafkaProducer` is required (the host fails to start without it). The job stays `Failed` in NexJob, and only the record value is forwarded, with no key. A job is forwarded only when it came from this trigger's topic and queue. See [Forwarding a dead-lettered job](../concepts/dead-letter-forwarding.md) for the full behavior and its limits.

### Listener status

The trigger registers with the [listener registry](triggers.md#live-listener-registry) and appears on the dashboard's `/listeners` page, with its consumer group: `Listening` once it subscribes, `Reconnecting` after a consume error and `Faulted` after an unexpected error in the loop. Read from the source: it is not set back to `Listening` afterwards.

## The outbox producer

### How it works

```text
[Your service]  scheduler.EnqueueKafkaAsync(...)
       │
       ▼
[NexJob storage]   a KafkaProducerJob is stored
       │
       ▼
[A worker]         runs the job: ProduceAsync to Kafka, with a traceparent header
       │
       ▼
[Kafka]
```

The message is safe in storage from the moment `EnqueueKafkaAsync` returns. If Kafka is unreachable the job fails and NexJob's [retry policy](../concepts/retries.md) tries again; when the attempts are exhausted the job is dead-lettered like any other.

### Register it

```csharp
builder.Services.AddNexJob()
    .AddKafkaProducer(options =>
    {
        options.BootstrapServers = builder.Configuration["KAFKA_BOOTSTRAP_SERVERS"] ?? "localhost:9092";
        options.Acks = Confluent.Kafka.Acks.All;
        options.EnableIdempotence = true;
        options.FlushTimeout = TimeSpan.FromSeconds(10);
    });
```

!!! note "Which queue the outbox jobs use"
    The producer jobs go to the `default` queue, which a worker polls out of the box. Pass `queue:` to `EnqueueKafkaAsync` to name another one; a queue you name is polled only when it is in `NexJobOptions.Queues`. Before 6.3.0 the default was `kafka-producer`, which no worker polled unless it was listed; jobs already waiting there are only drained while it stays in `Queues`.

| Option | Default | Meaning |
|---|---|---|
| `BootstrapServers` | required | The brokers. |
| `Acks` | `All` | The acknowledgements the broker must give. |
| `EnableIdempotence` | `true` | The Kafka producer's idempotence. |
| `FlushTimeout` | 10 seconds | How long the producer waits to flush on shutdown. |
| `ConfigureProducer` | `null` | A delegate over the Confluent `ProducerConfig`. |
| `Queue`, `DefaultPriority` | `"default"`, `Normal` | **No effect.** They are declared but nothing reads them; the queue and priority come from the arguments of `EnqueueKafkaAsync`. |

`BootstrapServers`, `Acks` and `EnableIdempotence` are forced back after `ConfigureProducer` runs, so the delegate cannot change them. On shutdown the producer flushes what it has in flight.

### Publish messages

Inject `IScheduler` and call `EnqueueKafkaAsync`. A `string` or `byte[]` is sent as it is, and any other object is serialized as JSON:

```csharp
public class OrderService(IScheduler scheduler)
{
    public Task CreateOrderAsync(OrderCreatedEvent order, CancellationToken ct) =>
        scheduler.EnqueueKafkaAsync(
            topic: "orders-topic",
            key: order.OrderId.ToString(),
            value: order,                      // serialized as JSON
            cancellationToken: ct);

    public Task PublishRawAsync(string orderId, string json, CancellationToken ct) =>
        scheduler.EnqueueKafkaAsync("orders-topic", orderId, json, cancellationToken: ct);        // sent verbatim

    public Task PublishBinaryAsync(string key, byte[] data, CancellationToken ct) =>
        scheduler.EnqueueKafkaAsync("events-topic", key, data, cancellationToken: ct);            // sent as bytes
}
```

Besides `topic`, `key` and `value`, the call takes `headers`, `queue`, `priority`, `idempotencyKey`, `duplicatePolicy`, `tags` and `deadlineAfter`. Things to know:

- **No key means an empty key.** A message enqueued with `key: null` is sent with an empty string key. Kafka routes by key, so pass one whenever the partition matters.
- **Serialization happens at the call.** An object that cannot be serialized throws from `EnqueueKafkaAsync` to your code, before anything is stored.
- **Delivery is at least once.** A job can publish and then fail before NexJob records it, so the message can be published twice. Give the consumers a way to ignore a repeat. The `idempotencyKey` argument only keeps your own code from storing the same message twice while the first is active.
- **The trace** of the publishing job is sent in the `traceparent` header.

## Ordering and worker concurrency

Kafka orders records within a partition, by key. The outbox does not preserve that order by itself. To keep the publishing in order as much as NexJob can, send the messages to a dedicated queue and let one worker run it:

```csharp
await scheduler.EnqueueKafkaAsync("orders-topic", order.TenantId.ToString(), order,
    queue: "kafka-ordered", cancellationToken: ct);

builder.Services.AddNexJob(options =>
{
    options.Workers = 1;
    options.Queues = ["kafka-ordered"];
});
```

This is not a strict guarantee. A job that fails is rescheduled after its retry delay, so the next message can be published before it; and two nodes polling the queue publish in parallel. If order matters across failures, the consumer needs to cope with it (a sequence number in the message, for example). This is reasoning from how retries work, not something run here.

## Security: SASL and TLS

Use `ConfigureConsumer` and `ConfigureProducer` to set SASL, TLS and timeouts:

```csharp
builder.Services.AddNexJob()
    .AddKafkaTrigger(options =>
    {
        options.BootstrapServers = "kafka.prod:9093";
        options.Topic = "incoming-orders";
        options.GroupId = "order-consumers";
        options.ConfigureConsumer = config =>
        {
            config.SecurityProtocol = SecurityProtocol.SaslSsl;
            config.SaslMechanism = SaslMechanism.ScramSha512;
            config.SaslUsername = builder.Configuration["Kafka:User"];
            config.SaslPassword = builder.Configuration["Kafka:Password"];
            config.SslCaPem = builder.Configuration["Kafka:CaPem"];
            config.SessionTimeoutMs = 45000;
        };
    })
    .AddKafkaProducer(options =>
    {
        options.BootstrapServers = "kafka.prod:9093";
        options.ConfigureProducer = config =>
        {
            config.SecurityProtocol = SecurityProtocol.SaslSsl;
            config.SaslMechanism = SaslMechanism.ScramSha512;
            config.SaslUsername = builder.Configuration["Kafka:User"];
            config.SaslPassword = builder.Configuration["Kafka:Password"];
            config.LingerMs = 20;
        };
    });
```

Keep the credentials in a secret store. The trigger and the producer have separate configuration, so set the security settings on both.

## Configuration from `appsettings.json`

The package does not read a fixed section. Bind the options from whichever one you choose:

```json
{ "Kafka": { "BootstrapServers": "localhost:9092", "Topic": "orders", "GroupId": "orders-consumer" } }
```

```csharp
builder.Services.AddNexJob()
    .AddKafkaTrigger(options => builder.Configuration.GetSection("Kafka").Bind(options));
```

## What was checked

Against a single-node Kafka in KRaft mode (Confluent `cp-kafka` 7.6.0): the trigger read a record that existed before its group; a record without a job type reached the dead-letter topic with the headers above; the outbox published a message when its queue was polled and did not when it was not. `ExhaustedJobsTopic` was run too: a job that exhausted its retries was copied to that topic with the `nexjob.error` header, and a blank topic forwarded nothing. The job type of a trigger must implement `IJob<string>`; one that does not fails at execution with an unhelpful error ([#510](https://github.com/oluciano/NexJob/issues/510)). Run on demand (`Category=Reliability.Validation`): a cluster of three KRaft brokers kept turning records into jobs, none lost and none doubled, after one broker was stopped; a `SASL_SSL` listener with the `PLAIN` mechanism and a certificate from a test CA accepted the trigger with the right password and left the records unread with a wrong one; and with storage slower than `max.poll.interval.ms` the consumer was evicted, the offset commit failed (`Kafka message ... was enqueued but its offset could not be committed`) and the record came back, reaching storage twice with the same `kafka:{topic}:{partition}:{offset}` key, which is what keeps it one job. `SCRAM-SHA-512` (the mechanism in the example above), mutual TLS and the ordering behavior were not run. The package is tested by its authors against KRaft clusters; ZooKeeper-based clusters are expected to work but were not tried here.

## Try it

The [`NexJob.Sample.Kafka`](https://github.com/oluciano/NexJob/tree/develop/samples/NexJob.Sample.Kafka) sample publishes events through the outbox and consumes them with the trigger. It uses the Kafka (KRaft) container from the Docker Compose stack in `samples/`.
