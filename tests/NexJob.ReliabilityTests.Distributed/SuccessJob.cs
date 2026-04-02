using Microsoft.Extensions.Logging;
using NexJob;

namespace NexJob.ReliabilityTests.Distributed;

/// <summary>
/// Test job that always succeeds immediately (IJob variant).
/// </summary>
internal sealed class SuccessJob : IJob
{
    private readonly Action _onExecuted;
    private readonly ILogger<SuccessJob> _logger;

    public SuccessJob(Action onExecuted, ILogger<SuccessJob> logger)
    {
        _onExecuted = onExecuted;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SuccessJob executed");
        _onExecuted();
        await Task.CompletedTask;
    }
}

/// <summary>
/// Test job that always fails with InvalidOperationException (IJob variant).
/// </summary>
internal sealed class AlwaysFailJob : IJob
{
    private readonly Action _onExecuted;
    private readonly ILogger<AlwaysFailJob> _logger;

    public AlwaysFailJob(Action onExecuted, ILogger<AlwaysFailJob> logger)
    {
        _onExecuted = onExecuted;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AlwaysFailJob executing");
        _onExecuted();
        await Task.CompletedTask;
        throw new InvalidOperationException("Job intentionally failed");
    }
}

/// <summary>
/// Test job that tracks execution count (IJob variant).
/// </summary>
internal sealed class TrackingJob : IJob
{
    private readonly Action _onExecuted;
    private readonly ILogger<TrackingJob> _logger;

    public TrackingJob(Action onExecuted, ILogger<TrackingJob> logger)
    {
        _onExecuted = onExecuted;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TrackingJob executed");
        _onExecuted();
        await Task.CompletedTask;
    }
}

/// <summary>
/// Test job with delay that can be cancelled (IJob variant).
/// </summary>
internal sealed class DelayJob : IJob
{
    private readonly Action _onExecuted;
    private readonly ILogger<DelayJob> _logger;

    public DelayJob(Action onExecuted, ILogger<DelayJob> logger)
    {
        _onExecuted = onExecuted;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DelayJob executing");
        await Task.Delay(2000, cancellationToken);
        _logger.LogInformation("DelayJob completed");
        _onExecuted();
    }
}

/// <summary>
/// Test job that fails on first attempt, succeeds on second (IJob variant).
/// </summary>
internal sealed class FailOnceThenSucceedJob : IJob
{
    private readonly Action _onExecuted;
    private readonly ILogger<FailOnceThenSucceedJob> _logger;

    private int _attempt = 0;

    public FailOnceThenSucceedJob(Action onExecuted, ILogger<FailOnceThenSucceedJob> logger)
    {
        _onExecuted = onExecuted;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _attempt++;
        _logger.LogInformation("FailOnceThenSucceedJob executing (attempt {Attempt})", _attempt);

        if (_attempt == 1)
        {
            _onExecuted();
            throw new InvalidOperationException("First attempt fails intentionally");
        }

        await Task.CompletedTask;
        _onExecuted();
    }
}

/// <summary>
/// Test job that respects cancellation gracefully (IJob variant).
/// </summary>
internal sealed class CancellableJob : IJob
{
    private readonly Action _onExecuted;
    private readonly ILogger<CancellableJob> _logger;

    public CancellableJob(Action onExecuted, ILogger<CancellableJob> logger)
    {
        _onExecuted = onExecuted;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("CancellableJob started");
            await Task.Delay(5000, cancellationToken);
            _logger.LogInformation("CancellableJob completed");
            _onExecuted();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("CancellableJob cancelled");
            throw;
        }
    }
}

/// <summary>
/// Test job that logs diagnostics (IJob variant).
/// </summary>
internal sealed class DiagnosticJob : IJob
{
    private readonly Action _onExecuted;
    private readonly ILogger<DiagnosticJob> _logger;

    public DiagnosticJob(Action onExecuted, ILogger<DiagnosticJob> logger)
    {
        _onExecuted = onExecuted;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DiagnosticJob executing");
        _logger.LogWarning("This is a warning log");
        _logger.LogError("This is an error log");
        _onExecuted();
        await Task.CompletedTask;
    }
}
