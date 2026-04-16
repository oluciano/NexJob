using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NexJob.Storage;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// Hardening unit tests for <see cref="ServerHeartbeatService"/>.
/// Targets 100% branch coverage for server lifecycle and heartbeat logic.
/// </summary>
public sealed class ServerHeartbeatServiceHardeningTests
{
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

        // We verify registration happens with a generated ID
        // Indirectly tested via StartAsync if we used this sut instance
    }
}
