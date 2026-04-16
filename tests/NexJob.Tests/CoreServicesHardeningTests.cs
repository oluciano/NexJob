using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NexJob.Configuration;
using NexJob.Storage;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// Hardening unit tests for Core background services.
/// Targets 100% branch coverage for maintenance and lifecycle services.
/// </summary>
public sealed class CoreServicesHardeningTests
{
    // ─── JobRetentionService ────────────────────────────────────────────────

    /// <summary>Tests that JobRetentionService executes purge cycles and handles storage errors.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task JobRetentionService_ExecutesPurgeCycles_AndSurvivesErrors()
    {
        var storage = new Mock<IJobStorage>();
        var runtimeStore = new Mock<IRuntimeSettingsStore>();
        var options = new NexJobOptions { RetentionInterval = TimeSpan.FromMilliseconds(10) };

        runtimeStore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new RuntimeSettings());

        // 1. Fails, 2. Succeeds
        storage.SetupSequence(x => x.PurgeJobsAsync(It.IsAny<RetentionPolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Purge error"))
            .ReturnsAsync(1);

        var sut = new JobRetentionService(storage.Object, runtimeStore.Object, options, NullLogger<JobRetentionService>.Instance);
        using var cts = new CancellationTokenSource();

        _ = sut.StartAsync(cts.Token);
        await Task.Delay(100);
        await sut.StopAsync(CancellationToken.None);

        storage.Verify(x => x.PurgeJobsAsync(It.IsAny<RetentionPolicy>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    // ─── OrphanedJobWatcherService ──────────────────────────────────────────

    /// <summary>Tests that OrphanedJobWatcherService executes recovery cycles.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task OrphanedJobWatcherService_ExecutesRecoveryCycles()
    {
        var storage = new Mock<IJobStorage>();
        var options = new NexJobOptions { HeartbeatTimeout = TimeSpan.FromMilliseconds(10) };

        storage.Setup(x => x.RequeueOrphanedJobsAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new OrphanedJobWatcherService(storage.Object, options, NullLogger<OrphanedJobWatcherService>.Instance);
        using var cts = new CancellationTokenSource();

        _ = sut.StartAsync(cts.Token);
        await Task.Delay(100);
        await sut.StopAsync(CancellationToken.None);

        storage.Verify(x => x.RequeueOrphanedJobsAsync(options.HeartbeatTimeout, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ─── ServerHeartbeatService ─────────────────────────────────────────────

    /// <summary>Tests ServerHeartbeatService registration and deregistration lifecycle.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ServerHeartbeatService_HandlesFullLifecycle()
    {
        var storage = new Mock<IJobStorage>();
        var options = new NexJobOptions { ServerId = "h-server", Workers = 5, Queues = new[] { "q1" } };

        var sut = new ServerHeartbeatService(storage.Object, Options.Create(options), NullLogger<ServerHeartbeatService>.Instance);

        // Start
        await sut.StartAsync(CancellationToken.None);
        storage.Verify(x => x.RegisterServerAsync(It.Is<ServerRecord>(s => s.Id == "h-server" && s.WorkerCount == 5), It.IsAny<CancellationToken>()), Times.Once);

        // Stop
        await sut.StopAsync(CancellationToken.None);
        storage.Verify(x => x.DeregisterServerAsync("h-server", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Tests that ServerHeartbeatService survives registration failure at startup.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ServerHeartbeatService_SurvivesRegistrationFailure()
    {
        var storage = new Mock<IJobStorage>();
        storage.Setup(x => x.RegisterServerAsync(It.IsAny<ServerRecord>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB Down"));

        var sut = new ServerHeartbeatService(storage.Object, Options.Create(new NexJobOptions()), NullLogger<ServerHeartbeatService>.Instance);

        Func<Task> act = () => sut.StartAsync(CancellationToken.None);
        await act.Should().NotThrowAsync("Service must survive startup registration errors.");
    }
}
