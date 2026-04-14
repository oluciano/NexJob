using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace NexJob.Dashboard;

/// <summary>
/// Extension methods for adding the NexJob dashboard to the middleware pipeline.
/// </summary>
public static class DashboardApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the NexJob dashboard middleware at the specified path prefix.
    /// </summary>
    /// <remarks>
    /// Requires <c>IMemoryCache</c> to be registered in the service collection for metrics caching.
    /// If not already registered, call <c>services.AddMemoryCache()</c> in your startup code.
    /// </remarks>
    /// <param name="app">The application builder.</param>
    /// <param name="pathPrefix">URL prefix where the dashboard is mounted (e.g. <c>/jobs</c>).</param>
    /// <param name="configure">Optional delegate to customise <see cref="DashboardOptions"/>.</param>
    public static IApplicationBuilder UseNexJobDashboard(
        this IApplicationBuilder app,
        string pathPrefix = "/dashboard",
        Action<DashboardOptions>? configure = null)
    {
        // Ensure memory cache is registered — throw descriptive error if missing
        _ = app.ApplicationServices.GetRequiredService<IMemoryCache>();

        var options = new DashboardOptions();
        configure?.Invoke(options);
        return app.UseMiddleware<DashboardMiddleware>(pathPrefix, options);
    }
}
