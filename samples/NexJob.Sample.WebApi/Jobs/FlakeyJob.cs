using NexJob;

namespace NexJob.Sample.WebApi.Jobs;

/// <summary>
/// Fails on the first N attempts, then succeeds — exercises the retry pipeline.
/// </summary>
public sealed record FlakeyRequest(string Name, int FailTimes);

public sealed class FlakeyJob(ILogger<FlakeyJob> logger) : IJob<FlakeyRequest>
{
    // shared counter per job name so retries see the same state
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _attempts = new();

    public Task ExecuteAsync(FlakeyRequest input, CancellationToken cancellationToken)
    {
        var key = input.Name ?? "unnamed";
        var count = _attempts.AddOrUpdate(key, 1, (_, v) => v + 1);

        if (count <= input.FailTimes)
        {
            logger.LogWarning("[FlakeyJob] {Name} — attempt {Count}/{FailTimes} FAILING intentionally",
                key, count, input.FailTimes);
            throw new InvalidOperationException($"Simulated failure #{count} for '{key}'");
        }

        _attempts.TryRemove(key, out _);
        logger.LogInformation("[FlakeyJob] {Name} — SUCCEEDED after {FailTimes} failure(s)", input.Name, input.FailTimes);
        return Task.CompletedTask;
    }
}
