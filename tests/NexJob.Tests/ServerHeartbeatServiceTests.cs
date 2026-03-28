using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NexJob;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests.Internal;

public sealed class ServerHeartbeatServiceTests
{
    private static ServerHeartbeatService MakeService(
        InMemoryStorageProvider storage,
        NexJobOptions options) =>
        new ServerHeartbeatService(
            storage,
            Options.Create(options),
            NullLogger<ServerHeartbeatService>.Instance);

    [Fact]
    public async Task StartAsync_RegistersServer()
    {
        var storage = new InMemoryStorageProvider();
        var options = new NexJobOptions { ServerId = "TestServer1" };
        var svc = MakeService(storage, options);

        await svc.StartAsync(CancellationToken.None);

        var activeServers = await storage.GetActiveServersAsync(TimeSpan.FromMinutes(1));
        activeServers.Should().ContainSingle(s => s.Id == "TestServer1");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    [Fact]
    public async Task Timer_TriggersHeartbeat()
    {
        var storage = new InMemoryStorageProvider();
        var options = new NexJobOptions
        {
            ServerId = "TestServer1",
            ServerHeartbeatInterval = TimeSpan.FromMilliseconds(50),
        };

        var svc = MakeService(storage, options);

        await svc.StartAsync(CancellationToken.None);

        var servers = await storage.GetActiveServersAsync(TimeSpan.FromMinutes(1));
        var initialHeartbeat = servers[0].HeartbeatAt;

        // Wait longer than the heartbeat interval
        await Task.Delay(150);

        servers = await storage.GetActiveServersAsync(TimeSpan.FromMinutes(1));
        var nextHeartbeat = servers[0].HeartbeatAt;

        nextHeartbeat.Should().BeAfter(initialHeartbeat);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    [Fact]
    public async Task StopAsync_DeregistersServer()
    {
        var storage = new InMemoryStorageProvider();
        var options = new NexJobOptions { ServerId = "TestServer1" };
        var svc = MakeService(storage, options);

        await svc.StartAsync(CancellationToken.None);

        var activeServers = await storage.GetActiveServersAsync(TimeSpan.FromMinutes(1));
        activeServers.Should().ContainSingle();

        await svc.StopAsync(CancellationToken.None);

        activeServers = await storage.GetActiveServersAsync(TimeSpan.FromMinutes(1));
        activeServers.Should().BeEmpty();

        svc.Dispose();
    }
}
