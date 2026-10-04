---
title: "NexJob Production Best Practices and Performance Tips"
sidebarTitle: "Best Practices"
description: "Production guidelines for NexJob: graceful shutdown, idempotency, queue isolation, storage selection, retry tuning, and connection pool sizing."
---

Running NexJob reliably in production requires a small set of deliberate decisions at design time: keeping jobs focused and idempotent, choosing retry policies that match your failure modes, isolating workloads by queue, and configuring graceful shutdown so worker restarts never lose work. This page collects those guidelines in one place.

## Job Design

### Keep Jobs Small and Focused

Each job should do one thing. If your job fetches data, transforms it, calls three APIs, and sends two emails, split it into multiple jobs connected with continuations.

```csharp
// Too broad — hard to retry, hard to test
public sealed class ProcessOrderJob : IJob<OrderInput> { ... }

// Focused — each step can retry independently
public sealed class ValidateOrderJob   : IJob<OrderInput>       { ... }
public sealed class ChargePaymentJob   : IJob<ChargeInput>      { ... }
public sealed class SendConfirmationJob : IJob<ConfirmationInput> { ... }
```

### Make Jobs Idempotent

NexJob can retry or re-execute jobs due to failures, orphan recovery, or manual requeue. Design every job so running it twice with the same input produces the same result.

```csharp
public sealed class ChargeCardJob : IJob<ChargeInput>
{
    public async Task ExecuteAsync(ChargeInput input, CancellationToken ct)
    {
        // Guard against double-charging
        var exists = await _payments.ExistsAsync(input.OrderId, ct);
        if (exists) return;

        await _payments.ChargeAsync(input.OrderId, input.Amount, ct);
    }
}
```

### Use Minimal Input

Pass only what the job needs. Avoid embedding full entity objects in job payloads — pass IDs and fetch inside the job instead.

```csharp
// Stores the entire Order object in the job queue — fragile and bloated
public sealed record ProcessOrderInput(Order FullOrder);

// Stores only the ID — lean and forward-compatible
public sealed record ProcessOrderInput(Guid OrderId);
```

## Retry Configuration

Match your retry policy to the kind of failure you expect. A policy suited for a transient network blip is wrong for a validation error that will never self-heal.

| Failure type | Max retries | Delay strategy | Rationale |
|---|---|---|---|
| Transient network error | 3 – 5 | Exponential, 30 s – 5 min | Usually resolves quickly |
| External API rate limit | 5 – 10 | Exponential, 1 min – 1 hr | May need to wait for a window reset |
| Database deadlock | 2 – 3 | Short, 1 – 5 s | Resolves on the next transaction |
| Validation error | 0 | N/A | Retrying bad input never helps |

### Set Deadlines for Time-Sensitive Jobs

If a job is useless after a certain point — a promotional email, a real-time notification — set a `deadlineAfter` so NexJob discards it rather than delivering it late.

```csharp
await scheduler.EnqueueAsync<SendPromoEmailJob>(
    deadlineAfter: TimeSpan.FromMinutes(10));
```

## Queue Design and Workload Isolation

Separate workloads into dedicated queues and deploy independent worker pools for each tier. This prevents a spike in one workload from starving another.

```csharp
// Worker A: handles user-facing and email workloads
options.Queues = new[] { "default", "emails" };
options.Workers = 20;

// Worker B: handles slow or memory-intensive compute
options.Queues = new[] { "heavy-compute" };
options.Workers = 5;
```

### Throttle Jobs That Call External Services

Apply `[Throttle]` to any job that calls an external API or shared resource to prevent overwhelming it during high-concurrency bursts.

```csharp
[Throttle("stripe-api", maxConcurrent: 5)]
public sealed class ChargeCardJob : IJob<ChargeInput> { ... }
```

### Protect Queues from Downstream Outages with Circuit Breakers

When an external service goes down, retries can pile up and hammer it further. Enable the queue circuit breaker to pause a queue automatically and ramp traffic back up gradually when the service recovers.

See the [Circuit Breaker guide](../guides/circuit-breaker.md) for the states, selective exceptions and how to reset it.

```csharp
builder.Services.AddNexJob(options =>
{
    options.ConfigureQueue("payments", queue =>
    {
        queue.EnableCircuitBreaker(cb =>
        {
            cb.ConsecutiveFailuresThreshold = 5;
            cb.OpenDuration = TimeSpan.FromSeconds(30);
            cb.BackoffMultiplier = 2.0;
            cb.MaxOpenDuration = TimeSpan.FromMinutes(10);
            cb.RecoveryDuration = TimeSpan.FromMinutes(2);
            cb.RecoveryConcurrency = 2;  // anti-thundering-herd ramp-up

            cb.BreakOnTransientHttpErrors(includeAuthErrors: true);
            cb.BreakOn<TimeoutException>();
        });
    });
});
```

!!! tip
    `RecoveryConcurrency = 2` means only 2 jobs run concurrently during the recovery window. This gentle ramp-up prevents you from crashing a service that is still fragile right after coming back online.


### Multi-Service Architectures

In environments where multiple services share a database cluster:

- Run a dedicated dashboard container with `DisableWorkers = true` so operational monitoring does not consume worker threads or take lock slots from backend workers.
- Scope the dashboard to relevant queues with `options.Queues = ["serviceA-queue"]` so each team sees only their own jobs.
- NexJob handles foreign job types safely by rolling back attempt counts and deferring via `ForeignJobRetryDelay` rather than dead-lettering them. See the [Multi-Service guide](../guides/multi-service.md).

## Storage Selection and Connection Pool Sizing

Use persistent storage (Postgres, SQL Server, MongoDB, or Redis) in production. InMemory storage loses all jobs on restart and is suitable only for tests and local development.

### Size the Database Connection Pool

Each worker node needs roughly `Workers + 5` database connections. Set the connection pool maximum to `Workers + 10` and multiply by your node count before you scale out.

```json
"ConnectionStrings": {
  "NexJob": "Host=db;Database=nexjob;Username=app;Password=secret;Maximum Pool Size=30"
}
```

| Workers | Recommended pool max | 3-node cluster total |
|---|---|---|
| 10 | 20 | 60 |
| 20 | 30 | 90 |
| 50 | 60 | 180 |

!!! warning
    Undersizing the connection pool causes timeouts under load. Oversizing it exhausts database server resources. Start with `Workers + 10` and adjust based on observed connection wait times.


### Control Storage Growth with Retention Policies

In high-throughput environments, successful job rows accumulate quickly. Use the `[Retention]` attribute to control this.

```csharp
// Delete the row immediately on success — zero table bloat
[Retention(PurgeOnSuccess = true)]
public sealed class HighFrequencyTelemetryJob : IJob<TelemetryPayload> { ... }

// Keep the row for auditing but strip the heavy input JSON
[Retention(TrimPayloadOnSuccess = true)]
public sealed class HeavyReportGenerationJob : IJob<LargeReportInput> { ... }
```

| Strategy | Effect | Auditability |
|---|---|---|
| Default (none) | Row retained until the retention service runs | Full record and payload intact |
| `PurgeOnSuccess = true` | Row deleted immediately on success | Succeeded jobs vanish from `/jobs`; lifetime stats preserved in `/catalog` |
| `TrimPayloadOnSuccess = true` | Payload cleared, row kept | State, duration, queue, and logs preserved; payload stripped |

!!! note
    `PurgeOnSuccess = true` only applies on success. A failing job always follows your normal retry policy and dead-letter retention so operators can diagnose and requeue it.


## Graceful Shutdown

When the host stops, NexJob immediately stops fetching new jobs and waits up to `ShutdownTimeout` for running jobs to finish. Jobs still running after the timeout are cancelled via the `CancellationToken` passed to `ExecuteAsync`.

A job cancelled by shutdown is **not** treated as a failure: it is requeued immediately, its attempt counter is not incremented, and it is never sent to a dead-letter handler.

```csharp
// HostOptions.ShutdownTimeout must exceed NexJobOptions.ShutdownTimeout
builder.Services.Configure<HostOptions>(o =>
    o.ShutdownTimeout = TimeSpan.FromSeconds(40));

builder.Services.AddNexJob(o =>
    o.ShutdownTimeout = TimeSpan.FromSeconds(30));
```

!!! warning
    The .NET host defaults to a 30-second shutdown timeout (5 seconds on older project templates). If NexJob's `ShutdownTimeout` equals the host's, the host can kill the process before the drain finishes. Always set `HostOptions.ShutdownTimeout` to at least `NexJobOptions.ShutdownTimeout + 10 seconds`.


Write cancellable jobs — honour the `CancellationToken` throughout your async calls so jobs can be interrupted quickly and requeued rather than being abandoned to the orphan watcher.

## Monitoring and Alerting

Enable OpenTelemetry from day one. NexJob emits traces and metrics for every job execution and queue depth change. To be notified when a job fails for good, see the [Alerts guide](../guides/alerts.md).

Set up alerts on:

- `nexjob.jobs.failed` increasing — indicates failed attempts (including ones that will still be retried)
- `nexjob.jobs.expired` increasing — deadlines are too tight or workers are insufficient
- `nexjob.job.duration` p99 spiking — jobs are getting slower

Check the dashboard regularly for queue depth trends, failed job error patterns, and orphaned jobs (which indicate worker crashes).

## Production Checklist


### Persistent storage

Use Postgres, SQL Server, MongoDB, or Redis — not InMemory.

### Retry policies

Set `MaxAttempts` appropriate to each failure mode. Register dead-letter handlers for critical jobs.

### Throttling

Add `[Throttle]` to every job that calls an external service or shared resource.

### Queue isolation

Separate heavy, slow, or external-facing workloads into dedicated queues with independent worker pools.

### Circuit breakers

Enable `EnableCircuitBreaker` on queues that communicate with external partners.

### Idempotency

Design every job to be safe to run more than once with the same input.

### Graceful shutdown

Set `HostOptions.ShutdownTimeout` greater than `NexJobOptions.ShutdownTimeout` and honour `CancellationToken` in all jobs.

### Connection pool sizing

Set `Maximum Pool Size` to `Workers + 10` per node.

### Retention policies

Add `[Retention]` to high-frequency job classes to control storage growth.

### Deadlines

Set `deadlineAfter` on time-sensitive jobs so stale work is discarded rather than delivered late.

### Observability

Configure OpenTelemetry, enable the dashboard with authorization, and set up alerts on failure and expiry metrics.

### Health check

Register `NexJobHealthCheck` and include it in your `/healthz` endpoint.



