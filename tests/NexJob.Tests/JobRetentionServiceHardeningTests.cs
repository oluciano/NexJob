using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexJob.Configuration;
using NexJob.Storage;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// Hardening unit tests for <see cref="JobRetentionService"/>.
/// Targets 100% branch coverage for terminal job purging logic.
/// </summary>
public sealed class JobRetentionServiceHardeningTests
{
    private readonly Mock<IJobStorage> _storage = new();
    private readonly Mock<IRuntimeSettingsStore> _runtimeStore = new();
    private readonly NexJobOptions _options = new()
    {
        RetentionInterval = TimeSpan.FromMilliseconds(10),
        RetentionSucceeded = TimeSpan.FromDays(7),
        RetentionFailed = TimeSpan.FromDays(30),
        RetentionExpired = TimeSpan.FromDays(7),
    };

    /// <summary>Tests that background service executes purge and survives storage errors.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteAsync_RunsPurgeAndSurvivesErrors()
    {
        // Arrange
        _runtimeStore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new RuntimeSettings());

        // Sequence: 1. Throws error, 2. Returns 5 deleted jobs, 3. Returns 0 deleted jobs
        _storage.SetupSequence(x => x.PurgeJobsAsync(It.IsAny<RetentionPolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Storage failure"))
            .ReturnsAsync(5)
            .ReturnsAsync(0);

        var sut = new JobRetentionService(_storage.Object, _runtimeStore.Object, _options, NullLogger<JobRetentionService>.Instance);
        using var cts = new CancellationTokenSource();

        // Act
        _ = sut.StartAsync(cts.Token);
        await Task.Delay(100, CancellationToken.None); // Allow multiple cycles
        await sut.StopAsync(CancellationToken.None);

        // Assert
        _storage.Verify(x => x.PurgeJobsAsync(It.IsAny<RetentionPolicy>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    /// <summary>Tests that runtime overrides are respected during purge.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteAsync_RespectsRuntimeOverrides()
    {
        // Arrange
        var runtime = new RuntimeSettings
        {
            RetentionSucceeded = TimeSpan.FromDays(1),
            RetentionFailed = TimeSpan.FromDays(2),
            RetentionExpired = TimeSpan.FromDays(3),
        };
        _runtimeStore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(runtime);
        _storage.Setup(x => x.PurgeJobsAsync(It.IsAny<RetentionPolicy>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var sut = new JobRetentionService(_storage.Object, _runtimeStore.Object, _options, NullLogger<JobRetentionService>.Instance);
        using var cts = new CancellationTokenSource();

        // Act
        _ = sut.StartAsync(cts.Token);
        await Task.Delay(50, CancellationToken.None);
        await sut.StopAsync(CancellationToken.None);

        // Assert
        _storage.Verify(x => x.PurgeJobsAsync(
            It.Is<RetentionPolicy>(p =>
                p.RetainSucceeded == runtime.RetentionSucceeded &&
                p.RetainFailed == runtime.RetentionFailed &&
                p.RetainExpired == runtime.RetentionExpired),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}
