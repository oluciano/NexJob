using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NexJob.Internal;

/// <summary>
/// Default dispatcher for resolving and invoking dead-letter handlers.
/// </summary>
internal sealed class DefaultDeadLetterDispatcher : IDeadLetterDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DefaultDeadLetterDispatcher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultDeadLetterDispatcher"/> class.
    /// </summary>
    /// <param name="scopeFactory">The scope factory used to resolve dead-letter handlers.</param>
    /// <param name="logger">The logger.</param>
    public DefaultDeadLetterDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<DefaultDeadLetterDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task DispatchAsync(JobRecord job, Exception lastException, CancellationToken ct = default)
    {
        try
        {
            var jobType = JobTypeResolver.ResolveJobType(job.JobType);
            if (jobType is null)
            {
                _logger.LogDebug(
                    "Cannot resolve job type {JobType} for dead-letter handler — handler skipped",
                    job.JobType);
                return;
            }

            using var scope = _scopeFactory.CreateScope();

            var handlerType = typeof(IDeadLetterHandler<>).MakeGenericType(jobType);
            var handler = scope.ServiceProvider.GetService(handlerType);
            if (handler is null)
            {
                return;
            }

            var method = handlerType.GetMethod(nameof(IDeadLetterHandler<object>.HandleAsync))
                        ?? throw new InvalidOperationException($"Cannot find HandleAsync method on {handlerType.Name}");

            var task = (Task)method.Invoke(handler, new object[] { job, lastException, ct })!;
            await task.ConfigureAwait(false);

            _logger.LogDebug(
                "Dead-letter handler {Handler} invoked for job {JobId}",
                handlerType.Name, job.Id);
        }
        catch (Exception handlerEx)
        {
            _logger.LogError(
                handlerEx,
                "Dead-letter handler threw for job {JobId} — handler errors are swallowed",
                job.Id);
        }
    }
}
