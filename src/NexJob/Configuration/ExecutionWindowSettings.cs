namespace NexJob.Configuration;

/// <summary>
/// Defines a time window during which a queue's workers are active.
/// Jobs outside the window are not fetched; they accumulate and run when the window reopens.
/// </summary>
public sealed class ExecutionWindowSettings
{
    /// <summary>Start of the active window (inclusive).</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>End of the active window (inclusive).</summary>
    public TimeOnly EndTime { get; set; }

    /// <summary>IANA or Windows timezone identifier used to evaluate the window. Defaults to <c>UTC</c>.</summary>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>
    /// Days of the week on which the window opens. <see langword="null"/> or empty (the default) means every day.
    /// The day is read from the local time in <see cref="TimeZone"/>, never from UTC. A window that crosses midnight
    /// belongs to the day it starts: with <c>22:00</c>–<c>06:00</c> and Friday selected, Saturday <c>03:00</c> is
    /// inside the window and Saturday <c>23:00</c> is not. Duplicates are ignored.
    /// </summary>
    public DayOfWeek[]? DaysOfWeek { get; set; }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="utcNow"/> falls within this window.
    /// Correctly handles windows that cross midnight (e.g. <c>22:00</c>–<c>06:00</c>). When <see cref="StartTime"/>
    /// equals <see cref="EndTime"/> the window is open for the whole day, which together with <see cref="DaysOfWeek"/>
    /// means "any hour, only on these days".
    /// </summary>
    /// <param name="utcNow">The current UTC time to evaluate.</param>
    public bool IsWithinWindow(DateTimeOffset utcNow)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
        var local = TimeZoneInfo.ConvertTime(utcNow, tz).DateTime;
        var time = TimeOnly.FromDateTime(local);

        if (StartTime < EndTime)
        {
            return time >= StartTime && time <= EndTime && IsDayAllowed(local.DayOfWeek);
        }

        // Crosses midnight (Start == End is a 24 h window). The early hours belong to the day the window started.
        if (time >= StartTime)
        {
            return IsDayAllowed(local.DayOfWeek);
        }

        return time <= EndTime && IsDayAllowed((DayOfWeek)(((int)local.DayOfWeek + 6) % 7));
    }

    private bool IsDayAllowed(DayOfWeek day) =>
        DaysOfWeek is not { Length: > 0 } days || Array.IndexOf(days, day) >= 0;
}
