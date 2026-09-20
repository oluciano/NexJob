namespace NexJob.Benchmarks;

/// <summary>Hangfire no-op job stub for throughput benchmarks.</summary>
public static class HangfireNoOpJob
{
    private static Action? _onExecuted;

    /// <summary>Sets the callback invoked after each job execution.</summary>
    public static void SetCompletionCallback(Action? callback) => _onExecuted = callback;

    /// <summary>No-op execute method enqueued by Hangfire.</summary>
    public static void Execute() => _onExecuted?.Invoke();

    /// <summary>No-op execute method with payload enqueued by Hangfire.</summary>
    /// <param name="payload">Payload string passed to benchmark serialization.</param>
    public static void Execute(string payload) => _onExecuted?.Invoke();
}
