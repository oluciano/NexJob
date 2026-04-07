using System.Text.RegularExpressions;

namespace NexJob.Dashboard.Pages;

internal static class Helpers
{
    internal static string ShortType(string fullType)
    {
        var name = fullType.Split(',')[0]; // remove assembly part
        var parts = name.Split('.');
        return parts[^1];
    }

    internal static string BadgeHtml(JobStatus s) => s switch
    {
        JobStatus.Enqueued => "<span class=\"badge badge-enqueued\">Enqueued</span>",
        JobStatus.Processing => "<span class=\"badge badge-processing\">Processing</span>",
        JobStatus.Succeeded => "<span class=\"badge badge-succeeded\">Succeeded</span>",
        JobStatus.Failed => "<span class=\"badge badge-failed\">Failed</span>",
        JobStatus.Scheduled => "<span class=\"badge badge-scheduled\">Scheduled</span>",
        JobStatus.AwaitingContinuation => "<span class=\"badge badge-awaiting\">Awaiting</span>",
        JobStatus.Expired => "<span class=\"badge badge-expired\">Expired</span>",
        _ => $"<span class=\"badge\">{s}</span>",
    };

    internal static string StatusDot(JobStatus status) => status switch
    {
        JobStatus.Processing => "<span class=\"dot dot-processing\"></span>",
        JobStatus.Succeeded => "<span class=\"dot dot-succeeded\"></span>",
        JobStatus.Failed => "<span class=\"dot dot-failed\"></span>",
        JobStatus.Scheduled => "<span class=\"dot dot-scheduled\"></span>",
        JobStatus.Enqueued => "<span class=\"dot dot-enqueued\"></span>",
        JobStatus.AwaitingContinuation => "<span class=\"dot dot-awaiting\"></span>",
        JobStatus.Expired => "<span class=\"dot dot-expired\"></span>",
        _ => "<span class=\"dot dot-default\"></span>",
    };

    internal static string Truncate(string? s, int max)
    {
        if (s is null)
        {
            return string.Empty;
        }

        return s.Length <= max ? s : s[..max] + "…";
    }

    internal static string FormatJson(string json)
    {
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var pretty = System.Text.Json.JsonSerializer.Serialize(doc,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            return ColorizeJson(pretty);
        }
        catch
        {
            return System.Web.HttpUtility.HtmlEncode(json);
        }
    }

    internal static string ColorizeJson(string json) =>
        Regex.Replace(
            System.Web.HttpUtility.HtmlEncode(json),
            @"""((?:[^""\\]|\\.)*)""(\s*:)?|(-?\d+\.?\d*(?:[eE][+-]?\d+)?)|(\btrue\b|\bfalse\b|\bnull\b)",
#pragma warning disable MA0023
            ColorizeMatch,
            RegexOptions.None,
#pragma warning restore MA0023
            TimeSpan.FromMilliseconds(100));

    internal static string ColorizeMatch(Match m)
    {
        if (m.Groups[2].Success)
        {
            return $"<span class='jk'>\"{m.Groups[1].Value}\"</span>{m.Groups[2].Value}";
        }

        if (m.Groups[3].Success)
        {
            return $"<span class='jn'>{m.Value}</span>";
        }

        if (m.Groups[4].Success)
        {
            return $"<span class='jb'>{m.Value}</span>";
        }

        return $"<span class='js'>\"{m.Groups[1].Value}\"</span>";
    }

    internal static string FormatCountdown(TimeSpan ts)
    {
        if (ts <= TimeSpan.Zero)
        {
            return "<span style=\"color:var(--warning)\">Due now</span>";
        }

        if (ts.TotalDays >= 1)
        {
            return $"{(int)ts.TotalDays}d {ts.Hours}h";
        }

        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        }

        if (ts.TotalMinutes >= 1)
        {
            return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        }

        return $"{(int)ts.TotalSeconds}s";
    }

    internal static string CountdownFriendly(TimeSpan span)
    {
        if (span.TotalSeconds < 0)
        {
            return "overdue";
        }

        if (span.TotalMinutes < 1)
        {
            return $"in {(int)span.TotalSeconds}s";
        }

        if (span.TotalHours < 1)
        {
            return $"in {(int)span.TotalMinutes}m";
        }

        if (span.TotalDays < 1)
        {
            return $"in {(int)span.TotalHours}h {span.Minutes}m";
        }

        return $"in {(int)span.TotalDays}d {span.Hours}h";
    }

    internal static string RelativeTime(DateTimeOffset? dt, DateTimeOffset now) =>
        dt is null ? "—" :
        (now - dt.Value) switch
        {
            var d when d.TotalSeconds < 60 => $"{(int)d.TotalSeconds}s ago",
            var d when d.TotalMinutes < 60 => $"{(int)d.TotalMinutes}m ago",
            var d when d.TotalHours < 24 => $"{(int)d.TotalHours}h ago",
            var d => $"{(int)d.TotalDays}d ago",
        };
}
