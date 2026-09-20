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
