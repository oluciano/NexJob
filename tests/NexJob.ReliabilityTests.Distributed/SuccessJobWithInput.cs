using Microsoft.Extensions.Logging;
using NexJob;

namespace NexJob.ReliabilityTests.Distributed;

/// <summary>
/// Test job with input that always succeeds immediately (IJob.<T> variant).
/// </summary>
internal sealed class SuccessJobWithInput : IJob<SuccessInput>
{
    private readonly Action _onExecuted;
    private readonly ILogger<SuccessJobWithInput> _logger;

    public SuccessJobWithInput(Action onExecuted, ILogger<SuccessJobWithInput> logger)
    {
        _onExecuted = onExecuted;
        _logger = logger;
    }

    public async Task ExecuteAsync(SuccessInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("SuccessJobWithInput executed");
        _onExecuted();
        await Task.CompletedTask;
    }
}

/// <summary>
/// Test job with input that always fails with InvalidOperationException (IJob.<T> variant).
/// </summary>
internal sealed class AlwaysFailJobWithInput : IJob<AlwaysFailInput>
{
    private readonly Action _onExecuted;
    private readonly ILogger<AlwaysFailJobWithInput> _logger;

    public AlwaysFailJobWithInput(Action onExecuted, ILogger<AlwaysFailJobWithInput> logger)
    {
        _onExecuted = onExecuted;
        _logger = logger;
    }

    public async Task ExecuteAsync(AlwaysFailInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("AlwaysFailJobWithInput executing");
        _onExecuted();
        await Task.CompletedTask;
        throw new InvalidOperationException("Job intentionally failed");
    }
}

/// <summary>
/// Test job with input that tracks execution count (IJob.<T> variant).
/// </summary>
internal sealed class TrackingJobWithInput : IJob<TrackingInput>
{
    private readonly Action _onExecuted;
    private readonly ILogger<TrackingJobWithInput> _logger;

    public TrackingJobWithInput(Action onExecuted, ILogger<TrackingJobWithInput> logger)
    {
        _onExecuted = onExecuted;
        _logger = logger;
    }

    public async Task ExecuteAsync(TrackingInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("TrackingJobWithInput executed");
        _onExecuted();
        await Task.CompletedTask;
    }
}

/// <summary>
/// Test job with input and delay that can be cancelled (IJob.<T> variant).
/// </summary>
internal sealed class DelayJobWithInput : IJob<DelayInput>
{
    private readonly Action _onExecuted;
    private readonly ILogger<DelayJobWithInput> _logger;

    public DelayJobWithInput(Action onExecuted, ILogger<DelayJobWithInput> logger)
    {
        _onExecuted = onExecuted;
        _logger = logger;
    }

    public async Task ExecuteAsync(DelayInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("DelayJobWithInput executing");
        await Task.Delay(2000, cancellationToken);
        _logger.LogInformation("DelayJobWithInput completed");
        _onExecuted();
    }
}

/// <summary>
/// Test job with input that fails on first attempt, succeeds on second (IJob.<T> variant).
/// </summary>
internal sealed class FailOnceThenSucceedJobWithInput : IJob<FailOnceThenSucceedInput>
{
    private readonly Action _onExecuted;
    private readonly ILogger<FailOnceThenSucceedJobWithInput> _logger;

    private int _attempt = 0;

    public FailOnceThenSucceedJobWithInput(Action onExecuted, ILogger<FailOnceThenSucceedJobWithInput> logger)
    {
        _onExecuted = onExecuted;
        _logger = logger;
    }

    public async Task ExecuteAsync(FailOnceThenSucceedInput input, CancellationToken cancellationToken)
    {
        _attempt++;
        _logger.LogInformation("FailOnceThenSucceedJobWithInput executing (attempt {Attempt})", _attempt);

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
/// Test job with input that respects cancellation gracefully (IJob.<T> variant).
/// </summary>
internal sealed class CancellableJobWithInput : IJob<CancellableInput>
{
    private readonly Action _onExecuted;
    private readonly ILogger<CancellableJobWithInput> _logger;

    public CancellableJobWithInput(Action onExecuted, ILogger<CancellableJobWithInput> logger)
    {
        _onExecuted = onExecuted;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellableInput input, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("CancellableJobWithInput started");
            await Task.Delay(5000, cancellationToken);
            _logger.LogInformation("CancellableJobWithInput completed");
            _onExecuted();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("CancellableJobWithInput cancelled");
            throw;
        }
    }
}

/// <summary>
/// Test job with input that logs diagnostics (IJob.<T> variant).
/// </summary>
internal sealed class DiagnosticJobWithInput : IJob<DiagnosticInput>
{
    private readonly Action _onExecuted;
    private readonly ILogger<DiagnosticJobWithInput> _logger;

    public DiagnosticJobWithInput(Action onExecuted, ILogger<DiagnosticJobWithInput> logger)
    {
        _onExecuted = onExecuted;
        _logger = logger;
    }

    public async Task ExecuteAsync(DiagnosticInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("DiagnosticJobWithInput executing");
        _logger.LogWarning("This is a warning log");
        _logger.LogError("This is an error log");
        _onExecuted();
        await Task.CompletedTask;
    }
}

// Input records for test jobs
internal sealed record SuccessInput(string Label);
internal sealed record AlwaysFailInput(string Reason);
internal sealed record TrackingInput(Guid RunId);
internal sealed record DelayInput(int DelayMs);
internal sealed record FailOnceThenSucceedInput(string Context);
internal sealed record CancellableInput();
internal sealed record DiagnosticInput(string Message);
