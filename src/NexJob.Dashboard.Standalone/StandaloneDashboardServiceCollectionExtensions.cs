using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NexJob.Dashboard.Standalone;

/// <summary>
/// Extension methods for registering the NexJob standalone dashboard
/// with the dependency injection container.
/// </summary>
public static class StandaloneDashboardServiceCollectionExtensions
{
    /// <summary>
    /// Adds the NexJob embedded dashboard server, reading configuration from
    /// <c>NexJob:Dashboard</c> in <paramref name="configuration"/>.
    /// </summary>
    /// <remarks>
    /// Use this overload in Worker Services and Console Applications that do not
    /// have their own HTTP pipeline. The dashboard server starts automatically
    /// when the host starts and is available at
    /// <c>http://localhost:{Port}{Path}</c> (default: http://localhost:5005/dashboard).
    /// </remarks>
    /// <example>
    /// <code>
    /// // appsettings.json
    /// {
    ///   "NexJob": {
    ///     "Dashboard": {
    ///       "Port": 5005,
    ///       "Path": "/dashboard",
    ///       "Title": "My Worker Jobs"
    ///     }
    ///   }
    /// }
    ///
    /// // Program.cs
    /// services.AddNexJob(configuration)
    ///         .AddNexJobStandaloneDashboard(configuration);
    /// </code>
    /// </example>
    public static IServiceCollection AddNexJobStandaloneDashboard(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<StandaloneDashboardOptions>? configure = null)
    {
        var options = new StandaloneDashboardOptions();

        // Bind from NexJob:Dashboard section — same section as DashboardSettings
        // so the developer has a single place to configure everything
        configuration
            .GetSection("NexJob:Dashboard")
            .Bind(options);

        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddHostedService<StandaloneDashboardHostedService>();

        return services;
    }

    /// <summary>
    /// Adds the NexJob embedded dashboard server with code-only configuration.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddNexJob(opt => opt.UsePostgres(conn))
    ///         .AddNexJobStandaloneDashboard(opt =>
    ///         {
    ///             opt.Port = 5005;
    ///             opt.Path = "/dashboard";
    ///             opt.Title = "My Worker Jobs";
    ///         });
    /// </code>
    /// </example>
    public static IServiceCollection AddNexJobStandaloneDashboard(
        this IServiceCollection services,
        Action<StandaloneDashboardOptions>? configure = null)
    {
        var options = new StandaloneDashboardOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddHostedService<StandaloneDashboardHostedService>();

        return services;
    }
}
