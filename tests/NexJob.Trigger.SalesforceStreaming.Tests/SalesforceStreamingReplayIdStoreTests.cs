using FluentAssertions;
using Xunit;

namespace NexJob.Trigger.SalesforceStreaming.Tests;

public sealed class SalesforceStreamingReplayIdStoreTests : IDisposable
{
    private readonly string _tempDirectory;

    public SalesforceStreamingReplayIdStoreTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "nexjob_test_sf_streaming_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch (IOException)
            {
                // ignore
            }
        }
    }

    [Fact]
    public async Task InMemoryStore_NonExistentChannel_ReturnsNull()
    {
        // Arrange
        var store = new InMemoryStreamingReplayIdStore();

        // Act
        var result = await store.GetReplayIdAsync("/data/Unknown__ChangeEvent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task InMemoryStore_SaveAndRetrieve_ReturnsCorrectValue()
    {
        // Arrange
        var store = new InMemoryStreamingReplayIdStore();

        // Act
        await store.SaveReplayIdAsync("/data/Order__ChangeEvent", 42000);
        var result = await store.GetReplayIdAsync("/data/Order__ChangeEvent");

        // Assert
        result.Should().Be(42000);
    }

    [Fact]
    public async Task InMemoryStore_Overwrite_StoresLatestValue()
    {
        // Arrange
        var store = new InMemoryStreamingReplayIdStore();

        // Act
        await store.SaveReplayIdAsync("/data/Order__ChangeEvent", 100);
        await store.SaveReplayIdAsync("/data/Order__ChangeEvent", 200);
        var result = await store.GetReplayIdAsync("/data/Order__ChangeEvent");

        // Assert
        result.Should().Be(200);
    }

    [Fact]
    public async Task InMemoryStore_CancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var store = new InMemoryStreamingReplayIdStore();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.GetReplayIdAsync("/data/Channel", cts.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.SaveReplayIdAsync("/data/Channel", 10, cts.Token));
    }

    [Fact]
    public async Task FileStore_NonExistentChannel_ReturnsNull()
    {
        // Arrange
        var store = new FileStreamingReplayIdStore(_tempDirectory);

        // Act
        var result = await store.GetReplayIdAsync("/data/Order__ChangeEvent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task FileStore_SaveAndRetrieve_ReturnsSavedReplayId()
    {
        // Arrange
        var store = new FileStreamingReplayIdStore(_tempDirectory);

        // Act
        await store.SaveReplayIdAsync("/data/FF_PickOrder__ChangeEvent", 999123);
        var result = await store.GetReplayIdAsync("/data/FF_PickOrder__ChangeEvent");

        // Assert
        result.Should().Be(999123);
    }

    [Fact]
    public async Task FileStore_Overwrite_UpdatesFileAtomically()
    {
        // Arrange
        var store = new FileStreamingReplayIdStore(_tempDirectory);

        // Act
        await store.SaveReplayIdAsync("/data/Invoice__ChangeEvent", 100);
        await store.SaveReplayIdAsync("/data/Invoice__ChangeEvent", 200);
        var result = await store.GetReplayIdAsync("/data/Invoice__ChangeEvent");

        // Assert
        result.Should().Be(200);
    }

    [Fact]
    public async Task FileStore_CorruptedFile_ReturnsNull()
    {
        // Arrange
        var store = new FileStreamingReplayIdStore(_tempDirectory);
        var filePath = store.GetFilePathForChannel("/data/Corrupted");
        await File.WriteAllTextAsync(filePath, "NOT_A_VALID_LONG_NUMBER");

        // Act
        var result = await store.GetReplayIdAsync("/data/Corrupted");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task FileStore_InvalidDirectoryOrChannel_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new FileStreamingReplayIdStore(string.Empty));
        Assert.Throws<ArgumentException>(() => new FileStreamingReplayIdStore("   "));

        var store = new FileStreamingReplayIdStore(_tempDirectory);
        await Assert.ThrowsAsync<ArgumentException>(() => store.GetReplayIdAsync(string.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveReplayIdAsync(string.Empty, 1));
    }

    [Fact]
    public void FileStore_SanitizesSpecialCharactersInChannelName()
    {
        // Arrange
        var store = new FileStreamingReplayIdStore(_tempDirectory);

        // Act
        var filePath = store.GetFilePathForChannel("/topic/special:chars?test*1");

        // Assert
        Path.GetFileName(filePath).Should().NotContain(":");
        Path.GetFileName(filePath).Should().NotContain("?");
        Path.GetFileName(filePath).Should().NotContain("*");
        filePath.Should().EndWith(".replay");
    }
}
