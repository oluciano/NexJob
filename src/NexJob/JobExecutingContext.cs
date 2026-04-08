namespace NexJob;

/// <summary>
/// Provides context about the currently executing job to <see cref="IJobExecutionFilter"/> implementations.
/// </summary>
public sealed class JobExecutingContext
{
    /// <summary>
    /// Initializes a new <see cref="JobExecutingContext"/>.
    /// </summary>
    /// <param name="job">The job record being executed.</param>
    /// <param name="services">The DI service provider scoped to this job execution.</param>
    internal JobExecutingContext(JobRecord job, IServiceProvider services)
    {
        Job = job;
        Services = services;
    }

    /// <summary>
    /// Gets the job record being executed, including its type, input, attempt count, and metadata.
    /// </summary>
    public JobRecord Job { get; }

    /// <summary>
    /// Gets the DI service provider scoped to this job execution.
    /// Use this to resolve scoped dependencies from within a filter.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Gets whether the job execution completed successfully.
    /// This is set after <see cref="IJobExecutionFilter.OnExecutingAsync"/> returns —
    /// it reflects the outcome of the pipeline invoked via the <c>next</c> delegate.
    /// </summary>
    public bool Succeeded { get; internal set; }

    /// <summary>
    /// Gets the exception thrown by the job or a downstream filter, or
    /// <see langword="null"/> when the execution succeeded.
    /// This is set after <see cref="IJobExecutionFilter.OnExecutingAsync"/> returns.
    /// </summary>
    public Exception? Exception { get; internal set; }
}
