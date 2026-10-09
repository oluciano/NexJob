using Microsoft.Extensions.DependencyInjection;
using NexJob.Redis;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>The default queue transition from v5 to v6 on Redis.</summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class RedisQueuePrefixUpgradeTests
    : QueuePrefixUpgradeScenarios,
      IClassFixture<RedisReliabilityFixture>
{
    private readonly RedisReliabilityFixture _fixture;

    public RedisQueuePrefixUpgradeTests(RedisReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobRedis(_fixture.ConnectionString);
}
