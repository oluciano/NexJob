using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexJob.Storage;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// Hardening unit tests for <see cref="OrphanedJobWatcherService"/>.
/// Targets 100% branch coverage for orphaned job recovery logic.
/// </summary>
public sealed class OrphanedJobWatcherServiceHardeningTests
{
    private readonly Mock<IJobStorage> _storage = new();
    private readonly NexJobOptions _options = new()
    {
        HeartbeatTimeout = TimeSpan.FromMilliseconds(10),
    };

    /// <summary>Tests that background service executes recovery and survives storage errors.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteAsync_RunsRecoveryAndSurvivesErrors()
    {
        // Arrange: 1. Throws, 2. Succeeds
        _storage.SetupSequence(x => x.RequeueOrphanedJobsAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Storage failure"))
            .Returns(Task.CompletedTask);

        var sut = new OrphanedJobWatcherService(_storage.Object, _options, NullLogger<OrphanedJobWatcherService>.Instance);
        using var cts = new CancellationTokenSource();

        // Act
        _ = sut.StartAsync(cts.Token);
        await Task.Delay(100, CancellationToken.None);
        await sut.StopAsync(CancellationToken.None);

        // Assert
        _storage.Verify(x => x.RequeueOrphanedJobsAsync(_options.HeartbeatTimeout, It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }
}
