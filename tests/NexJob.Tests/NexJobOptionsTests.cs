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
    public void ResolveQueue_ExplicitMixedCasePrefix_IsStoredAsTyped()
    {
        // Behavior changed in v6.0: an explicit prefix is stored exactly as typed (trimmed). Only the prefix derived
        // from the assembly name is lowercased. Lowercasing it hid what the developer wrote, so a queue written as
        // "Billing.default" in another service no longer matched the stored "billing.default".
        var options = new NexJobOptions { QueuePrefix = "  Billing " };

        options.ResolveQueue(null).Should().Be("Billing.default");
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
    public void SettingsFor_ConfigurationKeyedByDefault_GovernsThePrefixedAndTheLegacyQueue()
    {
        // #402: jobs stored in "default" before the upgrade follow the window and the breaker configured for "default".
        var options = new NexJobOptions { QueuePrefix = "billing" };
        options.ConfigureQueue("default", q => q.Workers = 3);

        var prefixed = options.SettingsFor("billing.default");
        var legacy = options.SettingsFor("default");

        prefixed.Should().NotBeNull();
        legacy.Should().BeSameAs(prefixed);
    }

    [Fact]
    public void SettingsFor_OtherQueue_DoesNotInheritTheDefaultSettings()
    {
        var options = new NexJobOptions { QueuePrefix = "billing" };
        options.ConfigureQueue("default", q => q.Workers = 3);
        options.ConfigureQueue("emails", q => q.Workers = 1);

        options.SettingsFor("emails")!.Name.Should().Be("emails");
        options.SettingsFor("reports").Should().BeNull();
    }

    [Fact]
    public void SettingsFor_ConfiguredWithThePrefixedName_DoesNotGovernTheLegacyQueue()
    {
        var options = new NexJobOptions { QueuePrefix = "billing" };
        options.ConfigureQueue("billing.default", q => q.Workers = 3);

        options.SettingsFor("billing.default").Should().NotBeNull();
        options.SettingsFor("default").Should().BeNull();
    }

    [Fact]
    public void SettingsFor_WithoutPrefix_DefaultIsTheOnlyName()
    {
        var options = new NexJobOptions { EntryAssembly = null };
        options.ConfigureQueue("default", q => q.Workers = 3);

        options.SettingsFor("default").Should().NotBeNull();
        options.SettingsFor("billing.default").Should().BeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Workers_Negative_IsRejected(int value)
    {
        // #404: zero means "this host does not execute jobs"; a negative count means nothing.
        var options = new NexJobOptions();

        var act = () => options.Workers = value;

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(64)]
    public void Workers_ZeroOrPositive_IsAccepted(int value)
    {
        var options = new NexJobOptions { Workers = value };

        options.Workers.Should().Be(value);
    }

    [Theory]
    [InlineData("default", "billing.default", true)]
    [InlineData("default", "default", true)]
    [InlineData(null, "billing.default", true)]
    [InlineData(null, "default", true)]
    [InlineData("billing.default", "billing.default", true)]
    [InlineData("orders", "orders", true)]
    [InlineData("default", "other.default", false)]
    [InlineData("orders", "billing.default", false)]
    [InlineData("orders", "default", false)]
    [InlineData("default", "", false)]
    public void StandsFor_ADefaultOrNamedQueue_MatchesItsStoredNames(string? written, string stored, bool expected)
    {
        // #406: a name written by the developer stands for the stored name it resolves to, and the default also for the legacy "default".
        var options = new NexJobOptions { QueuePrefix = "billing" };

        options.StandsFor(written, stored).Should().Be(expected);
    }

    [Fact]
    public void StandsFor_WithoutPrefix_DefaultIsTheOnlyStoredName()
    {
        var options = new NexJobOptions { EntryAssembly = null };

        options.StandsFor("default", "default").Should().BeTrue();
        options.StandsFor("default", "billing.default").Should().BeFalse();
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

    [Fact]
    public void ConfigureQueue_NameIsExact_ADifferentCaseIsAnotherQueue()
    {
        var options = new NexJobOptions();

        options.ConfigureQueue("Payments", q => q.Workers = 2);
        options.ConfigureQueue("payments", q => q.Workers = 5);

        options.QueueSettings.Should().HaveCount(2);
        options.SettingsFor("Payments")!.Workers.Should().Be(2);
        options.SettingsFor("payments")!.Workers.Should().Be(5);
    }

    [Fact]
    public void ConfigureQueue_SameName_EditsTheSameSettings()
    {
        var options = new NexJobOptions();

        options.ConfigureQueue("payments", q => q.Workers = 2);
        options.ConfigureQueue("payments", q => q.Workers = 5);

        options.QueueSettings.Should().ContainSingle().Which.Workers.Should().Be(5);
    }

    [Fact]
    public void SettingsFor_AndPause_DoNotMatchANameThatDiffersByCase()
    {
        var options = new NexJobOptions();
        options.ConfigureQueue("Payments", q => q.Workers = 2);

        options.SettingsFor("payments").Should().BeNull();
        options.IsQueuePaused("payments", ["Payments"]).Should().BeFalse();
        options.IsQueuePaused("Payments", ["Payments"]).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConfigureQueue_BlankName_Throws(string? name)
    {
        var options = new NexJobOptions();

        var act = () => options.ConfigureQueue(name!, _ => { });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetQueueNameCaseMismatches_ConfiguredNameDiffersByCaseFromAPolledQueue_IsReported()
    {
        var options = new NexJobOptions { Queues = ["payments"] };
        options.ConfigureQueue("Payments", q => q.Workers = null);

        options.GetQueueNameCaseMismatches().Should().ContainSingle()
            .Which.Should().Contain("Payments").And.Contain("payments");
    }

    [Fact]
    public void GetQueueNameCaseMismatches_TwoConfiguredNamesDifferOnlyByCase_AreReported()
    {
        var options = new NexJobOptions { Queues = ["emails"] };
        options.ConfigureQueue("Payments", _ => { });
        options.ConfigureQueue("payments", _ => { });

        options.GetQueueNameCaseMismatches().Should().NotBeEmpty();
    }

    [Fact]
    public void GetQueueNameCaseMismatches_ExactNames_ReportNothing()
    {
        var options = new NexJobOptions { Queues = ["payments"] };
        options.ConfigureQueue("payments", _ => { });
        options.ConfigureQueue("default", _ => { });

        options.GetQueueNameCaseMismatches().Should().BeEmpty();
    }

    [Fact]
    public void GetQueueNameCaseMismatches_NoConfiguration_ReportsNothing()
    {
        new NexJobOptions().GetQueueNameCaseMismatches().Should().BeEmpty();
    }
}
