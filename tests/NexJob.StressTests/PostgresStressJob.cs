using System.Threading;
using System.Threading.Tasks;
using NexJob;

namespace NexJob.StressTests;

public sealed class PostgresStressJob : IJob<StressJobInput>
{
    private static long _executionCount;

    public static long ExecutionCount => Interlocked.Read(ref _executionCount);

    public static void Reset() => Interlocked.Exchange(ref _executionCount, 0);

    public Task ExecuteAsync(StressJobInput input, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _executionCount);
        return Task.CompletedTask;
    }
}
