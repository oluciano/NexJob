namespace NexJob.Configuration;

/// <summary>Dashboard-specific settings in <c>appsettings.json</c>.</summary>
public sealed class DashboardSettings
{
    /// <summary>URL path at which the dashboard is mounted. Defaults to <c>/dashboard</c>.</summary>
    public string Path { get; set; } = "/dashboard";

    /// <summary>Title displayed in the browser tab and sidebar. Defaults to <c>NexJob Dashboard</c>.</summary>
    public string Title { get; set; } = "NexJob Dashboard";

    /// <summary>How often the dashboard polls for updated metrics (in seconds). Defaults to <c>3</c>.</summary>
    public int PollIntervalSeconds { get; set; } = 3;

    /// <summary>Port the embedded HTTP server listens on (Standalone mode only). Defaults to <c>5005</c>.</summary>
    public int Port { get; set; } = 5005;

    /// <summary>When <see langword="true"/>, the dashboard only accepts connections from localhost (Standalone mode only). Defaults to <see langword="false"/>.</summary>
    public bool LocalhostOnly { get; set; } = false;
}
