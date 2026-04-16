using FluentAssertions;
using Moq;
using NexJob.Configuration;
using NexJob.Storage;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// Hardening unit tests for <see cref="DefaultJobControlService"/>.
/// Targets 100% branch coverage for manual job and queue operations.
/// </summary>
public sealed class DefaultJobControlServiceHardeningTests
{
    private readonly Mock<IDashboardStorage> _storage = new();
    private readonly Mock<IRuntimeSettingsStore> _runtimeStore = new();
    private readonly DefaultJobControlService _sut;

    /// <summary>Constructor.</summary>
    public DefaultJobControlServiceHardeningTests()
    {
        _sut = new DefaultJobControlService(_storage.Object, _runtimeStore.Object);
    }

    /// <summary>Tests that RequeueJobAsync delegates to storage.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task RequeueJobAsync_DelegatesToStorage()
    {
        var id = JobId.New();
        await _sut.RequeueJobAsync(id);
        _storage.Verify(x => x.RequeueJobAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Tests that DeleteJobAsync delegates to storage.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task DeleteJobAsync_DelegatesToStorage()
    {
        var id = JobId.New();
        await _sut.DeleteJobAsync(id);
        _storage.Verify(x => x.DeleteJobAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Tests that PauseQueueAsync saves settings when queue is not already paused.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task PauseQueueAsync_WhenQueueNotPaused_SavesSettings()
    {
        var rt = new RuntimeSettings();
        _runtimeStore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(rt);

        await _sut.PauseQueueAsync("q1");

        rt.PausedQueues.Should().Contain("q1");
        _runtimeStore.Verify(x => x.SaveAsync(rt, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Tests that PauseQueueAsync does not save settings when queue is already paused.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task PauseQueueAsync_WhenQueueAlreadyPaused_DoesNotSave()
    {
        var rt = new RuntimeSettings { PausedQueues = { "q1" } };
        _runtimeStore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(rt);

        await _sut.PauseQueueAsync("q1");

        _runtimeStore.Verify(x => x.SaveAsync(It.IsAny<RuntimeSettings>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Tests that ResumeQueueAsync saves settings when queue is currently paused.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ResumeQueueAsync_WhenQueuePaused_RemovesAndSaves()
    {
        var rt = new RuntimeSettings { PausedQueues = { "q1" } };
        _runtimeStore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(rt);

        await _sut.ResumeQueueAsync("q1");

        rt.PausedQueues.Should().NotContain("q1");
        _runtimeStore.Verify(x => x.SaveAsync(rt, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Tests that ResumeQueueAsync does not save settings when queue is not paused.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ResumeQueueAsync_WhenQueueNotPaused_DoesNotSave()
    {
        var rt = new RuntimeSettings();
        _runtimeStore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(rt);

        await _sut.ResumeQueueAsync("q1");

        _runtimeStore.Verify(x => x.SaveAsync(It.IsAny<RuntimeSettings>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
