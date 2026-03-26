using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexJob.Configuration;
using Xunit;

namespace NexJob.Tests;

public sealed class NexJobSettingsTests
{
    // ─── defaults ─────────────────────────────────────────────────────────────

    [Fact]
    public void NexJobSettings_Defaults_AreCorrect()
    {
        var settings = new NexJobSettings();

        settings.Workers.Should().Be(10);
        settings.MaxAttempts.Should().Be(10);
        settings.PollingInterval.Should().Be(TimeSpan.FromSeconds(15));
        settings.HeartbeatInterval.Should().Be(TimeSpan.FromSeconds(30));
        settings.HeartbeatTimeout.Should().Be(TimeSpan.FromMinutes(5));
        settings.DefaultQueue.Should().Be("default");
        settings.Queues.Should().BeEmpty();
    }

    // ─── ApplySettings ────────────────────────────────────────────────────────

    [Fact]
    public void ApplySettings_OverridesAllScalarOptions()
    {
        var options = new NexJobOptions();
        var settings = new NexJobSettings
        {
            Workers = 5,
            MaxAttempts = 3,
            PollingInterval = TimeSpan.FromSeconds(10),
            HeartbeatInterval = TimeSpan.FromSeconds(20),
            HeartbeatTimeout = TimeSpan.FromMinutes(2),
        };

        options.ApplySettings(settings);

        options.Workers.Should().Be(5);
        options.MaxAttempts.Should().Be(3);
        options.PollingInterval.Should().Be(TimeSpan.FromSeconds(10));
        options.HeartbeatInterval.Should().Be(TimeSpan.FromSeconds(20));
        options.HeartbeatTimeout.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void ApplySettings_WithQueues_SetsQueuesOnOptions()
    {
        var options = new NexJobOptions();
        var settings = new NexJobSettings
        {
            Queues =
            [
                new QueueSettings { Name = "critical" },
                new QueueSettings { Name = "default" },
            ],
        };

        options.ApplySettings(settings);

        options.Queues.Should().Equal("critical", "default");
    }

    [Fact]
    public void ApplySettings_WithEmptyQueues_DoesNotOverrideExistingQueues()
    {
        var options = new NexJobOptions();
        options.Queues = ["my-queue"];
        var settings = new NexJobSettings { Queues = [] };

        options.ApplySettings(settings);

        options.Queues.Should().Contain("my-queue");
    }

    // ─── IConfiguration binding ───────────────────────────────────────────────

    [Fact]
    public void AddNexJob_WithConfiguration_BindsSettingsCorrectly()
    {
        var builder = new ConfigurationBuilder();
        ((IConfigurationBuilder)builder).Add(new Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource
        {
            InitialData = new Dictionary<string, string?>
            {
                ["NexJob:Workers"] = "7",
                ["NexJob:MaxAttempts"] = "5",
            },
        });
        var configuration = builder.Build();

        var services = new ServiceCollection();
        services.AddNexJob(configuration);

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<NexJobOptions>();

        options.Workers.Should().Be(7);
        options.MaxAttempts.Should().Be(5);
    }
}
