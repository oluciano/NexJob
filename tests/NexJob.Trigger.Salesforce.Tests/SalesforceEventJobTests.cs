using FluentAssertions;
using Xunit;

namespace NexJob.Trigger.Salesforce.Tests;

public sealed class SalesforceEventJobTests
{
    [Fact]
    public async Task ExecuteAsync_CompletedTask_ReturnsSuccess()
    {
        var job = new SalesforceEventJob();
        var act = async () => await job.ExecuteAsync("{}", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
