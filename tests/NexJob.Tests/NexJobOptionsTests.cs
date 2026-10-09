using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using NexJob.Configuration;
using Xunit;

namespace NexJob.Tests;

public sealed class NexJobOptionsTests
{
    [Fact]
    public void DistributedThrottleTtl_DefaultValue_IsOneHour()
    {
        // Assert
        new NexJobOptions().DistributedThrottleTtl.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void PolledQueues_ImplicitDefault_PollsPrefixedAndLegacyDefault()
    {
        var options = new NexJobOptions { QueuePrefix = "billing" };

        options.PolledQueues.Should().Equal("billing.default", "default");
    }

    [Fact]
    public void PolledQueues_NamedQueuesOnly_DoesNotDrainLegacyDefault()
    {
        var options = new NexJobOptions { QueuePrefix = "billing", Queues = ["emails"] };

        options.PolledQueues.Should().Equal("emails");
    }

    [Theory]
    [InlineData("has space")]
    [InlineData(".leading")]
    [InlineData("trailing.")]
    [InlineData("tab\tinside")]
    public void QueuePrefix_WithWhitespaceOrEdgeDots_IsRejected(string prefix)
    {
        var options = new NexJobOptions();

        var act = () => options.QueuePrefix = prefix;

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void QueuePrefix_LongerThanTheLimit_IsRejected()
    {
        var options = new NexJobOptions();

        var act = () => options.QueuePrefix = new string('x', 101);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("billing")]
    [InlineData("acme.billing")]
    [InlineData("   ")]
    [InlineData(null)]
    public void QueuePrefix_ValidOrUnset_IsAccepted(string? prefix)
    {
        var options = new NexJobOptions();

        var act = () => options.QueuePrefix = prefix;

        act.Should().NotThrow();
    }

    [Fact]
    public void ResolveQueue_ExplicitMixedCasePrefix_IsStoredLowercase()
    {
        var options = new NexJobOptions { QueuePrefix = "  Billing " };

        options.ResolveQueue(null).Should().Be("billing.default");
    }

    [Fact]
    public void ResolveQueue_WithoutEntryAssemblyAndPrefix_KeepsTheSharedDefault()
    {
        var options = new NexJobOptions { EntryAssembly = null };

        options.ResolveQueue(null).Should().Be("default");
        options.PolledQueues.Should().Equal("default");
    }

    [Theory]
    [InlineData("billing", "default", "billing.default", true)]
    [InlineData("billing", "billing.default", "billing.default", true)]
    [InlineData("billing", "billing.default", "default", false)]
    [InlineData("billing", "default", "default", true)]
    [InlineData("billing", "default", "emails", false)]
    [InlineData("billing", "emails", "emails", true)]
    [InlineData(null, "default", "default", true)]
    public void IsQueuePaused_FollowsTheDispatcherRule(string? prefix, string paused, string queue, bool expected)
    {
        var options = prefix is null ? new NexJobOptions { EntryAssembly = null } : new NexJobOptions { QueuePrefix = prefix };

        options.IsQueuePaused(queue, [paused]).Should().Be(expected);
    }

    [Fact]
    public void EffectivePrefix_InTheTestHost_IsTestHost()
    {
        // Guard (#377): tests that do not pin QueuePrefix rely on the prefix derived from the test host. If the
        // runner changes, this fails here instead of 20 tests failing on a queue name.
        new NexJobOptions().EffectivePrefix.Should().Be("testhost");
    }

    [Fact]
    public void HealthCheckTimeout_DefaultValue_IsFiveSeconds()
    {
        // Assert
        new NexJobOptions().HealthCheckTimeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void HealthCheckFailedThreshold_DefaultValue_IsOneHundred()
    {
        // Assert
        new NexJobOptions().HealthCheckFailedThreshold.Should().Be(100);
    }

    [Fact]
    public void ApplySettings_ConfiguresHealthCheckTimeoutAndThreshold()
    {
        // Arrange
        var settings = new NexJobSettings
        {
            HealthCheckTimeout = TimeSpan.FromSeconds(12),
            HealthCheckFailedThreshold = 42,
        };
        var options = new NexJobOptions();

        // Act
        options.ApplySettings(settings);

        // Assert
        options.HealthCheckTimeout.Should().Be(TimeSpan.FromSeconds(12));
        options.HealthCheckFailedThreshold.Should().Be(42);
    }

    [Fact]
    public void AddNexJob_RegistersMemoryCache_ForDashboard()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddNexJob();
        using var provider = services.BuildServiceProvider();

        // Assert
        provider.GetService<IMemoryCache>().Should().NotBeNull();
    }
}
