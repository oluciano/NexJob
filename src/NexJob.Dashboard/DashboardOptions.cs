namespace NexJob.Dashboard;

/// <summary>Configuration options for the NexJob dashboard middleware.</summary>
public sealed class DashboardOptions
{
    /// <summary>Title shown in the browser tab and sidebar header. Defaults to <c>NexJob</c>.</summary>
    public string Title { get; set; } = "NexJob";

    /// <summary>
    /// When <see langword="true"/>, the dashboard requires the user to be authenticated
    /// via ASP.NET Core authentication middleware. Defaults to <see langword="false"/>.
    /// </summary>
    public bool RequireAuth { get; set; }
}
