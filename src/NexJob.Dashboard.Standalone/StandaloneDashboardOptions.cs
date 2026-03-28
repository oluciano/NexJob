namespace NexJob.Dashboard.Standalone;

/// <summary>
/// Configuration for the NexJob embedded dashboard server.
/// Bind from <c>NexJob:Dashboard</c> in appsettings.json or configure via code.
/// </summary>
public sealed class StandaloneDashboardOptions
{
    /// <summary>
    /// Port the embedded HTTP server listens on. Defaults to <c>5005</c>.
    /// Override via <c>NexJob:Dashboard:Port</c> in appsettings.json to avoid
    /// conflicts with other processes on the same machine.
    /// </summary>
    public int Port { get; set; } = 5005;

    /// <summary>
    /// URL path prefix where the dashboard is mounted. Defaults to <c>/dashboard</c>.
    /// Do NOT use <c>/jobs</c> — it conflicts with common REST API route conventions.
    /// Override via <c>NexJob:Dashboard:Path</c> in appsettings.json.
    /// </summary>
    public string Path { get; set; } = "/dashboard";

    /// <summary>
    /// Title shown in the browser tab and sidebar. Defaults to <c>NexJob</c>.
    /// Override via <c>NexJob:Dashboard:Title</c> in appsettings.json.
    /// </summary>
    public string Title { get; set; } = "NexJob";

    /// <summary>
    /// When <see langword="true"/>, the dashboard only accepts connections from localhost.
    /// Recommended for production worker services. Defaults to <see langword="false"/>.
    /// Override via <c>NexJob:Dashboard:LocalhostOnly</c> in appsettings.json.
    /// </summary>
    public bool LocalhostOnly { get; set; } = false;

    /// <summary>
    /// How often the dashboard SSE stream polls for metrics updates, in seconds.
    /// Defaults to <c>3</c>.
    /// Override via <c>NexJob:Dashboard:PollIntervalSeconds</c> in appsettings.json.
    /// </summary>
    public int PollIntervalSeconds { get; set; } = 3;
}
