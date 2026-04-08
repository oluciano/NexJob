using FluentAssertions;
using NexJob.Configuration;
using Xunit;

namespace NexJob.IntegrationTests;

/// <summary>
/// Abstract contract tests for <see cref="IRuntimeSettingsStore"/> implementations.
/// </summary>
public abstract class RuntimeSettingsStoreTestsBase
{
    /// <summary>Creates and returns a ready-to-use runtime settings store for a clean test run.</summary>
    protected abstract Task<IRuntimeSettingsStore> CreateStoreAsync();

    [Fact]
    public async Task GetAsync_WhenEmpty_ReturnsDefaultSettings()
    {
        var store = await CreateStoreAsync();
        var settings = await store.GetAsync();

        settings.Should().NotBeNull();
        settings.Workers.Should().BeNull();
        settings.PollingInterval.Should().BeNull();
        settings.PausedQueues.Should().BeEmpty();
        settings.RecurringJobsPaused.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_ReturnsSavedSettings()
    {
        var store = await CreateStoreAsync();

        var saved = new RuntimeSettings
        {
            Workers = 5,
            PollingInterval = TimeSpan.FromSeconds(30),
            PausedQueues = ["critical", "bulk"],
            RecurringJobsPaused = true,
        };

        await store.SaveAsync(saved);
        var loaded = await store.GetAsync();

        loaded.Workers.Should().Be(5);
        loaded.PollingInterval.Should().Be(TimeSpan.FromSeconds(30));
        loaded.PausedQueues.Should().Contain("critical").And.Contain("bulk");
        loaded.RecurringJobsPaused.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_CalledTwice_OverwritesPreviousSettings()
    {
        var store = await CreateStoreAsync();

        await store.SaveAsync(new RuntimeSettings { Workers = 3, });
        await store.SaveAsync(new RuntimeSettings { Workers = 7, });

        var loaded = await store.GetAsync();
        loaded.Workers.Should().Be(7);
    }

    [Fact]
    public async Task SaveAsync_SetsUpdatedAt()
    {
        var store = await CreateStoreAsync();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        await store.SaveAsync(new RuntimeSettings { Workers = 2, });
        var loaded = await store.GetAsync();

        loaded.UpdatedAt.Should().BeAfter(before);
    }

    [Fact]
    public async Task SaveAsync_WithNullOverrides_RoundTripsCorrectly()
    {
        var store = await CreateStoreAsync();

        await store.SaveAsync(new RuntimeSettings
        {
            Workers = null,
            PollingInterval = null,
            RecurringJobsPaused = false,
        });

        var loaded = await store.GetAsync();
        loaded.Workers.Should().BeNull();
        loaded.PollingInterval.Should().BeNull();
        loaded.RecurringJobsPaused.Should().BeFalse();
    }
}
