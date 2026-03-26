namespace NexJob.Configuration;

/// <summary>Dashboard-specific settings in <c>appsettings.json</c>.</summary>
public sealed class DashboardSettings
{
    /// <summary>URL path at which the dashboard is mounted. Defaults to <c>/jobs</c>.</summary>
    public string Path { get; set; } = "/jobs";

    /// <summary>Title displayed in the browser tab and sidebar. Defaults to <c>NexJob Dashboard</c>.</summary>
    public string Title { get; set; } = "NexJob Dashboard";

    /// <summary>When <see langword="true"/>, requires ASP.NET Core authentication. Defaults to <see langword="false"/>.</summary>
    public bool RequireAuth { get; set; }

    /// <summary>How often the dashboard polls for updated metrics (in seconds). Defaults to <c>3</c>.</summary>
    public int PollIntervalSeconds { get; set; } = 3;
}
