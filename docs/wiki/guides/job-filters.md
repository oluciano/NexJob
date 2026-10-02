---
title: "Job Execution Filters for Cross-Cutting Behavior in NexJob"
sidebarTitle: "Job Filters"
description: "Implement IJobExecutionFilter in NexJob to add cross-cutting behavior like logging, tracing, or authorization around every job execution."
---

Job execution filters let you wrap every job execution with cross-cutting logic — structured logging, distributed tracing, tenant injection, audit trails, or custom authorization — without touching your job classes. Filters form a middleware pipeline: each filter calls the `next` delegate to pass control to the next filter or, at the end of the chain, to the job itself.

## The `IJobExecutionFilter` Interface

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

Call `await next(ct)` to invoke the rest of the pipeline. Everything before that call runs before the job; everything after runs once the job (and any downstream filters) have completed or failed.

## `JobExecutingContext` Properties

`JobExecutingContext` gives your filter access to the job record and the current execution scope.

| Property | Type | Description |
|---|---|---|
| `Job` | `JobRecord` | The full job record: type name, serialised input, attempt count, queue, tags, and all metadata. |
| `Services` | `IServiceProvider` | The DI scope for this job execution. Resolve scoped services from here inside your filter. |
| `Succeeded` | `bool` | `true` when the job completed without throwing. Available **after** you call `await next(ct)`. |
| `Exception` | `Exception?` | The exception thrown by the job or a downstream filter, or `null` on success. Available **after** you call `await next(ct)`. |

## Registering a Filter

Register your filter as a transient or scoped service implementing `IJobExecutionFilter`. NexJob discovers every registered implementation and runs them in registration order.

```csharp
services.AddTransient<IJobExecutionFilter, LoggingFilter>();
services.AddTransient<IJobExecutionFilter, TracingFilter>();
```

!!! note
    Filters are resolved from the job's DI scope, so scoped services are available inside them. If a filter throws, the exception propagates through the pipeline and is treated as a job failure — the normal retry and dead-letter flow applies.


## Example: Structured Logging Filter

The following filter logs the start of every job and then logs success or failure once the job completes.

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

        await next(ct);

        if (context.Succeeded)
        {
            _logger.LogInformation("Job {JobType} succeeded", context.Job.JobType);
        }
        else
        {
            _logger.LogWarning(
                "Job {JobType} failed: {Error}",
                context.Job.JobType,
                context.Exception?.Message);
        }
    }
}
```

Register it once at startup:

```csharp
builder.Services.AddTransient<IJobExecutionFilter, LoggingFilter>();
builder.Services.AddNexJob();
```

## Example: Tenant Injection Filter

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

## Example: Execution Timing Filter

Measure and record how long each job takes, including recording failures separately.

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

        await next(ct);

        stopwatch.Stop();

        _metrics.RecordJobDuration(
            jobType: context.Job.JobType,
            elapsed: stopwatch.Elapsed,
            succeeded: context.Succeeded);
    }
}
```

## Order of Execution

Filters execute in the order they are registered. Given the following registration:

```csharp
services.AddTransient<IJobExecutionFilter, LoggingFilter>();    // registered first
services.AddTransient<IJobExecutionFilter, TenantFilter>();     // registered second
services.AddTransient<IJobExecutionFilter, TimingFilter>();     // registered third
```

The execution flow is:


  #### LoggingFilter (before)

Logs job start.

  #### TenantFilter (before)

Sets tenant context.

  #### TimingFilter (before)

Starts stopwatch.

  #### Job executes

Your job's `ExecuteAsync` runs.

  #### TimingFilter (after)

Stops stopwatch, records duration.

  #### TenantFilter (after)

No teardown needed.

  #### LoggingFilter (after)

Logs success or failure.



## Handling Exceptions in Filters

If you want to observe an exception without suppressing it, inspect `context.Exception` after calling `await next(ct)` — but do not catch inside the filter unless you intend to swallow the error.

```csharp
public async Task OnExecutingAsync(
    JobExecutingContext context,
    JobExecutionDelegate next,
    CancellationToken ct)
{
    await next(ct);   // exception propagates to here if the job throws

    if (context.Exception is not null)
    {
        // Observe and re-throw or record, then let NexJob handle retries
        _alerting.NotifyFailure(context.Job.JobType, context.Exception);
    }
}
```

!!! warning
    If a filter throws its own exception (rather than re-throwing the original), that exception replaces the original and is treated as the job failure. Only throw deliberately from a filter when you intend to override the job's result.


## When to Use Filters

<div class="grid cards" markdown>
  -   **Use filters for**

    Structured logging, distributed tracing spans, tenant or user context injection, audit trail recording, custom metrics emission.

  -   **Avoid filters for**

    Job-specific business logic, input validation, retry decisions. Those belong in the job class itself or in a `[Retry]` attribute.

</div>
