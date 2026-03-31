using Microsoft.Extensions.Logging;
using NexJob;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Test job that always succeeds immediately.
/// </summary>
internal sealed class SuccessJob : IJob
{
    private readonly ILogger<SuccessJob> _logger;

    public SuccessJob(ILogger<SuccessJob> logger) => _logger = logger;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SuccessJob executed");
        await Task.CompletedTask;
    }
}

/// <summary>
/// Test job that always fails with InvalidOperationException.
/// </summary>
internal sealed class AlwaysFailJob : IJob
{
    private readonly ILogger<AlwaysFailJob> _logger;

    public AlwaysFailJob(ILogger<AlwaysFailJob> logger) => _logger = logger;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AlwaysFailJob executing");
        await Task.CompletedTask;
        throw new InvalidOperationException("Job intentionally failed");
    }
}

/// <summary>
/// Test job that tracks execution count.
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
/// Test job with structured input.
/// </summary>
internal sealed record DelayJobInput(int DelayMs);

internal sealed class DelayJob : IJob<DelayJobInput>
{
    private readonly ILogger<DelayJob> _logger;

    public DelayJob(ILogger<DelayJob> logger) => _logger = logger;

    public async Task ExecuteAsync(DelayJobInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("DelayJob executing with {DelayMs}ms delay", input.DelayMs);
        await Task.Delay(input.DelayMs, cancellationToken);
        _logger.LogInformation("DelayJob completed");
    }
}

/// <summary>
/// Test job that fails on first attempt, succeeds on second.
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
/// Test job that respects cancellation gracefully.
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
/// Test job that logs diagnostics.
/// </summary>
internal sealed record DiagnosticJobInput(string Message);

internal sealed class DiagnosticJob : IJob<DiagnosticJobInput>
{
    private readonly ILogger<DiagnosticJob> _logger;

    public DiagnosticJob(ILogger<DiagnosticJob> logger) => _logger = logger;

    public async Task ExecuteAsync(DiagnosticJobInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("DiagnosticJob executing with message: {Message}", input.Message);
        _logger.LogWarning("This is a warning log");
        _logger.LogError("This is an error log");
        await Task.CompletedTask;
    }
}
