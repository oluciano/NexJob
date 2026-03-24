using NexJob;

namespace NexJob.Sample.WebApi.Jobs;

/// <summary>Sleeps for a configurable duration — saturates the worker pool.</summary>
public sealed record SlowRequest(string Name, int DurationMs);

public sealed class SlowJob(ILogger<SlowJob> logger) : IJob<SlowRequest>
{
    public async Task ExecuteAsync(SlowRequest input, CancellationToken cancellationToken)
    {
        logger.LogInformation("[SlowJob] {Name} starting ({DurationMs}ms)", input.Name, input.DurationMs);
        await Task.Delay(input.DurationMs, cancellationToken);
        logger.LogInformation("[SlowJob] {Name} done", input.Name);
    }
}
