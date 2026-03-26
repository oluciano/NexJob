using FluentAssertions;
using NexJob.Configuration;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests;

public sealed class RuntimeSettingsTests
{
    // ─── InMemoryRuntimeSettingsStore ─────────────────────────────────────────

    [Fact]
    public async Task GetAsync_ReturnsDefaultSettings_WhenNothingSaved()
    {
        var store = new InMemoryRuntimeSettingsStore();

        var settings = await store.GetAsync();

        settings.Should().NotBeNull();
        settings.Workers.Should().BeNull();
        settings.PollingInterval.Should().BeNull();
        settings.PausedQueues.Should().BeEmpty();
        settings.RecurringJobsPaused.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_PersistsSettings_ReturnedByGetAsync()
    {
        var store = new InMemoryRuntimeSettingsStore();
        var saved = new RuntimeSettings
        {
            Workers = 5,
            PollingInterval = TimeSpan.FromSeconds(10),
            PausedQueues = ["queue-a"],
            RecurringJobsPaused = true,
        };

        await store.SaveAsync(saved);
        var retrieved = await store.GetAsync();

        retrieved.Workers.Should().Be(5);
        retrieved.PollingInterval.Should().Be(TimeSpan.FromSeconds(10));
        retrieved.PausedQueues.Should().Contain("queue-a");
        retrieved.RecurringJobsPaused.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_SetsUpdatedAt_ToApproximatelyNow()
    {
        var store = new InMemoryRuntimeSettingsStore();
        var before = DateTimeOffset.UtcNow;

        await store.SaveAsync(new RuntimeSettings());

        var settings = await store.GetAsync();
        settings.UpdatedAt.Should().BeOnOrAfter(before);
        settings.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task SaveAsync_ReplacesExistingSettings()
    {
        var store = new InMemoryRuntimeSettingsStore();
        await store.SaveAsync(new RuntimeSettings { Workers = 3 });

        await store.SaveAsync(new RuntimeSettings { Workers = 8 });

        var settings = await store.GetAsync();
        settings.Workers.Should().Be(8);
    }

    // ─── RuntimeSettings defaults ─────────────────────────────────────────────

    [Fact]
    public void RuntimeSettings_Defaults_AreCorrect()
    {
        var settings = new RuntimeSettings();

        settings.Workers.Should().BeNull();
        settings.PollingInterval.Should().BeNull();
        settings.PausedQueues.Should().BeEmpty();
        settings.RecurringJobsPaused.Should().BeFalse();
    }
}
