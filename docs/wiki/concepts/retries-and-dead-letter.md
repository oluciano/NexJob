---
title: "NexJob Retries, Dead-Letter Handling, and Retry Policy"
sidebarTitle: "Retries & Dead Letter"
description: "Configure global and per-job retry policies with exponential backoff in NexJob, and register dead-letter handlers for jobs that exhaust all attempts."
---

NexJob automatically retries failed jobs using a configurable delay strategy. When a job exhausts its retry budget, it is marked as `Failed` and its dead-letter handler runs — giving you a chance to alert, compensate, or forward the failure before it goes silent. You control both layers: a global policy that applies to every job, and per-job overrides for jobs that need different behavior.

## Global Retry Policy

Set the default maximum number of attempts for all jobs in `AddNexJob`:

```csharp
builder.Services.AddNexJob(options =>
{
    options.MaxAttempts = 5; // 1 initial run + 4 retries
});
```

The default is **10 attempts** if you do not configure `MaxAttempts`.

### Default Delay Between Attempts

Unless a job sets its own `InitialDelay` with `[Retry]`, NexJob computes the wait time using `options.RetryDelayFactory`. The built-in factory uses a polynomial curve with jitter: `attempt⁴ + 15` seconds plus a random 0–29 seconds multiplied by `(attempt + 1)`. This grows quickly — roughly 16 to 74 seconds after the first failure and 4.5 to 7 minutes after the fourth.

## Per-Job Retry Override

Apply the `[Retry]` attribute to a job class to override the global policy for that job only. When you set `InitialDelay`, NexJob uses exponential backoff with ±10% jitter instead of the global factory.

```csharp
[Retry(5, InitialDelay = "00:00:30", Multiplier = 2.0, MaxDelay = "01:00:00")]
public sealed class ProcessPaymentJob : IJob<PaymentInput>
{
    public async Task ExecuteAsync(PaymentInput input, CancellationToken ct)
    {
        // 5 total attempts: first run + 4 retries
        // Delays (with ±10% jitter): ~30s → ~60s → ~120s → ~240s, capped at 1 hour
    }
}
```

### Attribute Parameters

| Parameter | Default | Description |
|---|---|---|
| `attempts` | Required | Total executions including the first run. `1` and `0` both mean no retry — the job is dead-lettered immediately on failure. |
| `InitialDelay` | `null` | Delay before the first retry as a `TimeSpan` string, e.g. `"00:00:30"`. When omitted, the global `RetryDelayFactory` is used and `Multiplier`/`MaxDelay` are ignored. |
| `Multiplier` | `2.0` | Exponential backoff multiplier. Delay = `InitialDelay × Multiplier^(retry − 1)`. |
| `MaxDelay` | No cap | Upper bound on any single delay as a `TimeSpan` string, e.g. `"01:00:00"`. |

A random ±10% jitter is applied to every delay computed from `InitialDelay` to spread retry spikes across workers.

### Immediate Dead-Letter on Failure

Set `attempts` to `0` (or `1`) to skip all retries and send the job straight to dead-letter on its first failure. Use this for jobs where retrying cannot fix the problem — such as those that validate input data:

```csharp
[Retry(0)] // No retries — dead-letter immediately on any failure
public sealed class WebhookNotificationJob : IJob<WebhookInput>
{
    public async Task ExecuteAsync(WebhookInput input, CancellationToken ct)
    {
        // A failure here goes straight to the dead-letter handler
    }
}
```

## Execution Timeout

Apply `[ExecutionTimeout]` to a job class to cap how long it may run, or set `options.DefaultExecutionTimeout` for all jobs (`null` by default, so it is opt-in; the attribute wins).

```csharp
[ExecutionTimeout("00:05:00")]
public sealed class ReportJob : IJob<ReportInput>
{
    public Task ExecuteAsync(ReportInput input, CancellationToken ct) => _http.GetAsync(input.Url, ct);
}
```

At the limit the job's `CancellationToken` is cancelled and the run fails with a `TimeoutException`, then follows the normal failure path: the attempt is used, it is retried, and on the last attempt it is dead-lettered.

- **Cancellation is cooperative.** A job that ignores its token keeps its worker slot until it returns, so pass `ct` to every HTTP, database and delay call.
- The timer starts after any `[Throttle]` slots are held. A shutdown is not a timeout: the job goes back to the queue without using the attempt.

## Custom Retry Delay Factory

Replace the built-in delay curve entirely by assigning a delegate to `options.RetryDelayFactory`. The delegate receives the number of attempts already made (1 after the first failure) and returns the `TimeSpan` to wait before the next attempt. This affects all jobs that do not set their own `InitialDelay`.

```csharp
builder.Services.AddNexJob(options =>
{
    // Exponential backoff with up to 1 second of random jitter: 2s, 4s, 8s, ...
    options.RetryDelayFactory = attempt =>
        TimeSpan.FromSeconds(Math.Pow(2, attempt))
        + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
});
```

## IDeadLetterHandler&lt;TJob&gt;

Register a dead-letter handler to react when a job permanently fails. The handler runs in an isolated DI scope — exceptions are logged and swallowed, so a failing handler never crashes the dispatcher or prevents other jobs from running.

```csharp
public sealed class PaymentDeadLetterHandler : IDeadLetterHandler<ProcessPaymentJob>
{
    private readonly IAlertService _alerts;
    private readonly IRefundService _refunds;

    public PaymentDeadLetterHandler(IAlertService alerts, IRefundService refunds)
    {
        _alerts = alerts;
        _refunds = refunds;
    }

    public async Task HandleAsync(
        JobRecord failedJob,
        Exception lastException,
        CancellationToken cancellationToken)
    {
        await _alerts.SendAsync(
            $"Payment job {failedJob.Id} failed after {failedJob.Attempts} attempts: {lastException.Message}",
            cancellationToken);

        // Optionally trigger compensation logic
        // var input = JsonSerializer.Deserialize<PaymentInput>(failedJob.InputJson);
        // await _refunds.InitiateAsync(input!.OrderId, cancellationToken);
    }
}

// Register in Program.cs
builder.Services.AddTransient<IDeadLetterHandler<ProcessPaymentJob>, PaymentDeadLetterHandler>();
```

### What the JobRecord Contains

The `JobRecord` passed to your handler gives you everything you need to diagnose the failure or trigger compensation:

<div class="grid cards" markdown>
  -   **Id**

    The failed job's unique identifier.

  -   **Attempts / MaxAttempts**

    How many times the job ran and what the configured limit was.

  -   **LastErrorMessage**

    The exception message from the final failure.

  -   **LastErrorStackTrace**

    Full stack trace from the final failure.

  -   **InputJson**

    Serialized input — deserialize with `System.Text.Json` to recover the original payload.

  -   **Queue / Tags / CreatedAt**

    Full execution context for routing, filtering, and auditing.

</div>

### When the node dies

A job can also run out of attempts without ever reporting an error: the process running it died (out of memory, `kill -9`, a lost machine) and each recovery used up an attempt. When the orphan watcher finds such a job with no attempts left, it marks it `Failed` and runs the same dead-letter handling: the typed `IDeadLetterHandler<TJob>` and every `IDeadLetterForwarder`.

The exception you receive is an `OrphanedJobException`, because the job never threw one. It carries the job id and how many attempts were used, so a handler can tell a crash from an ordinary failure:

```csharp
public Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken ct)
{
    if (lastException is OrphanedJobException)
    {
        // The node died while running this job: likely a poison input (for example, out of memory).
    }

    return Task.CompletedTask;
}
```

- **Once per job.** The storage reports a job to the node whose scan moved it, so several nodes running the watcher never dead-letter the same job twice.
- **After the move.** The job is already `Failed` in storage when the handler runs. If the handler throws, or the node stops right then, the job stays `Failed` and the handler is not called again.
- **Custom storage providers.** Dead-letter handling for crashed jobs needs the storage to implement `IOrphanedJobReporter`. A provider that does not still fails those jobs, without calling any handler.

### Safety Guarantees

!!! note
    - Dead-letter handlers are **optional**. Jobs without a registered handler are simply marked `Failed` and remain visible in the dashboard for manual requeue.
      - Handlers run in an isolated DI scope. An exception thrown inside a handler is logged and swallowed — it never crashes the dispatcher.
      - The `IDeadLetterHandler<TJob>` generic parameter is the **job type**, not the input type.
      - Handlers work for both `IJob` and `IJob<TInput>` implementations.


## Forwarding a dead-lettered job

A job created from a consumed Kafka or RabbitMQ message that exhausts its retries stays **only inside NexJob** (`Failed` in the dashboard), because the broker message was acknowledged when it became a job. To hand a copy of the original message to another topic or queue, use the built-in forwarding of the trigger.


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


### Custom forwarding with `IDeadLetterForwarder`

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


## Choosing the Right Strategy

Use the table below to pick the right approach for common failure scenarios:

| Scenario | Recommended Approach |
|---|---|
| Transient network error (timeout, connection reset) | Retries with exponential backoff |
| External API rate limiting | Retries with longer `InitialDelay` and higher `MaxDelay` |
| Invalid input data | `[Retry(0)]` — dead-letter immediately |
| Business rule violation | `[Retry(0)]` — retry won't fix it |
| Database deadlock | Retries with a short `InitialDelay`, 2–3 attempts |
| Payment or legal operation failure | Dead-letter handler with alerting and compensation |

!!! warning
    Jobs in the `Failed` state are retained for 30 days by default, then automatically purged. Once a job is purged, its history and input are gone permanently. If you need longer retention for audit purposes, increase `options.RetentionFailed` — or set it to `TimeSpan.Zero` to keep failed jobs indefinitely.

