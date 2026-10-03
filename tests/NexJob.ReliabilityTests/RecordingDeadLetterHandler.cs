using Microsoft.Extensions.Logging;
using NexJob;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Test dead-letter handler that records invocations into the host's <see cref="DeadLetterRecorder"/>.
/// </summary>
/// <typeparam name="TJob">The job type.</typeparam>
internal sealed class RecordingDeadLetterHandler<TJob> : IDeadLetterHandler<TJob>
    where TJob : notnull
{
    private readonly ILogger<RecordingDeadLetterHandler<TJob>> _logger;
    private readonly DeadLetterRecorder _recorder;

    public RecordingDeadLetterHandler(ILogger<RecordingDeadLetterHandler<TJob>> logger, DeadLetterRecorder recorder)
    {
        _logger = logger;
        _recorder = recorder;
    }

    public Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Dead-letter handler invoked for job {JobId} of type {JobType} after {Attempts} attempts",
            failedJob.Id,
            typeof(TJob).Name,
            failedJob.Attempts);

        _recorder.Record(failedJob, lastException);

        return Task.CompletedTask;
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
