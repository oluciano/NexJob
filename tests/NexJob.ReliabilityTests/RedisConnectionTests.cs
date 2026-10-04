using Microsoft.Extensions.DependencyInjection;
using NexJob.Redis;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Connections seen by Redis while NexJob works.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class RedisConnectionTests
    : ConnectionCountScenarios,
      IClassFixture<RedisReliabilityFixture>
{
    private readonly RedisReliabilityFixture _fixture;
    private readonly Lazy<ConnectionMultiplexer> _admin;

    public RedisConnectionTests(RedisReliabilityFixture fixture, ITestOutputHelper output)
        : base(output)
    {
        _fixture = fixture;
        _admin = new Lazy<ConnectionMultiplexer>(() => ConnectionMultiplexer.Connect($"{fixture.ConnectionString},allowAdmin=true"));
    }

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobRedis(_fixture.ConnectionString);

    /// <inheritdoc/>
    protected override async Task<int> CountServerConnectionsAsync()
    {
        var server = _admin.Value.GetServer(_admin.Value.GetEndPoints()[0]);
        return (await server.ClientListAsync()).Length;
    }
}
