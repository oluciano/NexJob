---
title: "NexJob Job Types: IJob and IJob<T>"
sidebarTitle: "Job types"
description: "Learn how to define background jobs in NexJob using IJob for self-contained parameterless jobs and IJob<T> for jobs that accept structured typed input."
---

Every background task in NexJob is a class that implements one of two interfaces: `IJob` for self-contained work that needs no external data, and `IJob<TInput>` for work that requires a typed payload decided at enqueue time. NexJob resolves each job from its own dependency injection scope and disposes that scope when execution finishes, so scoped services like Entity Framework Core `DbContext` are always safe to inject directly.

## `IJob`: no input

Implement `IJob` when the job already knows what to do from its injected dependencies alone.

```csharp
public interface IJob
{
    Task ExecuteAsync(CancellationToken cancellationToken);
}
```

A typical self-contained job looks like this:

```csharp
public sealed class CleanupOldLogsJob : IJob
{
    private readonly IDbContext _db;

    public CleanupOldLogsJob(IDbContext db) => _db = db;

    public async Task ExecuteAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        await _db.Logs.Where(l => l.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
    }
}

// Enqueue
await scheduler.EnqueueAsync<CleanupOldLogsJob>(cancellationToken: ct);
```

**When to use `IJob`:** The job can query for its own work using its dependencies. Good examples are log cleanup, health checks, and periodic syncs where the job determines the scope of work itself.

## `IJob<TInput>`: structured input

Implement `IJob<TInput>` when the job needs specific data that is known at the time of enqueue.

```csharp
public interface IJob<in TInput>
{
    Task ExecuteAsync(TInput input, CancellationToken cancellationToken);
}
```

Define a record for your input type and implement the interface:

```csharp
public sealed record ProcessOrderInput(Guid OrderId);

public sealed class ProcessOrderJob : IJob<ProcessOrderInput>
{
    private readonly IOrderProcessor _processor;

    public ProcessOrderJob(IOrderProcessor processor) => _processor = processor;

    public async Task ExecuteAsync(ProcessOrderInput input, CancellationToken ct)
    {
        await _processor.ProcessAsync(input.OrderId, ct);
    }
}

// Enqueue with input
await scheduler.EnqueueAsync<ProcessOrderJob, ProcessOrderInput>(
    new ProcessOrderInput(orderId),
    cancellationToken: ct);
```

**When to use `IJob<TInput>`:** The job operates on a specific entity decided at call time. Good examples are processing a specific order, sending an email to a specific user, or delivering a webhook payload.

### Input serialization

The input is stored as JSON, so the type must be serializable with `System.Text.Json`. Prefer a `record`, and keep it to the identifiers and values the job needs. [Job payloads](job-payloads.md) has the rules and explains how to change an input type while jobs are queued.

## Dependency injection and scoped services

NexJob creates an isolated `IServiceScope` for every job execution. The job class is resolved from that scope, all its constructor-injected dependencies belong to the same scope, and the scope is disposed when execution completes — whether the job succeeded or failed. You never need to create a scope manually.

This means you can inject Entity Framework Core `DbContext` instances directly:

```csharp
public sealed class ProcessOrderJob : IJob<ProcessOrderInput>
{
    private readonly AppDbContext _db;
    private readonly ILogger<ProcessOrderJob> _logger;

    public ProcessOrderJob(AppDbContext db, ILogger<ProcessOrderJob> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ExecuteAsync(ProcessOrderInput input, CancellationToken ct)
    {
        // _db is fully isolated to this single execution.
        // Concurrent workers never share this DbContext instance.
        var order = await _db.Orders.FindAsync([input.OrderId], ct);
        if (order is not null)
        {
            order.Status = "Processed";
            await _db.SaveChangesAsync(ct);
        }
    }
}
```

!!! tip
    You do **not** need to call `serviceProvider.CreateScope()` yourself. NexJob guarantees scope isolation for every job execution out of the box.


The scope lives from the moment the job is resolved until `ExecuteAsync` returns, and is disposed whether the job succeeded or failed. The [execution pipeline](../mental-model.md#execution-pipeline) shows where this step sits among the others.

## Auto-registering job classes

Instead of registering every job manually, call `AddNexJobJobs` to scan an assembly and register all `IJob` and `IJob<TInput>` implementations automatically:

```csharp
// Registers CleanupOldLogsJob, ProcessOrderJob, SendEmailJob, etc.
builder.Services.AddNexJobJobs(typeof(Program).Assembly);
```

Every non-abstract class that implements `IJob` or `IJob<TInput>` is registered as a transient service, including `internal` classes. Registration uses `TryAddTransient`, so any job you already registered explicitly is left unchanged.

## Related

- **Changing an input type:** [Job payloads](job-payloads.md) covers serialization and `[SchemaVersion]` with `IJobMigration`.
- **A job that keeps failing:** [Retries](retries.md) covers the retry policy and [dead-letter handling](dead-letter.md) covers `IDeadLetterHandler<TJob>`.
- **The job's own context:** [`IJobContext`](../guides/job-context.md) gives a job its attempt number, progress reporting and checkpoints, so a retried job can resume where it stopped.
- **Behavior around every job:** [Job filters](../guides/job-filters.md) wrap each execution with logging, tenant injection or timing.
- **Enqueueing and timing:** [Scheduling](scheduling.md) covers `EnqueueAsync`, delays, priority and deadlines.
