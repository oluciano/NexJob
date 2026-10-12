using Microsoft.Extensions.DependencyInjection;
using NexJob.Redis;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// A failing result commit on a Redis database.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class RedisCommitFailureTests
    : CommitFailureScenarios,
      IClassFixture<RedisReliabilityFixture>
{
    private readonly RedisReliabilityFixture _fixture;

    public RedisCommitFailureTests(RedisReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobRedis(_fixture.ConnectionString);
}
