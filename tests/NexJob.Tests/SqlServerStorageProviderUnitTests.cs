using System.Reflection;
using FluentAssertions;
using NexJob.SqlServer;
using Xunit;

namespace NexJob.Tests;

/// <summary>Unit tests for the pure helpers of <see cref="SqlServerStorageProvider"/> (no database required).</summary>
public sealed class SqlServerStorageProviderUnitTests
{
    /// <summary>Tests that every stored status string maps to its <see cref="JobStatus"/>, unknown values to Failed.</summary>
    /// <param name="input">The stored status.</param>
    /// <param name="expected">The expected status.</param>
    [Theory]
    [InlineData("Enqueued", JobStatus.Enqueued)]
    [InlineData("Processing", JobStatus.Processing)]
    [InlineData("Succeeded", JobStatus.Succeeded)]
    [InlineData("Failed", JobStatus.Failed)]
    [InlineData("Scheduled", JobStatus.Scheduled)]
    [InlineData("AwaitingContinuation", JobStatus.AwaitingContinuation)]
    [InlineData("Expired", JobStatus.Expired)]
    [InlineData("Invalid", JobStatus.Failed)]
    public void ParseStatus_MapsCorrectly(string input, JobStatus expected)
    {
        var method = typeof(SqlServerStorageProvider).GetMethod("ParseStatus", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (JobStatus)method.Invoke(null, new object[] { input })!;
        result.Should().Be(expected);
    }
}
