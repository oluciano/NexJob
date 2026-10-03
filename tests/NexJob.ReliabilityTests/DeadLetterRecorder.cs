using NexJob;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Per-test record of dead-letter handler invocations. Registered as a singleton in each test host
/// so parallel tests never share state.
/// </summary>
internal sealed class DeadLetterRecorder
{
    private int _invocationCount;

    public JobRecord? LastFailedJob { get; private set; }

    public Exception? LastException { get; private set; }

    public int InvocationCount => Volatile.Read(ref _invocationCount);

    public void Record(JobRecord failedJob, Exception lastException)
    {
        LastFailedJob = failedJob;
        LastException = lastException;
        Interlocked.Increment(ref _invocationCount);
    }
}
