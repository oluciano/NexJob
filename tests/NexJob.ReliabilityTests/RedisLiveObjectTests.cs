using Microsoft.Extensions.DependencyInjection;
using NexJob.Redis;
using StackExchange.Redis;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// <c>AddNexJobRedis(IConnectionMultiplexer)</c> against a real Redis. The container has no password, so authentication is not
/// part of this one.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class RedisLiveObjectTests
    : LiveObjectOverloadScenarios,
      IClassFixture<RedisReliabilityFixture>
{
    private readonly RedisReliabilityFixture _fixture;

    public RedisLiveObjectTests(RedisReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override (Action<IServiceCollection> Register, IAsyncDisposable Owned) CreateOwnedObject()
    {
        var multiplexer = ConnectionMultiplexer.Connect(_fixture.ConnectionString);
        return (s => s.AddNexJobRedis(multiplexer), multiplexer);
    }

    /// <inheritdoc/>
    protected override async Task AssertStillUsableAsync(IAsyncDisposable owned)
    {
        var multiplexer = (ConnectionMultiplexer)owned;
        Assert.True(multiplexer.IsConnected);
        await multiplexer.GetDatabase().PingAsync();
    }
}
