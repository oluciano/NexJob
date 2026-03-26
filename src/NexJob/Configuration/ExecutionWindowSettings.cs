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
    /// Returns <see langword="true"/> if <paramref name="utcNow"/> falls within this window.
    /// Correctly handles windows that cross midnight (e.g. <c>22:00</c>–<c>06:00</c>).
    /// </summary>
    /// <param name="utcNow">The current UTC time to evaluate.</param>
    public bool IsWithinWindow(DateTimeOffset utcNow)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
        var local = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, tz).DateTime);

        return StartTime < EndTime
            ? local >= StartTime && local <= EndTime
            : local >= StartTime || local <= EndTime;
    }
}
