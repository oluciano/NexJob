using Microsoft.Extensions.DependencyInjection;
using NexJob.Redis;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Restart scenarios on Redis.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class RedisRecoveryTests
    : RecoveryScenarios,
      IClassFixture<RedisReliabilityFixture>
{
    private readonly RedisReliabilityFixture _fixture;

    public RedisRecoveryTests(RedisReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobRedis(_fixture.ConnectionString);
}
