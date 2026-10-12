---
title: "NexJob RabbitMQ: Triggers and Reliable Outbox Producer"
sidebarTitle: "RabbitMQ"
description: "Use NexJob.RabbitMQ to turn RabbitMQ messages into NexJob jobs and to publish messages through a durable outbox with publisher confirms, with its options, defaults and failure behavior."
---

`NexJob.RabbitMQ` has two independent halves in one package:

- the **trigger** consumes a RabbitMQ queue and enqueues each message as a NexJob job;
- the **outbox producer** stores a message in NexJob's storage first and publishes it to RabbitMQ from a job, with publisher confirms, so a broker outage does not lose it.

You can use either one alone. For what all triggers have in common (the message contract, how the job type is resolved, the idempotency key and the error model), see the [overview](triggers.md).

## Install

```bash
dotnet add package NexJob.RabbitMQ
```

## The trigger (consumer)

### Register it

Bind a RabbitMQ queue to one job type with the generic overload. The job is registered in DI as `Transient`:

```csharp
builder.Services.AddNexJob()
    .AddRabbitMqTrigger<ProcessOrderJob>(options =>
    {
        options.HostName = builder.Configuration["RABBITMQ_HOST"] ?? "localhost";
        options.UserName = builder.Configuration["RABBITMQ_USER"] ?? "guest";
        options.Password = builder.Configuration["RABBITMQ_PASSWORD"] ?? "guest";
        options.QueueName = "incoming-orders";
        options.TargetQueue = "orders";
        options.PrefetchCount = 10;
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

The job receives the message body as UTF-8 text and decides how to parse it. The same registration exists on the service collection as `services.AddNexJobRabbitMqTrigger<ProcessOrderJob>(...)`.

To route messages to different job types, omit the generic argument and have the publisher set the `nexjob.job_type` header to the assembly-qualified name of the job. `JobType` is the fallback for messages without the header; when both exist, the header wins:

```csharp
builder.Services.AddNexJob()
    .AddRabbitMqTrigger(options =>
    {
        options.HostName = "localhost";
        options.QueueName = "incoming-events";
        // options.JobType = typeof(DefaultEventJob).AssemblyQualifiedName; // optional fallback
    });
```

### Options

| Option | Default | Meaning |
|---|---|---|
| `QueueName` | required | The queue to consume. It must already exist; the trigger does not declare it. |
| `HostName` | `"localhost"` | The broker host. |
| `Port` | `5672` | The AMQP port. |
| `UserName`, `Password` | `"guest"`, `"guest"` | The credentials. RabbitMQ only lets `guest` connect from the same machine, so set real ones elsewhere. |
| `VirtualHost` | `"/"` | The virtual host. |
| `PrefetchCount` | `1` | How many unacknowledged messages the broker may send at once. |
| `JobType` | `null` | The job type for messages without a `nexjob.job_type` header. |
| `TargetQueue` | `"default"` | The NexJob queue the jobs go to. `"default"` is the application's `{prefix}.default` queue ([details](../concepts/default-queue.md)). |
| `JobPriority` | `Normal` | The priority of the jobs. |
| `ReconnectDelay` | 5 seconds | The wait between attempts to reconnect. |
| `ExhaustedJobsRoutingKey` | `null` | When set, a job that exhausts its retries is copied to this routing key. Needs `AddRabbitMqProducer`. |
| `ExhaustedJobsExchange` | `""` | The exchange for those copies; the default exchange when empty. |
| `ExhaustedJobsIncludeErrorHeader` | `false` | Adds the last error message to those copies, in a `nexjob.error` header. |

A missing `HostName` or `QueueName` fails when the host starts. The connection is made from `HostName`, `Port`, `UserName`, `Password` and `VirtualHost`; for TLS or any other connection setting, see [TLS and other connection settings](#tls-and-other-connection-settings).

### How a message becomes a job

- **Input:** the message body, as UTF-8 text.
- **Job type:** the `nexjob.job_type` header, else `JobType`.
- **Idempotency key:** the AMQP `MessageId`. It deduplicates only against jobs that are still active, and **a message without a `MessageId` has no key**, so every delivery of it creates a job. Set a unique `MessageId` on every message you publish to a queue that feeds a trigger. See [the overview](triggers.md#idempotency-keys-by-broker).
- **Trace:** a `traceparent` header, when present, becomes the job's trace parent.
- **Ack:** the message is acknowledged only after the job was enqueued.

With the default `PrefetchCount` of 1, messages are handled one at a time.

### What happens when it fails

| What happened | What the trigger does |
|---|---|
| The job was enqueued | The message is acknowledged. |
| A transient failure (storage down, network, timeout) | A warning is logged, the trigger waits 1 second and nacks the message with requeue, so the broker delivers it again. |
| The message can never become a job (no job type, a malformed payload) | It is nacked **without requeue**. With a dead-letter exchange on the queue, the broker routes it there; without one, the broker **discards it**. |
| The host is stopping | The message is nacked with requeue. |
| The broker is down at start | The host still starts. A warning is logged and the trigger retries every `ReconnectDelay`. |
| The connection drops | The trigger reconnects the same way. |
| The queue does not exist | The same reconnect loop, forever. Checked: a warning every `ReconnectDelay` and no failure of the host. |

!!! warning
    Configure a **dead-letter exchange** on the queue the trigger consumes. A message that can never become a job is otherwise thrown away, with only a warning in the log. Checked against a real broker: a message without a job type, on a queue with no dead-letter exchange, was removed and a single warning was logged.

### Forwarding exhausted jobs

Set `ExhaustedJobsRoutingKey` (and `ExhaustedJobsExchange` if you do not use the default exchange) to copy a job to another queue once it exhausts its retries. The copy goes through the outbox producer, so `AddRabbitMqProducer` is required (the host fails to start without it). The job stays `Failed` in NexJob, and only the message body is forwarded. A job is forwarded when it carries the RabbitMQ trigger tag and its queue is this trigger's `TargetQueue`. See [Forwarding a dead-lettered job](../concepts/dead-letter-forwarding.md) for the full behavior and its limits.

### Listener status

The trigger registers with the [listener registry](triggers.md#live-listener-registry) and appears on the dashboard's `/listeners` page: `Listening` once it consumes, `Reconnecting` when the connection fails or drops, and `Stopped` on shutdown. It goes back to `Listening` after a successful reconnect.

## The outbox producer

### How it works

```text
[Your service]  scheduler.EnqueueRabbitMqAsync(...)
       │
       ▼
[NexJob storage]   a RabbitMqProducerJob is stored
       │
       ▼
[A worker]         runs the job: publish, then wait for the broker's confirm
       │
       ▼
[RabbitMQ]
```

The message is safe in storage once `EnqueueRabbitMqAsync` returns. If RabbitMQ is unreachable, or the broker does not confirm in time, the job fails and NexJob's [retry policy](../concepts/retries.md) tries again.

### Register it

```csharp
builder.Services.AddNexJob()
    .AddRabbitMqProducer(options =>
    {
        options.HostName = builder.Configuration["RABBITMQ_HOST"] ?? "localhost";
        options.UserName = builder.Configuration["RABBITMQ_USER"] ?? "guest";
        options.Password = builder.Configuration["RABBITMQ_PASSWORD"] ?? "guest";
        options.ConfirmTimeout = TimeSpan.FromSeconds(5);
    });
```

!!! note "Which queue the outbox jobs use"
    The producer jobs go to the `default` queue, which a worker polls out of the box. Pass `queue:` to `EnqueueRabbitMqAsync` to name another one; a queue you name is polled only when it is in `NexJobOptions.Queues`. Before 6.3.0 the default was `rabbitmq-producer`, which no worker polled unless it was listed; jobs already waiting there are only drained while it stays in `Queues`.

| Option | Default | Meaning |
|---|---|---|
| `HostName`, `Port`, `UserName`, `Password`, `VirtualHost` | `localhost`, `5672`, `guest`, `guest`, `/` | The connection. For TLS, see [below](#tls-and-other-connection-settings). |
| `ConfirmTimeout` | 5 seconds | How long to wait for the broker's confirm. |
| `Mandatory` | `false` | Fail the publish when the broker cannot route the message. See the note below. |
| `DefaultExchange`, `DefaultRoutingKey` | `""`, `null` | The exchange and routing key used when the call passes none. See the note below. |
| `Queue`, `DefaultPriority` | `"default"`, `Normal` | **No effect.** They are declared but nothing reads them; the queue and priority come from the arguments of `EnqueueRabbitMqAsync`. |

!!! note "What `Mandatory`, `DefaultExchange` and `DefaultRoutingKey` do"
    - **`Mandatory`** (or `mandatory: true` on the call) makes the publish fail when the broker cannot route the message to any queue: the job fails with an `InvalidOperationException` that carries the broker's reply (for example `312 NO_ROUTE`) and goes through the retry and dead-letter flow. Without it, the broker drops an unroutable message silently. A publish to an exchange that does not exist fails either way.
    - **`DefaultExchange`** is used when the call names no exchange: `EnqueueRabbitMqAsync(routingKey, value)`, or `exchange: null` on the longer overload. It is empty by default, which is the broker's default exchange. An exchange you pass, even `""`, wins.
    - **`DefaultRoutingKey`** is used when the call passes `routingKey: null`. A routing key you pass, even `""`, wins.

### Publish messages

Inject `IScheduler` and call `EnqueueRabbitMqAsync`. Set `messageId` on every message, so a consuming trigger can deduplicate it:

```csharp
public class OrderService(IScheduler scheduler)
{
    public Task CreateOrderAsync(OrderCreatedEvent order, CancellationToken ct) =>
        scheduler.EnqueueRabbitMqAsync(
            exchange: "orders.events",
            routingKey: "order.created",
            value: order,                              // serialized as JSON
            correlationId: order.OrderId.ToString(),
            messageId: Guid.NewGuid().ToString(),
            cancellationToken: ct);

    // routing key only: publishes to the default exchange
    public Task SendNotificationAsync(NotificationEvent notification, CancellationToken ct) =>
        scheduler.EnqueueRabbitMqAsync("notifications", notification, cancellationToken: ct);

    // a JSON string, sent verbatim
    public Task PublishRawAsync(string routingKey, string json, CancellationToken ct) =>
        scheduler.EnqueueRabbitMqRawAsync("orders.events", routingKey, json, cancellationToken: ct);

    // bytes
    public Task PublishBinaryAsync(string exchange, string routingKey, byte[] data, CancellationToken ct) =>
        scheduler.EnqueueRabbitMqRawAsync(exchange, routingKey, data, cancellationToken: ct);
}
```

Besides those, the call takes `headers`, `mandatory`, `queue`, `priority`, `idempotencyKey`, `duplicatePolicy`, `tags` and `deadlineAfter`. Things to know:

- **Messages are persistent.** Each is published with the persistent flag, with content type `application/json` (`application/octet-stream` for bytes), `AppId` `NexJob` and a timestamp. The queue itself must be durable for that to matter.
- **Serialization happens at the call.** An object that cannot be serialized throws from `EnqueueRabbitMqAsync` to your code, before anything is stored.
- **Delivery is at least once.** A job can publish and then fail before NexJob records it, so a message can be published twice. The `messageId` is what lets a NexJob trigger on the other side ignore the repeat while the first job is active.
- **The trace** of the publishing job is sent in the `traceparent` header.

### Publisher confirms

Every channel the producer opens is in confirm mode. After each publish the job waits up to `ConfirmTimeout` for the broker. A negative confirm throws an `InvalidOperationException`, and a timeout throws a `TimeoutException` and discards the channel; either way the job fails and is retried. A publish to an exchange that does not exist also fails and is retried (checked). The producer uses one channel per node and publishes one message at a time, and it reconnects on its own when the connection is lost.

A confirm means the broker took the message. It does not mean a queue received it: see the warning above about unroutable messages.

## TLS and other connection settings

Both options classes (`RabbitMqTriggerOptions` and `RabbitMqProducerOptions`) have a `ConfigureConnection` delegate over the `ConnectionFactory`, like Kafka's `ConfigureConsumer`. Use it for TLS, heartbeat, a client name or any other `ConnectionFactory` property:

```csharp
builder.Services.AddNexJob()
    .AddRabbitMqTrigger<ProcessOrderJob>(options =>
    {
        options.HostName = "rabbit.prod.example.com";
        options.Port = 5671;
        options.QueueName = "incoming-events";
        options.ConfigureConnection = factory =>
        {
            factory.Ssl = new SslOption { Enabled = true, ServerName = "rabbit.prod.example.com" };
            factory.RequestedHeartbeat = TimeSpan.FromSeconds(30);
            factory.ClientProvidedName = "orders-worker";
        };
    });
```

The delegate runs before `HostName`, `Port`, `UserName`, `Password` and `VirtualHost` are applied, so those five settings always win: set them on the options, not in the delegate. It runs on every connection attempt, including each reconnect, so keep it cheap and free of side effects. A delegate that throws fails that attempt (the trigger logs it and keeps retrying; the producer's publish fails and is retried by the job).

If you register your own `IConnectionFactory` in DI instead, the trigger and the producer use it and overwrite the same five settings on it. The factory is then shared by both and the last registration wins. The delegate is applied to that factory too. TLS itself was not run against a TLS-enabled broker; what is checked is that the delegate's settings reach the factory used for the connection, and a real broker accepts a connection configured this way.

## Configuration from `appsettings.json` and the environment

The package does not read fixed environment variable names. Read them from `IConfiguration`, which includes environment variables in the default host:

```csharp
builder.Services.AddNexJob()
    .AddRabbitMqTrigger(options =>
    {
        options.HostName = builder.Configuration["RABBITMQ_HOST"] ?? "localhost";
        options.Port = int.TryParse(builder.Configuration["RABBITMQ_PORT"], out var port) ? port : 5672;
        options.VirtualHost = builder.Configuration["RABBITMQ_VIRTUAL_HOST"] ?? "/";
        options.QueueName = "incoming-events";
    });
```

Or bind a section: `.AddRabbitMqTrigger(options => builder.Configuration.GetSection("RabbitMQ").Bind(options))`. Keep the password in a secret store.

## What was checked

Against a single RabbitMQ 3.13 broker: the outbox published only when its queue was polled; a mandatory unroutable publish failed with `312 NO_ROUTE`, and a routable one and a non-mandatory unroutable one behaved as before; `DefaultExchange` and `DefaultRoutingKey` applied; a publish to a missing exchange failed; the trigger on a missing queue kept reconnecting; a message without a job type was discarded when the queue had no dead-letter exchange. A cluster, a TLS-enabled broker, a dead-letter exchange, `ExhaustedJobsRoutingKey` and prefetch above 1 were not run.

## Try it

The [`NexJob.Sample.RabbitMQ`](https://github.com/oluciano/NexJob/tree/develop/samples/NexJob.Sample.RabbitMQ) sample runs the outbox producer and the trigger against a RabbitMQ container from the Docker Compose stack in `samples/`.
