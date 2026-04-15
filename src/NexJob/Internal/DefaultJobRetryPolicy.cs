using System.Reflection;

namespace NexJob.Internal;

/// <summary>
/// Default retry policy that evaluates retry attributes and configured retry delays.
/// </summary>
internal sealed class DefaultJobRetryPolicy : IJobRetryPolicy
{
    private readonly NexJobOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultJobRetryPolicy"/> class.
    /// </summary>
    /// <param name="options">The NexJob options.</param>
    public DefaultJobRetryPolicy(NexJobOptions options)
    {
        _options = options;
    }

    /// <inheritdoc/>
    public DateTimeOffset? ComputeRetryAt(JobRecord job, Exception exception)
    {
        _ = exception;

        var retryAttr = job.JobType is not null
            ? Type.GetType(job.JobType)?.GetCustomAttribute<RetryAttribute>(inherit: true)
            : null;
        var effectiveMaxAttempts = retryAttr?.Attempts ?? job.MaxAttempts;

        if (job.Attempts < effectiveMaxAttempts)
        {
            var retryDelay = retryAttr?.InitialDelay is not null
                ? retryAttr.ComputeDelay(job.Attempts)
                : _options.RetryDelayFactory(job.Attempts);

            return DateTimeOffset.UtcNow + retryDelay;
        }

        return null;
    }
}
