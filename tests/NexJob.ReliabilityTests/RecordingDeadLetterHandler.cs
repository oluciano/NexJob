using Microsoft.Extensions.Logging;
using NexJob;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Test dead-letter handler that records invocations.
/// </summary>
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

/// <summary>
/// Test dead-letter handler that tracks async operations.
/// </summary>
internal sealed class AsyncDeadLetterHandler<TJob> : IDeadLetterHandler<TJob>
    where TJob : notnull
{
    private readonly ILogger<AsyncDeadLetterHandler<TJob>> _logger;

    public static TaskCompletionSource<bool> HandlerCompleted { get; } = new();

    public AsyncDeadLetterHandler(ILogger<AsyncDeadLetterHandler<TJob>> logger)
        => _logger = logger;

    public async Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Async handler starting for job {JobId}", failedJob.Id);
        await Task.Delay(100, cancellationToken); // Simulate async work
        _logger.LogInformation("Async handler completed for job {JobId}", failedJob.Id);
        HandlerCompleted.TrySetResult(true);
    }
}
