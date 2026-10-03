namespace NexJob.ReliabilityTests;

/// <summary>
/// Thread-safe count of job executions, shared by every job instance of one test host.
/// </summary>
internal sealed class ExecutionCounter
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Increment() => Interlocked.Increment(ref _count);
}
