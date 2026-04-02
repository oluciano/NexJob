using Microsoft.Extensions.Logging;
using NexJob;

namespace NexJob.ReliabilityTests.Distributed;

/// <summary>
/// Test dead-letter handler that records invocations.
/// </summary>
/// <typeparam name="TJob">The job type.</typeparam>
internal sealed class RecordingDeadLetterHandler<TJob> : IDeadLetterHandler<TJob>
    where TJob : notnull
{
    private readonly ILogger<RecordingDeadLetterHandler<TJob>> _logger;

    public static JobRecord? LastFailedJob { get; set; }

    public static Exception? LastException { get; set; }

    public static int InvocationCount { get; set; }

    public RecordingDeadLetterHandler(ILogger<RecordingDeadLetterHandler<TJob>> logger)
        => _logger = logger;

    public Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Dead-letter handler invoked for job {JobId} of type {JobType} after {Attempts} attempts",
            failedJob.Id,
            typeof(TJob).Name,
            failedJob.Attempts);

        LastFailedJob = failedJob;
        LastException = lastException;
        InvocationCount++;

        return Task.CompletedTask;
    }

    public static void Reset()
    {
        LastFailedJob = null;
        LastException = null;
        InvocationCount = 0;
    }
}

/// <summary>
/// Test dead-letter handler that throws to verify exception handling.
/// </summary>
/// <typeparam name="TJob">The job type.</typeparam>
internal sealed class ThrowingDeadLetterHandler<TJob> : IDeadLetterHandler<TJob>
    where TJob : notnull
{
    private readonly ILogger<ThrowingDeadLetterHandler<TJob>> _logger;

    public ThrowingDeadLetterHandler(ILogger<ThrowingDeadLetterHandler<TJob>> logger)
        => _logger = logger;

    public Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken)
    {
        _logger.LogError("Handler throwing intentionally");
        throw new InvalidOperationException("Handler intentionally throwing");
    }
}
