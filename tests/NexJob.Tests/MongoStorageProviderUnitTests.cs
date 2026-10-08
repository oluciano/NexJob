using System.Reflection;
using FluentAssertions;
using NexJob.MongoDB;
using Xunit;

namespace NexJob.Tests;

/// <summary>Unit tests for the pure helpers of <see cref="MongoStorageProvider"/> (no MongoDB required).</summary>
public sealed class MongoStorageProviderUnitTests
{
    /// <summary>Tests that the id filter can be built for a job id.</summary>
    [Fact]
    public void ById_CreatesFilterSuccessfully()
    {
        var method = typeof(MongoStorageProvider).GetMethod("ById", BindingFlags.NonPublic | BindingFlags.Static)!;
        var jobId = JobId.New();

        var result = method.Invoke(null, new object[] { jobId });

        result.Should().NotBeNull();
    }

    /// <summary>Tests which statuses count as active.</summary>
    /// <param name="status">The status.</param>
    /// <param name="expected">Whether it is active.</param>
    [Theory]
    [InlineData(JobStatus.Enqueued, true)]
    [InlineData(JobStatus.Processing, true)]
    [InlineData(JobStatus.Succeeded, false)]
    [InlineData(JobStatus.Failed, false)]
    public void IsActiveState_IdentifiesCorrectly(JobStatus status, bool expected)
    {
        var method = typeof(MongoStorageProvider).GetMethod("IsActiveState", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (bool)method.Invoke(null, new object[] { status })!;
        result.Should().Be(expected);
    }
}
