using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NexJob;
using NexJob.Internal;
using NexJob.Storage;
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

    private readonly Mock<IJobStorage> _storage = new();
    private readonly NexJobOptions _options = new()
    {
        ServerId = "test-server",
        Workers = 10,
        Queues = new[] { "default" },
        ServerHeartbeatInterval = TimeSpan.FromMilliseconds(10),
    };

    private ServerHeartbeatService CreateSut()
    {
        return new ServerHeartbeatService(_storage.Object, Options.Create(_options), NullLogger<ServerHeartbeatService>.Instance);
    }

    /// <summary>Tests that StartAsync registers the server and handles errors.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task StartAsync_RegistersServerAndHandlesFailure()
    {
        // Arrange: 1. Fails
        _storage.Setup(x => x.RegisterServerAsync(It.IsAny<ServerRecord>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Registration failure"));

        var sut = CreateSut();

        // Act
        await sut.StartAsync(CancellationToken.None);

        // Assert: Should not throw
        _storage.Verify(x => x.RegisterServerAsync(It.Is<ServerRecord>(s => s.Id == "test-server"), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Tests that StopAsync deregisters the server and handles errors.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task StopAsync_DeregistersServerAndHandlesFailure()
    {
        // Arrange
        _storage.Setup(x => x.DeregisterServerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Deregistration failure"));

        var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);

        // Act
        await sut.StopAsync(CancellationToken.None);

        // Assert: Should not throw
        _storage.Verify(x => x.DeregisterServerAsync("test-server", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Tests that constructor handles various ServerId configurations.</summary>
    [Fact]
    public void Constructor_HandlesEmptyServerId()
    {
        var options = new NexJobOptions { ServerId = null };
        var sut = new ServerHeartbeatService(_storage.Object, Options.Create(options), NullLogger<ServerHeartbeatService>.Instance);
        sut.Should().NotBeNull();

        // Verify it generated a composite ID
        var field = typeof(ServerHeartbeatService).GetField("_serverId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var id = (string)field!.GetValue(sut)!;
        id.Should().Contain(":");
    }

    /// <summary>Tests that heartbeat handles storage failure.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task HeartbeatAsync_WhenStorageThrows_SurvivesAndLogs()
    {
        // Arrange
        _storage.Setup(x => x.HeartbeatServerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Heartbeat failed"));

        var sut = CreateSut();
        var method = typeof(ServerHeartbeatService).GetMethod("HeartbeatAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act
        var task = (Task)method!.Invoke(sut, null)!;

        // Assert: Should not throw
        await task.Awaiting(t => t).Should().NotThrowAsync();
        _storage.Verify(x => x.HeartbeatServerAsync("test-server", It.IsAny<CancellationToken>()), Times.Once);
    }
}
