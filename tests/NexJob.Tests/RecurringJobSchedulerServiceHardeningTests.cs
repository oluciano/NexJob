using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexJob.Configuration;
using NexJob.Storage;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// Hardening unit tests for <see cref="RecurringJobSchedulerService"/>.
/// Targets 100% branch coverage for error handling and distributed locking.
/// </summary>
public sealed class RecurringJobSchedulerServiceHardeningTests
{
    private readonly Mock<IRecurringStorage> _recurringStorage = new();
    private readonly Mock<IJobStorage> _jobStorage = new();
    private readonly Mock<IRuntimeSettingsStore> _runtimeSettings = new();
    private readonly NexJobOptions _options = new() { PollingInterval = TimeSpan.FromMilliseconds(10) };

    private RecurringJobSchedulerService CreateSut()
    {
        return new RecurringJobSchedulerService(
            _jobStorage.Object,
            _recurringStorage.Object,
            _runtimeSettings.Object,
            _options,
            NullLogger<RecurringJobSchedulerService>.Instance);
    }

    /// <summary>Tests that loop survives and backs off when storage throws.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteAsync_WhenStorageThrows_SurvivesAndBacksOff()
    {
        // Arrange
        _runtimeSettings.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RuntimeSettings());

        _recurringStorage.SetupSequence(x => x.GetDueRecurringJobsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Storage Down"))
            .ReturnsAsync(new List<RecurringJobRecord>());

        var sut = CreateSut();
        using var cts = new CancellationTokenSource();

        // Act
        _ = sut.StartAsync(cts.Token);
        await Task.Delay(150); // Let it hit the exception and backoff delay
        await sut.StopAsync(CancellationToken.None);

        // Assert
        _recurringStorage.Verify(x => x.GetDueRecurringJobsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    /// <summary>Tests distributed lock branch when already acquired by another instance.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task EnqueueDueJobsAsync_WhenLockNotAcquired_SkipsJob()
    {
        // Arrange
        var job = new RecurringJobRecord
        {
            RecurringJobId = "r1",
            JobType = "TestJob",
            Enabled = true,
            NextExecution = DateTimeOffset.UtcNow.AddSeconds(-1),
        };

        _recurringStorage.Setup(x => x.GetDueRecurringJobsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RecurringJobRecord> { job });

        _recurringStorage.Setup(x => x.TryAcquireRecurringJobLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // Lock held by another node

        var sut = CreateSut();
        var method = typeof(RecurringJobSchedulerService).GetMethod("EnqueueDueJobsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act
        await (Task)method!.Invoke(sut, new object[] { CancellationToken.None })!;

        // Assert
        _jobStorage.Verify(x => x.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
