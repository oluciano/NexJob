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
        _ => $"<span class=\"badge\">{s}</span>",
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
            return System.Web.HttpUtility.HtmlEncode(
                System.Text.Json.JsonSerializer.Serialize(doc,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { return System.Web.HttpUtility.HtmlEncode(json); }
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
}
