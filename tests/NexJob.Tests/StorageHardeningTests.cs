using System.Reflection;
using FluentAssertions;
using NexJob.MongoDB;
using NexJob.Postgres;
using NexJob.Redis;
using NexJob.SqlServer;
using StackExchange.Redis;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Hardening unit tests for Storage Provider internal decision logic.
/// Targets 100% branch coverage for parsing and state identification.
/// </summary>
public sealed class StorageHardeningTests
{
    // ─── Postgres ────────────────────────────────────────────────────────────

    /// <summary>Tests Postgres status mapping.</summary>
    [Theory]
    [InlineData("Enqueued", JobStatus.Enqueued)]
    [InlineData("Processing", JobStatus.Processing)]
    [InlineData("Succeeded", JobStatus.Succeeded)]
    [InlineData("Failed", JobStatus.Failed)]
    [InlineData("Scheduled", JobStatus.Scheduled)]
    [InlineData("AwaitingContinuation", JobStatus.AwaitingContinuation)]
    [InlineData("Expired", JobStatus.Expired)]
    [InlineData("Invalid", JobStatus.Failed)]
    public void Postgres_ParseStatus_MapsAllBranches(string input, JobStatus expected)
    {
        var method = typeof(PostgresStorageProvider).GetMethod("ParseStatus", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (JobStatus)method.Invoke(null, new object[] { input })!;
        result.Should().Be(expected);
    }

    /// <summary>Tests Postgres active state identification.</summary>
    [Theory]
    [InlineData(JobStatus.Enqueued, true)]
    [InlineData(JobStatus.Processing, true)]
    [InlineData(JobStatus.Succeeded, false)]
    [InlineData(JobStatus.Failed, false)]
    public void Postgres_IsActiveState_IdentifiesCorrectBranches(JobStatus status, bool expected)
    {
        var method = typeof(PostgresStorageProvider).GetMethod("IsActiveState", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (bool)method.Invoke(null, new object[] { status })!;
        result.Should().Be(expected);
    }

    // ─── SQL Server ──────────────────────────────────────────────────────────

    /// <summary>Tests SQL Server status mapping.</summary>
    [Theory]
    [InlineData("Enqueued", JobStatus.Enqueued)]
    [InlineData("Processing", JobStatus.Processing)]
    [InlineData("Succeeded", JobStatus.Succeeded)]
    [InlineData("Failed", JobStatus.Failed)]
    [InlineData("Scheduled", JobStatus.Scheduled)]
    [InlineData("AwaitingContinuation", JobStatus.AwaitingContinuation)]
    [InlineData("Expired", JobStatus.Expired)]
    [InlineData("Invalid", JobStatus.Failed)]
    public void SqlServer_ParseStatus_MapsCorrectly(string input, JobStatus expected)
    {
        var method = typeof(SqlServerStorageProvider).GetMethod("ParseStatus", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (JobStatus)method.Invoke(null, new object[] { input })!;
        result.Should().Be(expected);
    }

    // ─── Redis ───────────────────────────────────────────────────────────────

    /// <summary>Tests Redis score calculation for priority queuing.</summary>
    [Fact]
    public void Redis_QueueScore_HandlesPriorityCorrectly()
    {
        var method = typeof(RedisStorageProvider).GetMethod("QueueScore", BindingFlags.NonPublic | BindingFlags.Static)!;
        var now = DateTimeOffset.UtcNow;

        var scoreHigh = (double)method.Invoke(null, new object[] { 1, now })!;
        var scoreNormal = (double)method.Invoke(null, new object[] { 3, now })!;

        scoreHigh.Should().BeLessThan(scoreNormal);
    }

    /// <summary>Tests Redis flat array parsing into dictionary.</summary>
    [Fact]
    public void Redis_ParseFlatArray_HandlesCorrectly()
    {
        var method = typeof(RedisStorageProvider).GetMethod("ParseFlatArray", BindingFlags.NonPublic | BindingFlags.Static)!;
        var flat = new RedisValue[] { "k1", "v1", "k2", "v2" };

        var result = (Dictionary<string, string>)method.Invoke(null, new object[] { flat })!;

        result.Should().HaveCount(2);
        result["k1"].Should().Be("v1");
        result["k2"].Should().Be("v2");
    }

    // ─── MongoDB ─────────────────────────────────────────────────────────────

    /// <summary>Tests MongoDB active state identification.</summary>
    [Theory]
    [InlineData(JobStatus.Enqueued, true)]
    [InlineData(JobStatus.Processing, true)]
    [InlineData(JobStatus.Succeeded, false)]
    [InlineData(JobStatus.Failed, false)]
    public void MongoDB_IsActiveState_IdentifiesCorrectly(JobStatus status, bool expected)
    {
        var method = typeof(MongoStorageProvider).GetMethod("IsActiveState", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (bool)method.Invoke(null, new object[] { status })!;
        result.Should().Be(expected);
    }
}
