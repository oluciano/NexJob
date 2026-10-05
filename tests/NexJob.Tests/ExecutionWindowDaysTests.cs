using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NexJob.Configuration;
using NexJob.Dashboard.Pages;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// <see cref="ExecutionWindowSettings.DaysOfWeek"/> (#350): the window can be limited to some days, read from the
/// local time of its time zone, and an overnight window belongs to the day it starts.
/// 2024-06-14 is a Friday, 2024-06-15 a Saturday, 2024-06-16 a Sunday and 2024-06-17 a Monday.
/// </summary>
public sealed class ExecutionWindowDaysTests
{
    private static readonly DayOfWeek[] Weekdays =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    // ─── N1 / N2: a daytime window with days ────────────────────────────────

    /// <summary>N1/N2: a daytime window opens on the selected days only.</summary>
    /// <param name="utcNow">The instant to evaluate.</param>
    /// <param name="expected">Whether the window is open.</param>
    [Theory]
    [InlineData("2024-06-14T09:00:00Z", true)]   // Friday, inside
    [InlineData("2024-06-17T09:00:00Z", true)]   // Monday, inside
    [InlineData("2024-06-15T09:00:00Z", false)]  // Saturday: day excluded
    [InlineData("2024-06-16T09:00:00Z", false)]  // Sunday: day excluded
    [InlineData("2024-06-14T07:59:00Z", false)]  // Friday, before the start
    [InlineData("2024-06-14T18:01:00Z", false)]  // Friday, after the end
    public void IsWithinWindow_DaytimeWindowOnWeekdays_OpensOnSelectedDaysOnly(string utcNow, bool expected)
    {
        var window = Window("08:00", "18:00", "UTC", Weekdays);

        window.IsWithinWindow(Utc(utcNow)).Should().Be(expected);
    }

    // ─── N1 / N2: an overnight window belongs to the day it starts ──────────

    /// <summary>N1/N2: with 22:00–06:00 and only Friday selected, the early hours of Saturday belong to Friday.</summary>
    /// <param name="utcNow">The instant to evaluate.</param>
    /// <param name="expected">Whether the window is open.</param>
    [Theory]
    [InlineData("2024-06-14T23:00:00Z", true)]   // Friday 23:00: the window starts on Friday
    [InlineData("2024-06-14T22:00:00Z", true)]   // Friday 22:00: at the start, inclusive
    [InlineData("2024-06-15T03:00:00Z", true)]   // Saturday 03:00: still Friday's window
    [InlineData("2024-06-15T06:00:00Z", true)]   // Saturday 06:00: at the end, inclusive
    [InlineData("2024-06-15T06:01:00Z", false)]  // Saturday 06:01: just after the end
    [InlineData("2024-06-15T23:00:00Z", false)]  // Saturday 23:00: Saturday is not selected
    [InlineData("2024-06-14T03:00:00Z", false)]  // Friday 03:00: belongs to Thursday's window
    [InlineData("2024-06-16T03:00:00Z", false)]  // Sunday 03:00: belongs to Saturday's window
    public void IsWithinWindow_OvernightWindowOnFriday_BelongsToTheDayItStarts(string utcNow, bool expected)
    {
        var window = Window("22:00", "06:00", "UTC", [DayOfWeek.Friday]);

        window.IsWithinWindow(Utc(utcNow)).Should().Be(expected);
    }

    // ─── N3: default, empty, duplicates ─────────────────────────────────────

    /// <summary>N3: null and empty mean every day, as before the property existed.</summary>
    /// <param name="utcNow">The instant to evaluate.</param>
    [Theory]
    [InlineData("2024-06-14T09:00:00Z")]
    [InlineData("2024-06-15T09:00:00Z")]
    [InlineData("2024-06-16T09:00:00Z")]
    public void IsWithinWindow_WithNullOrEmptyDays_OpensEveryDay(string utcNow)
    {
        Window("08:00", "18:00", "UTC", null).IsWithinWindow(Utc(utcNow)).Should().BeTrue();
        Window("08:00", "18:00", "UTC", []).IsWithinWindow(Utc(utcNow)).Should().BeTrue();
    }

    /// <summary>N3: duplicates are harmless.</summary>
    [Fact]
    public void IsWithinWindow_WithDuplicateDays_BehavesAsASet()
    {
        var window = Window("08:00", "18:00", "UTC", [DayOfWeek.Monday, DayOfWeek.Monday]);

        window.IsWithinWindow(Utc("2024-06-17T09:00:00Z")).Should().BeTrue("Monday is selected");
        window.IsWithinWindow(Utc("2024-06-18T09:00:00Z")).Should().BeFalse("Tuesday is not");
    }

    // ─── N3: the day comes from the local time, never from UTC ──────────────

    /// <summary>N3: Sunday 23:30 UTC is still Sunday evening in New York; Monday 02:00 UTC is still Sunday there.</summary>
    /// <param name="utcNow">The instant to evaluate.</param>
    /// <param name="day">The only selected day.</param>
    /// <param name="expected">Whether the window is open.</param>
    [Theory]
    [InlineData("2024-06-16T23:30:00Z", DayOfWeek.Sunday, true)]   // 19:30 Sunday in New York
    [InlineData("2024-06-16T23:30:00Z", DayOfWeek.Monday, false)]  // not Monday there yet
    [InlineData("2024-06-17T02:00:00Z", DayOfWeek.Sunday, true)]   // 22:00 Sunday in New York although UTC says Monday
    [InlineData("2024-06-17T02:00:00Z", DayOfWeek.Monday, false)]  // UTC says Monday, local time does not
    public void IsWithinWindow_ReadsTheDayFromTheLocalTime(string utcNow, DayOfWeek day, bool expected)
    {
        var window = Window("18:00", "23:00", "America/New_York", [day]);

        window.IsWithinWindow(Utc(utcNow)).Should().Be(expected);
    }

    /// <summary>N3: the day is right on DST transition days, including the repeated hour of the fall-back.</summary>
    /// <param name="utcNow">The instant to evaluate.</param>
    /// <param name="day">The only selected day.</param>
    /// <param name="expected">Whether the window is open.</param>
    [Theory]
    [InlineData("2026-03-08T15:00:00Z", DayOfWeek.Sunday, true)]    // spring forward day, 11:00 EDT
    [InlineData("2026-03-08T15:00:00Z", DayOfWeek.Saturday, false)]
    [InlineData("2026-11-01T05:30:00Z", DayOfWeek.Sunday, true)]    // 01:30 EDT, first pass of the repeated hour
    [InlineData("2026-11-01T06:30:00Z", DayOfWeek.Sunday, true)]    // 01:30 EST, second pass
    [InlineData("2026-11-01T06:30:00Z", DayOfWeek.Saturday, false)]
    public void IsWithinWindow_OnDstTransitionDays_UsesTheLocalDay(string utcNow, DayOfWeek day, bool expected)
    {
        var window = Window("00:00", "12:00", "America/New_York", [day]);

        window.IsWithinWindow(Utc(utcNow)).Should().Be(expected);
    }

    // ─── StartTime == EndTime stays a 24 h window ───────────────────────────

    /// <summary>Start equal to end is a 24 h window: it is how to say "any hour, only on these days".</summary>
    /// <param name="utcNow">The instant to evaluate.</param>
    /// <param name="expected">Whether the window is open.</param>
    [Theory]
    [InlineData("2024-06-17T12:00:00Z", true)]   // Monday noon
    [InlineData("2024-06-17T00:00:00Z", true)]   // Monday midnight
    [InlineData("2024-06-17T23:59:00Z", true)]   // Monday end of day
    [InlineData("2024-06-15T12:00:00Z", false)]  // Saturday: day excluded
    public void IsWithinWindow_WhenStartEqualsEnd_IsAnAllDayWindowOnTheSelectedDays(string utcNow, bool expected)
    {
        var window = Window("00:00", "00:00", "UTC", Weekdays);

        window.IsWithinWindow(Utc(utcNow)).Should().Be(expected);
    }

    /// <summary>Start equal to end without days is open at every hour, as it has always been.</summary>
    [Fact]
    public void IsWithinWindow_WhenStartEqualsEndWithoutDays_IsAlwaysOpen()
    {
        var window = Window("08:00", "08:00", "UTC", null);

        window.IsWithinWindow(Utc("2024-06-15T03:00:00Z")).Should().BeTrue();
        window.IsWithinWindow(Utc("2024-06-15T12:00:00Z")).Should().BeTrue();
    }

    // ─── configuration binding ──────────────────────────────────────────────

    /// <summary>The days bind from configuration by enum name, next to the other window settings.</summary>
    [Fact]
    public void Binding_FromConfiguration_ReadsDaysByName()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["QueueSettings:0:Name"] = "partner-sync",
                ["QueueSettings:0:ExecutionWindow:StartTime"] = "08:00",
                ["QueueSettings:0:ExecutionWindow:EndTime"] = "18:00",
                ["QueueSettings:0:ExecutionWindow:TimeZone"] = "America/Sao_Paulo",
                ["QueueSettings:0:ExecutionWindow:DaysOfWeek:0"] = "Monday",
                ["QueueSettings:0:ExecutionWindow:DaysOfWeek:1"] = "Friday",
            })
            .Build();

        var settings = new NexJobSettings();
        config.Bind(settings);

        var window = settings.QueueSettings.Should().ContainSingle().Which.ExecutionWindow!;
        window.DaysOfWeek.Should().Equal(DayOfWeek.Monday, DayOfWeek.Friday);
        window.TimeZone.Should().Be("America/Sao_Paulo");
    }

    // ─── dashboard summary ──────────────────────────────────────────────────

    /// <summary>The queue row explains the window, so an "Outside Window" badge on a weekend is not a mystery.</summary>
    /// <param name="days">The selected days; <see langword="null"/> means every day.</param>
    /// <param name="expected">The expected summary.</param>
    [Theory]
    [MemberData(nameof(SummaryCases))]
    public void DescribeExecutionWindow_SummarisesTimesZoneAndDays(DayOfWeek[]? days, string expected)
    {
        var window = Window("08:00", "18:00", "America/Sao_Paulo", days);

        Helpers.DescribeExecutionWindow(window).Should().Be(expected);
    }

    /// <summary>The summary cases.</summary>
    /// <returns>The theory data.</returns>
    public static TheoryData<DayOfWeek[]?, string> SummaryCases() => new()
    {
        { null, "08:00–18:00 America/Sao_Paulo · every day" },
        { [], "08:00–18:00 America/Sao_Paulo · every day" },
        { Weekdays, "08:00–18:00 America/Sao_Paulo · Mon–Fri" },
        { [DayOfWeek.Saturday, DayOfWeek.Sunday], "08:00–18:00 America/Sao_Paulo · Sat, Sun" },
        { [DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Monday], "08:00–18:00 America/Sao_Paulo · Mon, Fri" },
    };

    private static ExecutionWindowSettings Window(string start, string end, string timeZone, DayOfWeek[]? days) =>
        new()
        {
            StartTime = TimeOnly.Parse(start, CultureInfo.InvariantCulture),
            EndTime = TimeOnly.Parse(end, CultureInfo.InvariantCulture),
            TimeZone = timeZone,
            DaysOfWeek = days,
        };

    private static DateTimeOffset Utc(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
