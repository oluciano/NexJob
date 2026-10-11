using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NexJob.Redis;
using NexJob.Storage;
using StackExchange.Redis;
using Xunit;

namespace NexJob.Tests;

public sealed class NexJobRedisExtensionsTests
{
    // N1 (Positive): a standalone Redis, and a primary with replicas, register as before.
    [Theory]
    [InlineData(ServerType.Standalone)]
    [InlineData(ServerType.Sentinel)]
    public void AddNexJobRedis_WithANonClusterMultiplexer_RegistersTheStorage(ServerType serverType)
    {
        var services = new ServiceCollection();

        services.AddNexJobRedis(Multiplexer(serverType).Object);

        services.Should().Contain(d => d.ServiceType == typeof(IStorageProvider));
    }

    // N2 (Negative): Redis Cluster is not supported, so the registration fails before any job is touched.
    [Fact]
    public void AddNexJobRedis_WithAClusterMultiplexer_ThrowsAndSaysWhatToUse()
    {
        var services = new ServiceCollection();

        var act = () => services.AddNexJobRedis(Multiplexer(ServerType.Standalone, ServerType.Cluster).Object);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("Redis Cluster").And.Contain("standalone");
        services.Should().NotContain(d => d.ServiceType == typeof(IStorageProvider));
    }

    // N3 (Invalid input): no multiplexer is an argument error, and a multiplexer that lists no server is not a cluster.
    [Fact]
    public void AddNexJobRedis_WithANullMultiplexer_ThrowsArgumentNull()
    {
        var act = () => new ServiceCollection().AddNexJobRedis((IConnectionMultiplexer)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddNexJobRedis_WithAMultiplexerThatListsNoServer_Registers()
    {
        var services = new ServiceCollection();

        services.AddNexJobRedis(Multiplexer().Object);

        services.Should().Contain(d => d.ServiceType == typeof(IStorageProvider));
    }

    private static Mock<IConnectionMultiplexer> Multiplexer(params ServerType[] serverTypes)
    {
        var servers = serverTypes.Select(t =>
        {
            var server = new Mock<IServer>();
            server.SetupGet(s => s.ServerType).Returns(t);
            return server.Object;
        }).ToArray();
        var mux = new Mock<IConnectionMultiplexer>();
        mux.Setup(m => m.GetServers()).Returns(servers);
        mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(new Mock<IDatabase>().Object);
        return mux;
    }
}
