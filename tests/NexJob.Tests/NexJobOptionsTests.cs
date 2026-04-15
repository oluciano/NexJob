using FluentAssertions;
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
}
