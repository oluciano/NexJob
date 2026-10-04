using Microsoft.Extensions.DependencyInjection;
using NexJob.Redis;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Job control service scenarios on Redis.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class RedisControlServiceTests
    : ControlServiceScenarios,
      IClassFixture<RedisReliabilityFixture>
{
    private readonly RedisReliabilityFixture _fixture;

    public RedisControlServiceTests(RedisReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobRedis(_fixture.ConnectionString);
}
