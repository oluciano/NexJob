using Microsoft.Extensions.Logging;
using NexJob;

namespace NexJob.ReliabilityTests.Distributed;

/// <summary>
/// Test job that always succeeds immediately (IJob variant).
/// </summary>
internal sealed class SuccessJob : IJob
{
    public static int ExecutionCount { get; set; }

    private readonly ILogger<SuccessJob> _logger;

    public SuccessJob(ILogger<SuccessJob> logger) => _logger = logger;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        ExecutionCount++;
        _logger.LogInformation("SuccessJob executed (count: {Count})", ExecutionCount);
        await Task.CompletedTask;
    }
}

/// <summary>
/// Test job that always fails with InvalidOperationException (IJob variant).
/// </summary>
internal sealed class AlwaysFailJob : IJob
{
    public static int ExecutionCount { get; set; }

    private readonly ILogger<AlwaysFailJob> _logger;

    public AlwaysFailJob(ILogger<AlwaysFailJob> logger) => _logger = logger;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        ExecutionCount++;
        _logger.LogInformation("AlwaysFailJob executing (count: {Count})", ExecutionCount);
        await Task.CompletedTask;
        throw new InvalidOperationException("Job intentionally failed");
    }
}

/// <summary>
/// Test job that tracks execution count (IJob variant).
/// </summary>
internal sealed class TrackingJob : IJob
{
    public static int ExecutionCount { get; set; }

    private readonly ILogger<TrackingJob> _logger;

    public TrackingJob(ILogger<TrackingJob> logger) => _logger = logger;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        ExecutionCount++;
        _logger.LogInformation("TrackingJob executed (count: {Count})", ExecutionCount);
        await Task.CompletedTask;
    }
}

/// <summary>
/// Test job with delay that can be cancelled (IJob variant).
/// </summary>
internal sealed class DelayJob : IJob
{
    public static int ExecutionCount { get; set; }

    private readonly ILogger<DelayJob> _logger;

    public DelayJob(ILogger<DelayJob> logger) => _logger = logger;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        ExecutionCount++;
        _logger.LogInformation("DelayJob executing (count: {Count})", ExecutionCount);
        await Task.Delay(2000, cancellationToken);
        _logger.LogInformation("DelayJob completed");
    }
}

/// <summary>
/// Test job that fails on first attempt, succeeds on second (IJob variant).
/// </summary>
internal sealed class FailOnceThenSucceedJob : IJob
{
    public static int ExecutionCount { get; set; }

    private readonly ILogger<FailOnceThenSucceedJob> _logger;

    public FailOnceThenSucceedJob(ILogger<FailOnceThenSucceedJob> logger) => _logger = logger;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        ExecutionCount++;
        _logger.LogInformation("FailOnceThenSucceedJob executing (attempt {Attempt})", ExecutionCount);

        if (ExecutionCount == 1)
        {
            await Task.CompletedTask;
            throw new InvalidOperationException("First attempt fails intentionally");
        }

        await Task.CompletedTask;
    }
}

/// <summary>
/// Test job that respects cancellation gracefully (IJob variant).
/// </summary>
internal sealed class CancellableJob : IJob
{
    public static int CancellationCount { get; set; }

    private readonly ILogger<CancellableJob> _logger;

    public CancellableJob(ILogger<CancellableJob> logger) => _logger = logger;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("CancellableJob started");
            await Task.Delay(5000, cancellationToken); // Long delay to be cancellable
            _logger.LogInformation("CancellableJob completed");
        }
        catch (OperationCanceledException)
        {
            CancellationCount++;
            _logger.LogInformation("CancellableJob cancelled (count: {Count})", CancellationCount);
            throw;
        }
    }
}

/// <summary>
/// Test job that logs diagnostics (IJob variant).
/// </summary>
internal sealed class DiagnosticJob : IJob
{
    public static int ExecutionCount { get; set; }

    private readonly ILogger<DiagnosticJob> _logger;

    public DiagnosticJob(ILogger<DiagnosticJob> logger) => _logger = logger;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        ExecutionCount++;
        _logger.LogInformation("DiagnosticJob executing (count: {Count})", ExecutionCount);
        _logger.LogWarning("This is a warning log");
        _logger.LogError("This is an error log");
        await Task.CompletedTask;
    }
}
