using FluentAssertions;
using NexJob.Dashboard;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests;

/// <summary>Unit tests for <see cref="DashboardJobFactory"/> (#390).</summary>
public sealed class DashboardJobFactoryTests
{
    private static JobRecord Build(string? queue, NexJobOptions options) =>
        DashboardJobFactory.CreateJobRecord(
            typeof(DashboardJobFactoryTests), "raw", typeof(NoInput).AssemblyQualifiedName!, "{}", queue, options);

    /// <summary>N1: a job created without a queue goes to the prefixed default queue.</summary>
    /// <param name="queue">The queue typed in the dashboard.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("default")]
    public void CreateJobRecord_NoQueueOrDefault_UsesThePrefixedDefault(string? queue)
    {
        Build(queue, new NexJobOptions { QueuePrefix = "billing" }).Queue.Should().Be("billing.default");
    }

    /// <summary>N2: a queue the operator typed is used as it is.</summary>
    /// <param name="queue">The queue typed in the dashboard.</param>
    [Theory]
    [InlineData("emails")]
    [InlineData("inventory.default")]
    public void CreateJobRecord_NamedQueue_IsNotPrefixed(string queue)
    {
        Build(queue, new NexJobOptions { QueuePrefix = "billing" }).Queue.Should().Be(queue);
    }

    /// <summary>N3: the attempt limit comes from the options and the record is enqueued.</summary>
    [Fact]
    public void CreateJobRecord_UsesOptionsMaxAttempts()
    {
        var job = Build(null, new NexJobOptions { QueuePrefix = "billing", MaxAttempts = 4 });

        job.MaxAttempts.Should().Be(4);
        job.Status.Should().Be(JobStatus.Enqueued);
    }
}
