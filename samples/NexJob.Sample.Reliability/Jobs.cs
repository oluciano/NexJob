using System.Collections.Concurrent;
using NexJob;

namespace NexJob.Sample.Reliability;

/// <summary>What the demo jobs did, in order. Exposed by <c>GET /log</c>.</summary>
public sealed class SampleLog
{
    private readonly ConcurrentQueue<string> _entries = new();

    /// <summary>Adds an entry.</summary>
    /// <param name="entry">What happened.</param>
    public void Add(string entry) => _entries.Enqueue(entry);

    /// <summary>Gets a snapshot of the entries.</summary>
    /// <returns>The entries, oldest first.</returns>
    public string[] Snapshot() => _entries.ToArray();
}

/// <summary>Fails its first two attempts and succeeds on the third. <c>[Retry]</c> sets the attempts and the delay between them.</summary>
[Retry(4, InitialDelay = "00:00:01")]
public sealed class FlakyJob : IJob
{
    private readonly IJobContext _context;
    private readonly SampleLog _log;

    /// <summary>Initializes a new instance of the <see cref="FlakyJob"/> class.</summary>
    /// <param name="context">The runtime context of the job.</param>
    /// <param name="log">The demo log.</param>
    public FlakyJob(IJobContext context, SampleLog log)
    {
        _context = context;
        _log = log;
    }

    /// <inheritdoc/>
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _log.Add($"flaky:{_context.JobId.Value}:attempt-{_context.Attempt}");
        if (_context.Attempt < 3)
        {
            throw new InvalidOperationException($"Flaky failure on attempt {_context.Attempt}");
        }

        return Task.CompletedTask;
    }
}

/// <summary>How far <see cref="CheckpointJob"/> got.</summary>
/// <param name="Done">The number of steps already finished.</param>
public sealed record CheckpointState(int Done);

/// <summary>
/// Five steps; after each one the progress is saved. The first attempt "crashes" after step 3, and the retry resumes
/// from the checkpoint: steps 1 to 3 are not repeated.
/// </summary>
[Retry(3, InitialDelay = "00:00:01")]
public sealed class CheckpointJob : IJob
{
    private readonly IJobContext _context;
    private readonly SampleLog _log;

    /// <summary>Initializes a new instance of the <see cref="CheckpointJob"/> class.</summary>
    /// <param name="context">The runtime context of the job.</param>
    /// <param name="log">The demo log.</param>
    public CheckpointJob(IJobContext context, SampleLog log)
    {
        _context = context;
        _log = log;
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var done = _context.GetCheckpoint<CheckpointState>()?.Done ?? 0;
        for (var step = done + 1; step <= 5; step++)
        {
            _log.Add($"checkpoint:{_context.JobId.Value}:step-{step}");
            await _context.SaveCheckpointAsync(new CheckpointState(step), step * 20, $"step {step} of 5", cancellationToken);
            if (step == 3 && _context.Attempt == 1)
            {
                throw new InvalidOperationException("Simulated crash after step 3");
            }
        }
    }
}

/// <summary>Always fails. With one attempt it dead-letters at once; on the <c>fragile</c> queue it trips the circuit breaker.</summary>
[Retry(1)]
public sealed class AlwaysFailJob : IJob
{
    /// <inheritdoc/>
    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("This job always fails");
}

/// <summary>Always fails after two attempts, and has a dead-letter handler.</summary>
[Retry(2, InitialDelay = "00:00:01")]
public sealed class DeadLetterDemoJob : IJob
{
    /// <inheritdoc/>
    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Dead-letter demo failure");
}

/// <summary>Records that it ran.</summary>
public sealed class QuickJob : IJob
{
    private readonly IJobContext _context;
    private readonly SampleLog _log;

    /// <summary>Initializes a new instance of the <see cref="QuickJob"/> class.</summary>
    /// <param name="context">The runtime context of the job.</param>
    /// <param name="log">The demo log.</param>
    public QuickJob(IJobContext context, SampleLog log)
    {
        _context = context;
        _log = log;
    }

    /// <inheritdoc/>
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _log.Add($"quick:{_context.JobId.Value}");
        return Task.CompletedTask;
    }
}

/// <summary>Runs when <see cref="DeadLetterDemoJob"/> has used all its attempts.</summary>
public sealed class DeadLetterDemoHandler : IDeadLetterHandler<DeadLetterDemoJob>
{
    private readonly SampleLog _log;

    /// <summary>Initializes a new instance of the <see cref="DeadLetterDemoHandler"/> class.</summary>
    /// <param name="log">The demo log.</param>
    public DeadLetterDemoHandler(SampleLog log) => _log = log;

    /// <inheritdoc/>
    public Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken)
    {
        _log.Add($"deadletter:{failedJob.Id.Value}:{lastException.Message}");
        return Task.CompletedTask;
    }
}
