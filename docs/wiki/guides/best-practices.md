---
title: "NexJob Production Best Practices and Performance Tips"
sidebarTitle: "Best Practices"
description: "Production guidelines for NexJob: graceful shutdown, idempotency, queue isolation, storage selection, retry tuning, and connection pool sizing."
---

Running NexJob reliably in production requires a small set of deliberate decisions at design time: keeping jobs focused and idempotent, choosing retry policies that match your failure modes, isolating workloads by queue, and configuring graceful shutdown so worker restarts never lose work. This page collects those guidelines in one place.

## Job design

### Keep jobs small and focused

Each job should do one thing. If your job fetches data, transforms it, calls three APIs, and sends two emails, split it into multiple jobs connected with continuations.

```csharp
// Too broad — hard to retry, hard to test
public sealed class ProcessOrderJob : IJob<OrderInput> { ... }

// Focused — each step can retry independently
public sealed class ValidateOrderJob   : IJob<OrderInput>       { ... }
public sealed class ChargePaymentJob   : IJob<ChargeInput>      { ... }
public sealed class SendConfirmationJob : IJob<ConfirmationInput> { ... }
```

### Make jobs idempotent

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

### Use minimal input

Pass only what the job needs. Avoid embedding full entity objects in job payloads — pass IDs and fetch inside the job instead.

```csharp
// Stores the entire Order object in the job queue — fragile and bloated
public sealed record ProcessOrderInput(Order FullOrder);

// Stores only the ID — lean and forward-compatible
public sealed record ProcessOrderInput(Guid OrderId);
```

## Retry configuration

Match your retry policy to the kind of failure you expect. A policy suited for a transient network blip is wrong for a validation error that will never self-heal.

| Failure type | Max retries | Delay strategy | Rationale |
|---|---|---|---|
| Transient network error | 3 – 5 | Exponential, 30 s – 5 min | Usually resolves quickly |
| External API rate limit | 5 – 10 | Exponential, 1 min – 1 hr | May need to wait for a window reset |
| Database deadlock | 2 – 3 | Short, 1 – 5 s | Resolves on the next transaction |
| Validation error | 0 | N/A | Retrying bad input never helps. Use `[Retry(0)]`, or list the exception in [`IgnoreRetryAttemptExceptions`](../concepts/retries.md#failures-that-should-not-be-retried) |

### Set deadlines for time-sensitive jobs

If a job is useless after a certain point — a promotional email, a real-time notification — set a `deadlineAfter` so NexJob discards it rather than delivering it late.

```csharp
await scheduler.EnqueueAsync<SendPromoEmailJob>(
    deadlineAfter: TimeSpan.FromMinutes(10));
```

## Queue design and workload isolation

Separate workloads into dedicated queues and deploy independent worker pools for each tier. This prevents a spike in one workload from starving another.

```csharp
// Worker A: handles user-facing and email workloads
options.Queues = new[] { "default", "emails" };
options.Workers = 20;

// Worker B: handles slow or memory-intensive compute
options.Queues = new[] { "heavy-compute" };
options.Workers = 5;
```

### Throttle jobs that call external services

Apply `[Throttle]` to any job that calls an external API or shared resource to prevent overwhelming it during high-concurrency bursts.

```csharp
[Throttle("stripe-api", maxConcurrent: 5)]
public sealed class ChargeCardJob : IJob<ChargeInput> { ... }
```

### Protect queues from downstream outages with circuit breakers

When an external service goes down, retries can pile up and hammer it further. Enable the queue circuit breaker to pause a queue automatically and ramp traffic back up gradually when the service recovers.

See the [Circuit Breaker guide](../guides/circuit-breaker.md) for the states, the defaults, which failures count and how to reset it. A recovery ramp (`RecoveryConcurrency`, 2 by default) keeps a service that is still fragile from being hit with the full load as soon as it comes back.

### Multi-service architectures

In environments where multiple services share a database cluster:

- Set `QueuePrefix` explicitly in each service so its default queue (`{prefix}.default`) survives renames and refactors; the dashboard of a standalone host needs the same value. See [the default queue and prefix](../concepts/default-queue.md).
- Run a dedicated dashboard container with `DisableWorkers = true` so it never takes worker slots from the services.
- Scope the dashboard to relevant queues with `options.Queues = ["serviceA-queue"]` so each team sees only their own jobs.
- NexJob handles foreign job types safely by rolling back attempt counts and deferring via `ForeignJobRetryDelay` rather than dead-lettering them. See the [Multi-Service guide](../guides/multi-service.md).

## Storage selection and connection pool sizing

Use persistent storage (Postgres, SQL Server, MongoDB, or Redis) in production. InMemory storage loses all jobs on restart and is suitable only for tests and local development.

### Size the database connection pool

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


### Control storage growth with retention policies

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
| Default (none) | Row retained until the retention service removes it: 7 days for `Succeeded`, 30 days for `Failed` and 7 days for `Expired` by default (`RetentionSucceeded`, `RetentionFailed`, `RetentionExpired`) | Full record and payload intact |
| `PurgeOnSuccess = true` | Row deleted immediately on success | Succeeded jobs vanish from `/jobs`; lifetime stats preserved in `/catalog` |
| `TrimPayloadOnSuccess = true` | Payload cleared, row kept | State, duration, queue, and logs preserved; payload stripped |

!!! note
    `PurgeOnSuccess = true` only applies on success. A failing job always follows your normal retry policy and dead-letter retention so operators can diagnose and requeue it.


## Graceful shutdown

When the host stops, NexJob immediately stops fetching new jobs and waits up to `ShutdownTimeout` for running jobs to finish. Jobs still running after the timeout are cancelled via the `CancellationToken` passed to `ExecuteAsync`.

A job cancelled by shutdown is **not** treated as a failure: NexJob requeues it without incrementing its attempt counter (see the warning below), and it is never sent to a dead-letter handler.

```csharp
// HostOptions.ShutdownTimeout must exceed NexJobOptions.ShutdownTimeout
builder.Services.Configure<HostOptions>(o =>
    o.ShutdownTimeout = TimeSpan.FromSeconds(40));

builder.Services.AddNexJob(o =>
    o.ShutdownTimeout = TimeSpan.FromSeconds(30));
```

!!! warning "A job that outlives the drain may not be requeued before the process exits"
    The requeue of a job that is cancelled when the drain ends is committed **after** `StopAsync` has returned. If the process exits right away, which is what a deploy does, that commit can be lost: on PostgreSQL the job stayed `Processing` with its attempt used in 3 of 3 runs, and only the orphan watcher recovered it, after `HeartbeatTimeout`. A job that finishes within the drain is not affected. Give `ShutdownTimeout` room for your longest job, or split long jobs and save [checkpoints](../guides/job-context.md#progress-checkpoints-for-long-running-jobs). Tracked in [#476](https://github.com/oluciano/NexJob/issues/476).

!!! warning
    The .NET host defaults to a 30-second shutdown timeout (5 seconds on older project templates). If NexJob's `ShutdownTimeout` equals the host's, the host can kill the process before the drain finishes. Always set `HostOptions.ShutdownTimeout` to at least `NexJobOptions.ShutdownTimeout + 10 seconds`.


Write cancellable jobs — honour the `CancellationToken` throughout your async calls so jobs can be interrupted quickly and requeued rather than being abandoned to the orphan watcher.

## Monitoring and alerting

Enable OpenTelemetry from day one. NexJob emits traces and metrics for every job execution and queue depth change. To be notified when a job fails for good, see the [Alerts guide](../guides/alerts.md).

Set up alerts on:

- `nexjob.jobs.failed` increasing — indicates failed attempts (including ones that will still be retried)
- `nexjob.jobs.expired` increasing — deadlines are too tight or workers are insufficient
- `nexjob.job.duration` p99 spiking — jobs are getting slower

Check the dashboard regularly for queue depth trends, failed job error patterns, and orphaned jobs (which indicate worker crashes).

## Production checklist


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

Add it with `services.AddHealthChecks().AddNexJob()` and include it in your `/healthz` endpoint. It reports whether storage is reachable.

## Roslyn diagnostic analyzers

NexJob ships with built-in compile-time Roslyn Diagnostic Analyzers bundled directly in the core `NexJob` package (`analyzers/dotnet/cs/NexJob.Analyzers.dll`). Installing `NexJob` automatically activates real-time guardrails in your IDE (Visual Studio, JetBrains Rider, VS Code) to detect common job-authoring pitfalls (blocking calls, unawaited tasks, swallowed exceptions, and missing cancellation tokens) as you type.

For the complete list of rules, code examples, severity levels, and `.editorconfig` configuration instructions, see the dedicated [Roslyn Diagnostic Analyzers Guide](analyzers.md).



