---
title: "NexJob Job Types: IJob, IJob<T>, and Dead-Letter Handling"
sidebarTitle: "Job Types"
description: "Learn how to define background jobs in NexJob using IJob for self-contained parameterless jobs and IJob<T> for jobs that accept structured typed input."
---

Every background task in NexJob is a class that implements one of two interfaces: `IJob` for self-contained work that needs no external data, and `IJob<TInput>` for work that requires a typed payload decided at enqueue time. NexJob resolves each job from its own dependency injection scope and disposes that scope when execution finishes, so scoped services like Entity Framework Core `DbContext` are always safe to inject directly.

## IJob — No Input

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

## IJob&lt;TInput&gt; — Structured Input

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

### Input Serialization Rules

NexJob serializes your input to JSON and stores it alongside the job record. Keep the following rules in mind:

!!! note
    - Input types must be JSON-serializable using `System.Text.Json`.
      - Prefer `record` types — they are immutable, concise, and serialize predictably.
      - Keep input minimal: store only the identifiers and values the job needs, not entire entity graphs.
      - Avoid types that cannot survive a round-trip (e.g. delegates, streams, or non-serializable structs).


## Dependency Injection and Scoped Services

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


The execution lifecycle for a single job looks like this:


### Dispatcher picks up the job

The dispatcher dequeues the job record from storage and begins execution.

### A new DI scope is created

NexJob calls `IServiceProvider.CreateScope()` to create an isolated scope specifically for this execution.

### Job is resolved from the scope

The job class (e.g. `ProcessOrderJob`) and all its dependencies are resolved from the new scope.

### ExecuteAsync is called

NexJob calls `ExecuteAsync`, passing the deserialized input and a `CancellationToken` that is triggered on host shutdown.

### Scope is disposed

Whether the job succeeds or fails, the scope is disposed. Database connections are closed and memory is released.




## Dead-Letter Handlers

When a job exhausts all its retry attempts, NexJob marks it as `Failed` and invokes its dead-letter handler if one is registered. Registering a handler is optional — jobs without one are simply marked as `Failed` and visible in the dashboard.

```csharp
public sealed class PaymentDeadLetterHandler : IDeadLetterHandler<ProcessPaymentJob>
{
    private readonly IAlertService _alerts;

    public PaymentDeadLetterHandler(IAlertService alerts) => _alerts = alerts;

    public async Task HandleAsync(
        JobRecord failedJob,
        Exception lastException,
        CancellationToken cancellationToken)
    {
        await _alerts.SendAsync(
            $"Payment job {failedJob.Id} failed after {failedJob.Attempts} attempts: {lastException.Message}",
            cancellationToken);
    }
}

// Register in Program.cs
builder.Services.AddTransient<IDeadLetterHandler<ProcessPaymentJob>, PaymentDeadLetterHandler>();
```

Key behaviors to know:

- Use the **job type** as the generic parameter, not the input type.
- Works for both `IJob` and `IJob<TInput>` implementations.
- Handlers run in an isolated DI scope — exceptions inside the handler are logged and swallowed so they never crash the dispatcher.
- The `JobRecord` parameter gives you full execution context: `Id`, `Attempts`, `MaxAttempts`, `LastErrorMessage`, `LastErrorStackTrace`, `InputJson`, `Queue`, `Tags`, `CreatedAt`, and `CompletedAt`.

## Job Execution Filters

`IJobExecutionFilter` lets you wrap every job execution with cross-cutting behavior — logging, tenant injection, tracing, or circuit breakers — without modifying individual job classes. Implement the interface and register it in the DI container:

```csharp
public sealed class LoggingFilter : IJobExecutionFilter
{
    private readonly ILogger<LoggingFilter> _logger;

    public LoggingFilter(ILogger<LoggingFilter> logger) => _logger = logger;

    public async Task OnExecutingAsync(
        JobExecutingContext context,
        JobExecutionDelegate next,
        CancellationToken ct)
    {
        _logger.LogInformation("Starting job {JobType}", context.Job.JobType);
        await next(ct);
    }
}

// Register — filters run in registration order
builder.Services.AddTransient<IJobExecutionFilter, LoggingFilter>();
```

See [Job Filters](../guides/job-filters.md) for the full filter pipeline documentation, including ordering, error handling, and per-execution scoped services.

## Auto-Registering Job Classes

Instead of registering every job manually, call `AddNexJobJobs` to scan an assembly and register all `IJob` and `IJob<TInput>` implementations automatically:

```csharp
// Registers CleanupOldLogsJob, ProcessOrderJob, SendEmailJob, etc.
builder.Services.AddNexJobJobs(typeof(Program).Assembly);
```

Every non-abstract class that implements `IJob` or `IJob<TInput>` is registered as a transient service, including `internal` classes. Registration uses `TryAddTransient`, so any job you already registered explicitly is left unchanged.
