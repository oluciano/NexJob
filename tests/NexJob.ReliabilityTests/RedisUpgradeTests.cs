using Microsoft.Extensions.DependencyInjection;
using NexJob.Redis;
using StackExchange.Redis;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Upgrade scenarios on Redis: job hashes written by the previous version have no <c>expiresAt</c> field.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class RedisUpgradeTests
    : UpgradeScenarios,
      IClassFixture<RedisReliabilityFixture>
{
    private readonly RedisReliabilityFixture _fixture;

    public RedisUpgradeTests(RedisReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobRedis(_fixture.ConnectionString);

    /// <inheritdoc/>
    protected override async Task MoveBackToPreviousVersionAsync()
    {
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(_fixture.ConnectionString);
        var database = multiplexer.GetDatabase();
        foreach (var endpoint in multiplexer.GetEndPoints())
        {
            await foreach (var key in multiplexer.GetServer(endpoint).KeysAsync(pattern: "nexjob:jobs:*"))
            {
                await database.HashDeleteAsync(key, "expiresAt");
            }
        }
    }

    /// <inheritdoc/>
    protected override Task AssertUpgradedAsync() => Task.CompletedTask; // no schema: the field is added when a job is written
}
