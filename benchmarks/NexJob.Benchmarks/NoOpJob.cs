using NexJob;

namespace NexJob.Benchmarks;

/// <summary>
/// NexJob job that completes instantly. Used to measure pure scheduling and dispatch overhead.
/// </summary>
public sealed class NoOpJob : IJob<NoOpInput>
{
    private static Action? _onExecuted;

    /// <summary>Sets the callback invoked each time the job executes.</summary>
    public static void SetCompletionCallback(Action? callback) => _onExecuted = callback;

    /// <inheritdoc/>
    public Task ExecuteAsync(NoOpInput input, CancellationToken cancellationToken)
    {
        _onExecuted?.Invoke();
        return Task.CompletedTask;
    }
}
