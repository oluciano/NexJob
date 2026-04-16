using System.Reflection;
using FluentAssertions;
using NexJob.Postgres;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Hardening unit tests for <see cref="PostgresStorageProvider"/>.
/// Targets 100% coverage for mapping and decision logic via reflection.
/// </summary>
public sealed class PostgresHardeningTests
{
    private static readonly MethodInfo ParseStatusMethod = typeof(PostgresStorageProvider)
        .GetMethod("ParseStatus", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo IsActiveStateMethod = typeof(PostgresStorageProvider)
        .GetMethod("IsActiveState", BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>Tests all status mapping branches.</summary>
    /// <param name="input">The string input.</param>
    /// <param name="expected">The expected enum.</param>
    [Theory]
    [InlineData("Enqueued", JobStatus.Enqueued)]
    [InlineData("Processing", JobStatus.Processing)]
    [InlineData("Succeeded", JobStatus.Succeeded)]
    [InlineData("Failed", JobStatus.Failed)]
    [InlineData("Scheduled", JobStatus.Scheduled)]
    [InlineData("AwaitingContinuation", JobStatus.AwaitingContinuation)]
    [InlineData("Expired", JobStatus.Expired)]
    [InlineData("Invalid", JobStatus.Failed)]
    public void ParseStatus_MapsAllBranches(string input, JobStatus expected)
    {
        var result = (JobStatus)ParseStatusMethod.Invoke(null, new object[] { input })!;
        result.Should().Be(expected);
    }

    /// <summary>Tests all active state identification branches.</summary>
    /// <param name="status">The status.</param>
    /// <param name="expected">If true, expected.</param>
    [Theory]
    [InlineData(JobStatus.Enqueued, true)]
    [InlineData(JobStatus.Processing, true)]
    [InlineData(JobStatus.Scheduled, true)]
    [InlineData(JobStatus.AwaitingContinuation, true)]
    [InlineData(JobStatus.Succeeded, false)]
    [InlineData(JobStatus.Failed, false)]
    [InlineData(JobStatus.Expired, false)]
    public void IsActiveState_IdentifiesCorrectBranches(JobStatus status, bool expected)
    {
        var result = (bool)IsActiveStateMethod.Invoke(null, new object[] { status })!;
        result.Should().Be(expected);
    }
}
