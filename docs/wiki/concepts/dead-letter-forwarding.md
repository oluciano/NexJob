---
title: "NexJob Dead-Letter Forwarding: Copy Exhausted Jobs to a Broker"
sidebarTitle: "Dead-letter forwarding"
description: "Hand a copy of a job that exhausted its attempts to a Kafka topic, a RabbitMQ queue or any destination, through the trigger's built-in forwarding or an IDeadLetterForwarder."
---

A job created from a consumed Kafka or RabbitMQ message that exhausts its retries stays **only inside NexJob** (`Failed` in the dashboard), because the broker message was acknowledged when it became a job. To hand a copy of the original message to another topic or queue, use the built-in forwarding of the trigger.

## Forward a copy to Kafka or RabbitMQ

=== "Kafka"

    ```csharp
    builder.Services.AddNexJob()
        .AddKafkaProducer(opt => opt.BootstrapServers = "localhost:9092")   // the copy goes through the Outbox
        .AddKafkaTrigger<ProcessOrderJob>(opt =>
        {
            opt.BootstrapServers = "localhost:9092";
            opt.Topic = "orders";
            opt.GroupId = "orders-worker";
            opt.TargetQueue = "orders";

            opt.ExhaustedJobsTopic = "orders.exhausted";          // turns forwarding on
            opt.ExhaustedJobsIncludeErrorHeader = false;          // optional: add the last error as header `nexjob.error`
        });
    ```

=== "RabbitMQ"

    ```csharp
    builder.Services.AddNexJob()
        .AddRabbitMqProducer(opt => opt.HostName = "localhost")             // the copy goes through the Outbox
        .AddRabbitMqTrigger<ProcessOrderJob>(opt =>
        {
            opt.QueueName = "orders";
            opt.TargetQueue = "orders";

            opt.ExhaustedJobsExchange = string.Empty;             // default exchange
            opt.ExhaustedJobsRoutingKey = "orders.exhausted";     // turns forwarding on (the queue name on the default exchange)
            opt.ExhaustedJobsIncludeErrorHeader = false;          // optional: add the last error as header `nexjob.error`
        });
    ```



What you get:

- **A copy of what was received.** The message body is published verbatim and the job **stays in NexJob** as `Failed`, so it is still visible and can still be requeued from the dashboard.
- **Only that trigger's jobs.** Forwarding applies to jobs the trigger created on its `TargetQueue`. Jobs enqueued by hand, jobs of other queues and the Outbox publisher jobs are never forwarded, so it cannot loop.
- **Durable and retried.** The copy is an Outbox job: a broker that is down delays it, and the Outbox retry policy keeps trying. If that also exhausts, the publish job is `Failed` and visible in the dashboard.
- **Safe for the dispatcher.** A forwarder that throws is logged and swallowed; it never crashes the dispatcher, and it never stops a dead-letter handler you wrote for the same job type.
- **Fail fast.** Turning forwarding on without registering the matching producer (`AddKafkaProducer` or `AddRabbitMqProducer`) fails when the host starts.

!!! warning
    - Only the **body** is forwarded. The original key, headers and message properties are not stored with the job. The content type of a forwarded RabbitMQ message is `application/json`.
    - A job requeued from the dashboard that is then processed again by the service reading the forwarded copy runs twice. Treat forwarded jobs as handled elsewhere, or make the consumer idempotent.
    - `ExhaustedJobsTopic` is not `DeadLetterTopic`: the latter receives messages that could never become a job (unknown job type, malformed payload).


## Custom forwarding with `IDeadLetterForwarder`

For other destinations, implement `IDeadLetterForwarder` and register it. After the typed `IDeadLetterHandler<TJob>` runs, the dispatcher calls every registered forwarder whose `AppliesTo` returns `true`. Each forwarder is isolated, so one that throws never prevents another from running.

```csharp
public sealed class AuditForwarder(IAuditSink sink) : IDeadLetterForwarder
{
    public bool AppliesTo(JobRecord failedJob) => failedJob.Queue == "orders";

    public Task ForwardAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken) =>
        sink.WriteAsync(failedJob.Id, failedJob.InputJson, lastException.Message, cancellationToken);
}

builder.Services.AddSingleton<IDeadLetterForwarder, AuditForwarder>();
```

The dispatcher counts forwards in `nexjob.dead_letter.forwarded` and failed ones in `nexjob.dead_letter.forward_failed` (tag `nexjob.forwarder` with the forwarder type name).

!!! tip
    To be **notified** when a job exhausts its attempts (Slack, Teams, e-mail, Grafana), see the [Alerts](../guides/alerts.md) guide.

## Next

- [Dead-letter handling](dead-letter.md): the handler that runs before the forwarders.
- [Kafka](../integrations/kafka.md) and [RabbitMQ](../integrations/rabbitmq.md): the trigger and producer configuration.
