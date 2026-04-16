using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
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
