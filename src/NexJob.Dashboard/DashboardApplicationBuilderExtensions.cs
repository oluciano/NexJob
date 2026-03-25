using Microsoft.AspNetCore.Builder;

namespace NexJob.Dashboard;

/// <summary>
/// Extension methods for adding the NexJob dashboard to the middleware pipeline.
/// </summary>
public static class DashboardApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the NexJob dashboard middleware at the specified path prefix.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="pathPrefix">URL prefix where the dashboard is mounted (e.g. <c>/jobs</c>).</param>
    /// <param name="configure">Optional delegate to customise <see cref="DashboardOptions"/>.</param>
    public static IApplicationBuilder UseNexJobDashboard(
        this IApplicationBuilder app,
        string pathPrefix = "/jobs",
        Action<DashboardOptions>? configure = null)
    {
        var options = new DashboardOptions();
        configure?.Invoke(options);
        return app.UseMiddleware<DashboardMiddleware>(pathPrefix, options);
    }
}
