namespace NexJob;

/// <summary>
/// Defines a middleware component that wraps job execution.
/// Implement this interface and register it in the DI container to add cross-cutting
/// behaviour — logging, tenant injection, audit trails, metrics, circuit breakers.
/// </summary>
/// <remarks>
/// <para>
/// Filters are executed in the order they are registered in the DI container.
/// Each filter receives a <see cref="JobExecutionDelegate"/> that invokes the next
/// filter in the pipeline, or the job itself when no more filters remain.
/// </para>
/// <para>
/// Filters are resolved from the job's DI scope, so scoped services are available.
/// </para>
/// <para>
/// If a filter throws, the exception propagates through the pipeline and is treated
/// as a job failure — the normal retry and dead-letter flow applies.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Logging filter
/// public class LoggingFilter : IJobExecutionFilter
/// {
///     private readonly ILogger&lt;LoggingFilter&gt; _logger;
///     public LoggingFilter(ILogger&lt;LoggingFilter&gt; logger) => _logger = logger;
///
///     public async Task OnExecutingAsync(
///         JobExecutingContext context,
///         JobExecutionDelegate next,
///         CancellationToken ct)
///     {
///         _logger.LogInformation("Starting job {JobType}", context.Job.JobType);
///         await next(ct);
///         if (context.Succeeded)
///             _logger.LogInformation("Job {JobType} succeeded", context.Job.JobType);
///         else
///             _logger.LogWarning("Job {JobType} failed: {Error}", context.Job.JobType, context.Exception?.Message);
///     }
/// }
///
/// // Registration
/// services.AddSingleton&lt;IJobExecutionFilter, LoggingFilter&gt;();
/// </code>
/// </example>
public interface IJobExecutionFilter
{
    /// <summary>
    /// Called when a job is about to execute.
    /// Invoke <paramref name="next"/> to pass control to the next filter or the job itself.
    /// </summary>
    /// <param name="context">Context for the current job execution.</param>
    /// <param name="next">Delegate that invokes the next component in the pipeline.</param>
    /// <param name="ct">Cancellation token.</param>
    Task OnExecutingAsync(JobExecutingContext context, JobExecutionDelegate next, CancellationToken ct);
}
