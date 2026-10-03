using Microsoft.Extensions.DependencyInjection;
using NexJob.Redis;
using Xunit;

namespace NexJob.ReliabilityTests.Distributed;

/// <summary>
/// Retry and dead-letter scenarios on Redis.
/// </summary>
public sealed class RedisRetryAndDeadLetterTests
    : RetryAndDeadLetterScenarios,
      IClassFixture<RedisReliabilityFixture>
{
    private readonly RedisReliabilityFixture _fixture;

    public RedisRetryAndDeadLetterTests(RedisReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobRedis(_fixture.ConnectionString);
}
