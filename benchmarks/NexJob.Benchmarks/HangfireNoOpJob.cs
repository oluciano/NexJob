namespace NexJob.Benchmarks;

/// <summary>Hangfire no-op job stub for throughput benchmarks.</summary>
public static class HangfireNoOpJob
{
    /// <summary>Increments the completion counter and signals when all jobs have finished.</summary>
    public static void Execute(ref int completed, TaskCompletionSource<bool> tcs, int target)
    {
        if (Interlocked.Increment(ref completed) >= target)
        {
            tcs.TrySetResult(true);
        }
    }
}
