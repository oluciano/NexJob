using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace NexJob.Internal;

/// <summary>
/// Default retry policy that evaluates retry attributes and configured retry delays.
/// </summary>
internal sealed class DefaultJobRetryPolicy : IJobRetryPolicy
{
    private readonly NexJobOptions _options;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultJobRetryPolicy"/> class.
    /// </summary>
    /// <param name="options">The NexJob options.</param>
    /// <param name="logger">Optional logger.</param>
    public DefaultJobRetryPolicy(NexJobOptions options, ILogger<DefaultJobRetryPolicy>? logger = null)
    {
        _options = options;
        _logger = logger ?? NullLogger<DefaultJobRetryPolicy>.Instance;
    }

    /// <inheritdoc/>
    public DateTimeOffset? ComputeRetryAt(JobRecord job, Exception exception)
    {
        var retryAttr = job.JobType is not null
            ? Type.GetType(job.JobType)?.GetCustomAttribute<RetryAttribute>(inherit: true)
            : null;

        if (IsNotRetriable(exception, retryAttr))
        {
            _logger.LogInformation(
                "Job {JobId} failed with {ExceptionType}, which must not be retried. Skipping the remaining attempts and moving it to dead-letter.",
                job.Id,
                exception.GetType().Name);
            return null;
        }

        // A stored limit that differs from the global default is an explicit per-job choice and wins. A stored value
        // equal to the default may be the default itself (legacy, recurring, dashboard or trigger created jobs), so
        // the class attribute applies; an explicit value equal to the default cannot be told apart from it.
        var effectiveMaxAttempts = job.MaxAttempts != _options.MaxAttempts
            ? job.MaxAttempts
            : retryAttr?.Attempts ?? job.MaxAttempts;

        if (job.Attempts < effectiveMaxAttempts)
        {
            var retryDelay = retryAttr?.InitialDelay is not null
                ? retryAttr.ComputeDelay(job.Attempts)
                : _options.RetryDelayFactory(job.Attempts);

            return DateTimeOffset.UtcNow + retryDelay;
        }

        return null;
    }

    private static Exception? Unwrap(Exception exception) => exception switch
    {
        TargetInvocationException { InnerException: { } inner } => inner,
        AggregateException aggregate when aggregate.Flatten().InnerExceptions is { Count: 1 } single => single[0],
        _ => null,
    };

    private static bool Matches(IEnumerable<Type>? listed, Type thrown) =>
        listed is not null && listed.Any(type => type.IsAssignableFrom(thrown));

    private bool IsNotRetriable(Exception exception, RetryAttribute? retryAttr)
    {
        var global = _options.IgnoreRetryAttemptExceptions;
        var perJob = retryAttr?.IgnoreRetryAttemptExceptions;
        if (global.Count == 0 && perJob is not { Length: > 0 })
        {
            return false;
        }

        // The listed type may be the thrown one or the single cause wrapped by reflection or a task.
        for (var current = exception; current is not null; current = Unwrap(current))
        {
            var thrown = current.GetType();
            if (Matches(global, thrown) || Matches(perJob, thrown))
            {
                return true;
            }
        }

        return false;
    }
}
