using FluentAssertions;
using Moq;
using NexJob.Configuration;
using NexJob.Internal;
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

        _hardenedSut = new DefaultJobControlService(_hardenedStorage.Object, _hardenedRuntimestore.Object);
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

    private readonly Mock<IDashboardStorage> _hardenedStorage = new();
    private readonly Mock<IRuntimeSettingsStore> _hardenedRuntimestore = new();
    private readonly DefaultJobControlService _hardenedSut;

    /// <summary>Tests that RequeueJobAsync delegates to storage.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task RequeueJobAsync_DelegatesToStorage()
    {
        var id = JobId.New();
        await _hardenedSut.RequeueJobAsync(id);
        _hardenedStorage.Verify(x => x.RequeueJobAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Tests that DeleteJobAsync delegates to storage.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task DeleteJobAsync_DelegatesToStorage()
    {
        var id = JobId.New();
        await _hardenedSut.DeleteJobAsync(id);
        _hardenedStorage.Verify(x => x.DeleteJobAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Tests that PauseQueueAsync saves settings when queue is not already paused.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task PauseQueueAsync_WhenQueueNotPaused_SavesSettings()
    {
        var rt = new RuntimeSettings();
        _hardenedRuntimestore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(rt);

        await _hardenedSut.PauseQueueAsync("q1");

        rt.PausedQueues.Should().Contain("q1");
        _hardenedRuntimestore.Verify(x => x.SaveAsync(rt, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Tests that PauseQueueAsync does not save settings when queue is already paused.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task PauseQueueAsync_WhenQueueAlreadyPaused_DoesNotSave()
    {
        var rt = new RuntimeSettings { PausedQueues = { "q1" } };
        _hardenedRuntimestore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(rt);

        await _hardenedSut.PauseQueueAsync("q1");

        _hardenedRuntimestore.Verify(x => x.SaveAsync(It.IsAny<RuntimeSettings>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Tests that ResumeQueueAsync saves settings when queue is currently paused.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ResumeQueueAsync_WhenQueuePaused_RemovesAndSaves()
    {
        var rt = new RuntimeSettings { PausedQueues = { "q1" } };
        _hardenedRuntimestore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(rt);

        await _hardenedSut.ResumeQueueAsync("q1");

        rt.PausedQueues.Should().NotContain("q1");
        _hardenedRuntimestore.Verify(x => x.SaveAsync(rt, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Tests that ResumeQueueAsync does not save settings when queue is not paused.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ResumeQueueAsync_WhenQueueNotPaused_DoesNotSave()
    {
        var rt = new RuntimeSettings();
        _hardenedRuntimestore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(rt);

        await _hardenedSut.ResumeQueueAsync("q1");

        _hardenedRuntimestore.Verify(x => x.SaveAsync(It.IsAny<RuntimeSettings>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
