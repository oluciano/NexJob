using FluentAssertions;
using Moq;
using NexJob.Configuration;
using NexJob.Storage;
using Xunit;

namespace NexJob.Internal.Tests;

public sealed class JobControlServiceTests
{
    private readonly Mock<IDashboardStorage> _dashboardStorage = new();
    private readonly Mock<IRuntimeSettingsStore> _runtimeStore = new();
    private readonly DefaultJobControlService _sut;

    public JobControlServiceTests()
    {
        _sut = new DefaultJobControlService(_dashboardStorage.Object, _runtimeStore.Object);
    }

    [Fact]
    public async Task RequeueJobAsync_CallsDashboardStorage()
    {
        // Arrange
        var jobId = JobId.New();

        // Act
        await _sut.RequeueJobAsync(jobId);

        // Assert
        _dashboardStorage.Verify(x => x.RequeueJobAsync(jobId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteJobAsync_CallsDashboardStorage()
    {
        // Arrange
        var jobId = JobId.New();

        // Act
        await _sut.DeleteJobAsync(jobId);

        // Assert
        _dashboardStorage.Verify(x => x.DeleteJobAsync(jobId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PauseQueueAsync_AddsToPausedQueues()
    {
        // Arrange
        var settings = new RuntimeSettings();
        _runtimeStore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);

        // Act
        await _sut.PauseQueueAsync("test-queue");

        // Assert
        settings.PausedQueues.Should().Contain("test-queue");
        _runtimeStore.Verify(x => x.SaveAsync(settings, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResumeQueueAsync_RemovesFromPausedQueues()
    {
        // Arrange
        var settings = new RuntimeSettings();
        settings.PausedQueues.Add("test-queue");
        _runtimeStore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);

        // Act
        await _sut.ResumeQueueAsync("test-queue");

        // Assert
        settings.PausedQueues.Should().NotContain("test-queue");
        _runtimeStore.Verify(x => x.SaveAsync(settings, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PauseQueueAsync_AlreadyPaused_IsIdempotent()
    {
        // Arrange
        var settings = new RuntimeSettings();
        settings.PausedQueues.Add("test-queue");
        _runtimeStore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);

        // Act
        await _sut.PauseQueueAsync("test-queue");

        // Assert
        _runtimeStore.Verify(x => x.SaveAsync(It.IsAny<RuntimeSettings>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResumeQueueAsync_NotPaused_IsIdempotent()
    {
        // Arrange
        var settings = new RuntimeSettings();
        _runtimeStore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);

        // Act
        await _sut.ResumeQueueAsync("test-queue");

        // Assert
        _runtimeStore.Verify(x => x.SaveAsync(It.IsAny<RuntimeSettings>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
