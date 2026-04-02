using Microsoft.Extensions.Logging;
using NexJob;

namespace NexJob.ReliabilityTests.Distributed;

// ─────────────────────────────────────────────────────────────────────────────
// IJob<T> stubs (with input) – equivalents for testing structured job inputs
// ─────────────────────────────────────────────────────────────────────────────

internal sealed record SuccessJobInput(string Label);

/// <summary>
/// Test job that always succeeds immediately (IJob.<T> variant).
/// </summary>
internal sealed class SuccessJobWithInput : IJob<SuccessJobInput>
{
    public static int ExecutionCount { get; set; }

    private readonly ILogger<SuccessJobWithInput> _logger;

    public SuccessJobWithInput(ILogger<SuccessJobWithInput> logger) => _logger = logger;

    public async Task ExecuteAsync(SuccessJobInput input, CancellationToken cancellationToken)
    {
        ExecutionCount++;
        _logger.LogInformation("SuccessJobWithInput executed with label: {Label} (count: {Count})", input.Label, ExecutionCount);
        await Task.CompletedTask;
    }
}

internal sealed record AlwaysFailJobInput(string Reason);

/// <summary>
/// Test job that always fails with InvalidOperationException (IJob.<T> variant).
/// </summary>
internal sealed class AlwaysFailJobWithInput : IJob<AlwaysFailJobInput>
{
    public static int ExecutionCount { get; set; }

    private readonly ILogger<AlwaysFailJobWithInput> _logger;

    public AlwaysFailJobWithInput(ILogger<AlwaysFailJobWithInput> logger) => _logger = logger;

    public async Task ExecuteAsync(AlwaysFailJobInput input, CancellationToken cancellationToken)
    {
        ExecutionCount++;
        _logger.LogInformation("AlwaysFailJobWithInput executing: {Reason} (count: {Count})", input.Reason, ExecutionCount);
        await Task.CompletedTask;
        throw new InvalidOperationException($"Intentional failure: {input.Reason}");
    }
}

internal sealed record TrackingJobInput(Guid RunId);

/// <summary>
/// Test job that tracks execution count (IJob.<T> variant).
/// </summary>
internal sealed class TrackingJobWithInput : IJob<TrackingJobInput>
{
    public static int ExecutionCount { get; set; }

    private readonly ILogger<TrackingJobWithInput> _logger;

    public TrackingJobWithInput(ILogger<TrackingJobWithInput> logger) => _logger = logger;

    public async Task ExecuteAsync(TrackingJobInput input, CancellationToken cancellationToken)
    {
        ExecutionCount++;
        _logger.LogInformation("TrackingJobWithInput executed (RunId: {RunId}, count: {Count})", input.RunId, ExecutionCount);
        await Task.CompletedTask;
    }
}

internal sealed record DelayJobInputWithInput(int DelayMs);

/// <summary>
/// Test job with delay that can be cancelled (IJob.<T> variant).
/// </summary>
internal sealed class DelayJobWithInput : IJob<DelayJobInputWithInput>
{
    public static int ExecutionCount { get; set; }

    private readonly ILogger<DelayJobWithInput> _logger;

    public DelayJobWithInput(ILogger<DelayJobWithInput> logger) => _logger = logger;

    public async Task ExecuteAsync(DelayJobInputWithInput input, CancellationToken cancellationToken)
    {
        ExecutionCount++;
        _logger.LogInformation("DelayJobWithInput executing with {DelayMs}ms delay (count: {Count})", input.DelayMs, ExecutionCount);
        await Task.Delay(input.DelayMs, cancellationToken);
        _logger.LogInformation("DelayJobWithInput completed");
    }
}

internal sealed record FailOnceThenSucceedJobInput(string Context);

/// <summary>
/// Test job that fails on first attempt, succeeds on second (IJob.<T> variant).
/// </summary>
internal sealed class FailOnceThenSucceedJobWithInput : IJob<FailOnceThenSucceedJobInput>
{
    public static int ExecutionCount { get; set; }

    private readonly ILogger<FailOnceThenSucceedJobWithInput> _logger;

    public FailOnceThenSucceedJobWithInput(ILogger<FailOnceThenSucceedJobWithInput> logger) => _logger = logger;

    public async Task ExecuteAsync(FailOnceThenSucceedJobInput input, CancellationToken cancellationToken)
    {
        ExecutionCount++;
        _logger.LogInformation("FailOnceThenSucceedJobWithInput executing (context: {Context}, attempt: {Attempt})", input.Context, ExecutionCount);

        if (ExecutionCount == 1)
        {
            await Task.CompletedTask;
            throw new InvalidOperationException("First attempt fails intentionally");
        }

        await Task.CompletedTask;
    }
}

/// <summary>
/// Test job that respects cancellation gracefully (IJob.<T> variant).
/// </summary>
internal sealed class CancellableJobWithInput : IJob<TrackingJobInput>
{
    public static int CancellationCount { get; set; }

    private readonly ILogger<CancellableJobWithInput> _logger;

    public CancellableJobWithInput(ILogger<CancellableJobWithInput> logger) => _logger = logger;

    public async Task ExecuteAsync(TrackingJobInput input, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("CancellableJobWithInput started (RunId: {RunId})", input.RunId);
            await Task.Delay(5000, cancellationToken); // Long delay to be cancellable
            _logger.LogInformation("CancellableJobWithInput completed");
        }
        catch (OperationCanceledException)
        {
            CancellationCount++;
            _logger.LogInformation("CancellableJobWithInput cancelled (count: {Count})", CancellationCount);
            throw;
        }
    }
}

internal sealed record DiagnosticJobInput(string Message);

/// <summary>
/// Test job that logs diagnostics (IJob.<T> variant).
/// </summary>
internal sealed class DiagnosticJobWithInput : IJob<DiagnosticJobInput>
{
    public static int ExecutionCount { get; set; }

    private readonly ILogger<DiagnosticJobWithInput> _logger;

    public DiagnosticJobWithInput(ILogger<DiagnosticJobWithInput> logger) => _logger = logger;

    public async Task ExecuteAsync(DiagnosticJobInput input, CancellationToken cancellationToken)
    {
        ExecutionCount++;
        _logger.LogInformation("DiagnosticJobWithInput executing with message: {Message} (count: {Count})", input.Message, ExecutionCount);
        _logger.LogWarning("This is a warning log");
        _logger.LogError("This is an error log");
        await Task.CompletedTask;
    }
}
