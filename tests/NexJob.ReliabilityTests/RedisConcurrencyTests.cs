using Microsoft.Extensions.DependencyInjection;
using NexJob.Redis;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Concurrency scenarios on Redis.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class RedisConcurrencyTests
    : ConcurrencyScenarios,
      IClassFixture<RedisReliabilityFixture>
{
    private readonly RedisReliabilityFixture _fixture;

    public RedisConcurrencyTests(RedisReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobRedis(_fixture.ConnectionString);
}
