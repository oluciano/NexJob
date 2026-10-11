---
title: "Job Execution Filters for Cross-Cutting Behavior in NexJob"
sidebarTitle: "Job Filters"
description: "Implement IJobExecutionFilter in NexJob to add cross-cutting behavior like logging, tracing, or authorization around every job execution."
---

Job execution filters let you wrap every job execution with cross-cutting logic — structured logging, distributed tracing, tenant injection, audit trails, or custom authorization — without touching your job classes. Filters form a middleware pipeline: each filter calls the `next` delegate to pass control to the next filter or, at the end of the chain, to the job itself.

## The `IJobExecutionFilter` interface

Implement `IJobExecutionFilter` to create a filter. The interface has a single method:

```csharp
public interface IJobExecutionFilter
{
    Task OnExecutingAsync(
        JobExecutingContext context,
        JobExecutionDelegate next,
        CancellationToken ct);
}
```

Call `await next(ct)` to invoke the rest of the pipeline. Everything before that call runs before the job. Everything after it runs **only if the job and the downstream filters did not throw**: when the job throws, the exception passes through `next` and out of your filter. To react to a failure, or to run code in both cases, wrap `next` in `try/catch/finally`.

## `JobExecutingContext` properties

`JobExecutingContext` gives your filter access to the job record and the current execution scope.

| Property | Type | Description |
|---|---|---|
| `Job` | `JobRecord` | The full job record: type name, serialised input, attempt count, queue, tags, and all metadata. |
| `Services` | `IServiceProvider` | The DI scope for this job execution. Resolve scoped services from here inside your filter. |
| `Succeeded` | `bool` | `false` until `next` returns; after `await next(ct)` it is `true` when the job and the filters below this one completed without throwing. |
| `Exception` | `Exception?` | The exception the job or a filter below this one threw. `await next(ct)` rethrows it, so read it in a `catch` or `finally`, where it is set before the exception reaches you. |

## Registering a filter

Register your filter as a service implementing `IJobExecutionFilter`. NexJob discovers every registered implementation and runs them in registration order.

```csharp
services.AddTransient<IJobExecutionFilter, LoggingFilter>();
services.AddTransient<IJobExecutionFilter, TracingFilter>();
```

!!! note "A filter follows the lifetime it was registered with"
    NexJob resolves the filters from the DI scope of each job. A `Transient` or `Scoped` filter is built once per job, so it can hold per-job state and take scoped services in its constructor. A `Singleton` filter is one instance shared by every job and every worker at the same time, so it must be safe to call concurrently. `context.Services` is the same scope.

!!! note
    If a filter throws, the exception propagates through the pipeline and is treated as a job failure: the normal retry and dead-letter flow applies.

!!! warning "A filter that does not call `next` skips the job"
    The job does not run, and NexJob stores it as `Succeeded`. That is the same result you get if a filter catches the job's exception and does not rethrow it. Use it deliberately.


## Example: structured logging filter

The following filter logs the start of every job and then logs success or failure.

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
        _logger.LogInformation(
            "Starting job {JobType} (attempt {Attempts})",
            context.Job.JobType,
            context.Job.Attempts);

        try
        {
            await next(ct);
            _logger.LogInformation("Job {JobType} succeeded", context.Job.JobType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Job {JobType} failed", context.Job.JobType);
            throw; // let NexJob handle the retry or the dead-letter
        }
    }
}
```

Register it once at startup:

```csharp
builder.Services.AddTransient<IJobExecutionFilter, LoggingFilter>();
builder.Services.AddNexJob();
```

## Example: tenant injection filter

Filters can resolve scoped services from `context.Services` to inject request-scoped state into the job's execution environment.

```csharp
public sealed class TenantFilter : IJobExecutionFilter
{
    public async Task OnExecutingAsync(
        JobExecutingContext context,
        JobExecutionDelegate next,
        CancellationToken ct)
    {
        // Resolve the scoped tenant context and set it from the job's tags
        var tenantContext = context.Services.GetRequiredService<ITenantContext>();
        var tenantTag = context.Job.Tags.FirstOrDefault(t => t.StartsWith("tenant:"));

        if (tenantTag is not null)
        {
            tenantContext.TenantId = tenantTag["tenant:".Length..];
        }

        await next(ct);
    }
}
```

## Example: execution timing filter

Measure and record how long each job takes, recording failures separately. The `finally` block runs whether the job succeeded or threw.

```csharp
public sealed class TimingFilter : IJobExecutionFilter
{
    private readonly IMetrics _metrics;

    public TimingFilter(IMetrics metrics) => _metrics = metrics;

    public async Task OnExecutingAsync(
        JobExecutingContext context,
        JobExecutionDelegate next,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var succeeded = false;

        try
        {
            await next(ct);
            succeeded = true;
        }
        finally
        {
            stopwatch.Stop();

            _metrics.RecordJobDuration(
                jobType: context.Job.JobType,
                elapsed: stopwatch.Elapsed,
                succeeded: succeeded);
        }
    }
}
```

## Order of execution

Filters execute in the order they are registered. Given the following registration:

```csharp
services.AddTransient<IJobExecutionFilter, LoggingFilter>();    // registered first
services.AddTransient<IJobExecutionFilter, TenantFilter>();     // registered second
services.AddTransient<IJobExecutionFilter, TimingFilter>();     // registered third
```

The execution flow for a job that succeeds is:


### LoggingFilter (before)

Logs job start.

### TenantFilter (before)

Sets tenant context.

### TimingFilter (before)

Starts stopwatch.

### Job executes

Your job's `ExecuteAsync` runs.

### TimingFilter (after)

Stops stopwatch, records duration.

### TenantFilter (after)

No teardown needed.

### LoggingFilter (after)

Logs success or failure.




## Handling exceptions in filters

To observe an exception without suppressing it, catch it around `next` and rethrow it. If you do not rethrow, the job counts as having succeeded.

```csharp
public async Task OnExecutingAsync(
    JobExecutingContext context,
    JobExecutionDelegate next,
    CancellationToken ct)
{
    try
    {
        await next(ct);
    }
    catch (Exception ex)
    {
        _alerting.NotifyFailure(context.Job.JobType, ex);
        throw; // rethrow, and NexJob handles retries and dead-letter
    }
}
```

!!! warning
    If a filter throws its own exception (rather than rethrowing the original), that exception replaces the original and is treated as the job failure. Only throw deliberately from a filter when you intend to override the job's result.


## When to use filters

<div class="grid cards" markdown>
  -   **Use filters for**

    Structured logging, distributed tracing spans, tenant or user context injection, audit trail recording, custom metrics emission.

  -   **Avoid filters for**

    Job-specific business logic, input validation, retry decisions. Those belong in the job class itself or in a `[Retry]` attribute.

</div>
