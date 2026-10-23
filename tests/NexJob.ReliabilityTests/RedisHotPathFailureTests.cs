using Microsoft.Extensions.DependencyInjection;
using NexJob.Redis;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// The storage failing at the hot-path call sites, on a Redis database.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class RedisHotPathFailureTests
    : HotPathFailureScenarios,
      IClassFixture<RedisReliabilityFixture>
{
    private readonly RedisReliabilityFixture _fixture;

    public RedisHotPathFailureTests(RedisReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobRedis(_fixture.ConnectionString);
}
