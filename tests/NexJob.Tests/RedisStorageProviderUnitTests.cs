using System.Reflection;
using FluentAssertions;
using NexJob.Redis;
using StackExchange.Redis;
using Xunit;

namespace NexJob.Tests;

/// <summary>Unit tests for the pure helpers of <see cref="RedisStorageProvider"/> (no Redis required).</summary>
public sealed class RedisStorageProviderUnitTests
{
    /// <summary>Tests that a higher priority produces a lower queue score.</summary>
    [Fact]
    public void QueueScore_HandlesPriorityCorrectly()
    {
        var method = typeof(RedisStorageProvider).GetMethod("QueueScore", BindingFlags.NonPublic | BindingFlags.Static)!;
        var now = DateTimeOffset.UtcNow;

        var scoreHigh = (double)method.Invoke(null, new object[] { 1, now })!;
        var scoreNormal = (double)method.Invoke(null, new object[] { 3, now })!;

        scoreHigh.Should().BeLessThan(scoreNormal, "Higher priority must have lower score.");
    }

    /// <summary>Tests that a flat key/value array is parsed into a dictionary.</summary>
    [Fact]
    public void ParseFlatArray_HandlesCorrectly()
    {
        var method = typeof(RedisStorageProvider).GetMethod("ParseFlatArray", BindingFlags.NonPublic | BindingFlags.Static)!;
        var flat = new RedisValue[] { "k1", "v1", "k2", "v2" };

        var result = (Dictionary<string, string>)method.Invoke(null, new object[] { flat })!;

        result.Should().HaveCount(2);
        result["k1"].Should().Be("v1");
        result["k2"].Should().Be("v2");
    }
}
