using FluentAssertions;
using Xunit;

namespace NexJob.Trigger.Salesforce.Tests;

public sealed class ReplayIdStoreTests : IDisposable
{
    private readonly string _testDir;

    public ReplayIdStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "nexjob_salesforce_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try
            {
                Directory.Delete(_testDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    [Fact]
    public async Task InMemoryStore_SaveAndGet_ReturnsSavedBytes()
    {
        // Arrange
        var store = new InMemoryReplayIdStore();
        var topic = "/data/ChangeEvents";
        byte[] replayId = [10, 20, 30, 40];

        // Act
        await store.SaveReplayIdAsync(topic, replayId);
        var retrieved = await store.GetLastReplayIdAsync(topic);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved.Should().BeEquivalentTo(replayId);
    }

    [Fact]
    public async Task InMemoryStore_GetUnknownTopic_ReturnsNull()
    {
        // Arrange
        var store = new InMemoryReplayIdStore();

        // Act
        var result = await store.GetLastReplayIdAsync("/data/UnknownEvent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task InMemoryStore_Clear_RemovesAllEntries()
    {
        // Arrange
        var store = new InMemoryReplayIdStore();
        await store.SaveReplayIdAsync("/data/AccountChangeEvent", [1, 2, 3]);

        // Act
        store.Clear();
        var result = await store.GetLastReplayIdAsync("/data/AccountChangeEvent");

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task InMemoryStore_InvalidTopic_ThrowsArgumentException(string? topic)
    {
        var store = new InMemoryReplayIdStore();
        Func<Task> actGet = async () => await store.GetLastReplayIdAsync(topic!);
        Func<Task> actSave = async () => await store.SaveReplayIdAsync(topic!, [1, 2]);

        await actGet.Should().ThrowAsync<ArgumentException>();
        await actSave.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task InMemoryStore_NullReplayId_ThrowsArgumentNullException()
    {
        var store = new InMemoryReplayIdStore();
        Func<Task> act = async () => await store.SaveReplayIdAsync("/data/Test", null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task FileStore_SaveAndGet_PersistsBytesToDisk()
    {
        // Arrange
        var store = new FileReplayIdStore(_testDir);
        var topic = "/data/ChangeEvents";
        byte[] expected = [0xAA, 0xBB, 0xCC, 0xDD];

        // Act
        await store.SaveReplayIdAsync(topic, expected);
        var retrieved = await store.GetLastReplayIdAsync(topic);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task FileStore_OverwritingReplayId_UpdatesFileAtomically()
    {
        // Arrange
        var store = new FileReplayIdStore(_testDir);
        var topic = "/data/ChangeEvents";
        byte[] initial = [1, 2, 3];
        byte[] updated = [4, 5, 6, 7];

        // Act
        await store.SaveReplayIdAsync(topic, initial);
        await store.SaveReplayIdAsync(topic, updated);
        var retrieved = await store.GetLastReplayIdAsync(topic);

        // Assert
        retrieved.Should().BeEquivalentTo(updated);
    }

    [Fact]
    public async Task FileStore_GetNonExistentTopic_ReturnsNull()
    {
        // Arrange
        var store = new FileReplayIdStore(_testDir);

        // Act
        var result = await store.GetLastReplayIdAsync("/data/NonExistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task FileStore_EmptyFile_ReturnsNull()
    {
        // Arrange
        var store = new FileReplayIdStore(_testDir);
        var topic = "/data/EmptyTopic";
        var path = store.GetFilePathForTopic(topic);
        await File.WriteAllBytesAsync(path, []);

        // Act
        var result = await store.GetLastReplayIdAsync(topic);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void FileStore_SanitizeTopicName_RemovesInvalidCharacters()
    {
        // Arrange
        var store = new FileReplayIdStore(_testDir);
        var path = store.GetFilePathForTopic("/data/sub/topic:event*");

        // Assert
        Path.GetFileName(path).Should().NotContainAny("/", "\\", ":", "*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task FileStore_InvalidTopic_ThrowsArgumentException(string? topic)
    {
        var store = new FileReplayIdStore(_testDir);
        Func<Task> actGet = async () => await store.GetLastReplayIdAsync(topic!);
        Func<Task> actSave = async () => await store.SaveReplayIdAsync(topic!, [1]);

        await actGet.Should().ThrowAsync<ArgumentException>();
        await actSave.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task FileStore_NullReplayId_ThrowsArgumentNullException()
    {
        var store = new FileReplayIdStore(_testDir);
        Func<Task> act = async () => await store.SaveReplayIdAsync("/data/Test", null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
