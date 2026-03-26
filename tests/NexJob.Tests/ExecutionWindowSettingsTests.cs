using System.Globalization;
using FluentAssertions;
using NexJob.Configuration;
using Xunit;

namespace NexJob.Tests;

public sealed class ExecutionWindowSettingsTests
{
    // ─── within window ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("08:00", "18:00", "UTC", "2024-06-15T09:00:00Z", true)]   // daytime: inside
    [InlineData("08:00", "18:00", "UTC", "2024-06-15T08:00:00Z", true)]   // daytime: at start (inclusive)
    [InlineData("08:00", "18:00", "UTC", "2024-06-15T18:00:00Z", true)]   // daytime: at end (inclusive)
    [InlineData("08:00", "18:00", "UTC", "2024-06-15T07:59:00Z", false)]  // daytime: just before
    [InlineData("08:00", "18:00", "UTC", "2024-06-15T18:01:00Z", false)]  // daytime: just after
    [InlineData("08:00", "18:00", "UTC", "2024-06-15T00:00:00Z", false)]  // daytime: midnight
    [InlineData("08:00", "18:00", "UTC", "2024-06-15T23:59:00Z", false)]  // daytime: end of day
    [InlineData("22:00", "06:00", "UTC", "2024-06-15T23:00:00Z", true)]   // midnight-crossing: after start
    [InlineData("22:00", "06:00", "UTC", "2024-06-16T03:00:00Z", true)]   // midnight-crossing: after midnight
    [InlineData("22:00", "06:00", "UTC", "2024-06-15T22:00:00Z", true)]   // midnight-crossing: at start
    [InlineData("22:00", "06:00", "UTC", "2024-06-16T06:00:00Z", true)]   // midnight-crossing: at end
    [InlineData("22:00", "06:00", "UTC", "2024-06-15T12:00:00Z", false)]  // midnight-crossing: middle of day
    [InlineData("22:00", "06:00", "UTC", "2024-06-15T21:59:00Z", false)]  // midnight-crossing: just before start
    [InlineData("22:00", "06:00", "UTC", "2024-06-16T06:01:00Z", false)]  // midnight-crossing: just after end
    public void IsWithinWindow_ReturnsExpected(
        string start, string end, string tz, string utcNow, bool expected)
    {
        var window = new ExecutionWindowSettings
        {
            StartTime = TimeOnly.Parse(start, CultureInfo.InvariantCulture),
            EndTime = TimeOnly.Parse(end, CultureInfo.InvariantCulture),
            TimeZone = tz,
        };

        var result = window.IsWithinWindow(
            DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

        result.Should().Be(expected);
    }

    // ─── defaults ─────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultTimeZone_IsUtc()
    {
        var window = new ExecutionWindowSettings();

        window.TimeZone.Should().Be("UTC");
    }
}
