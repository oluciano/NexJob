namespace NexJob.Kafka.IntegrationTests;

/// <summary>
/// Sample job for Kafka consumer integration testing.
/// </summary>
public sealed class TestJob : IJob
{
    /// <inheritdoc/>
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
