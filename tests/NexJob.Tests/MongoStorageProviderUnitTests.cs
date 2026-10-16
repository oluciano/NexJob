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
}
