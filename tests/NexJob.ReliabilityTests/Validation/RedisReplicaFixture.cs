using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Testcontainers.Redis;
using Xunit;

namespace NexJob.ReliabilityTests.Validation;

/// <summary>
/// A Redis primary with one replica, on a private network, for the validation rounds.
/// </summary>
public sealed class RedisReplicaFixture : IAsyncLifetime
{
    private const string PrimaryAlias = "redis-primary";

    private readonly INetwork _network = new NetworkBuilder().Build();
    private RedisContainer? _primary;
    private RedisContainer? _replica;

    /// <summary>Gets the host:port of the primary, as the test process reaches it.</summary>
    public string PrimaryEndpoint => Endpoint(_primary!);

    /// <summary>Gets the host:port of the replica, as the test process reaches it.</summary>
    public string ReplicaEndpoint => Endpoint(_replica!);

    /// <summary>Stops the primary, as in a crash.</summary>
    /// <returns>A task that completes when the primary has stopped.</returns>
    public Task StopPrimaryAsync() => _primary!.StopAsync();

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        await _network.CreateAsync();
        _primary = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .WithNetwork(_network)
            .WithNetworkAliases(PrimaryAlias)
            .Build();
        await _primary.StartAsync();

        _replica = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .WithNetwork(_network)
            .WithCommand("redis-server", "--replicaof", PrimaryAlias, "6379")
            .Build();
        await _replica.StartAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        if (_replica is not null)
        {
            await _replica.DisposeAsync();
        }

        if (_primary is not null)
        {
            await _primary.DisposeAsync();
        }

        await _network.DisposeAsync();
    }

    private static string Endpoint(IContainer container) => $"{container.Hostname}:{container.GetMappedPublicPort(6379)}";
}
