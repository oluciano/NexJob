using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob.Storage;

namespace NexJob.Dashboard.Standalone;

/// <summary>
/// A hosted service that runs an embedded ASP.NET Core web server to serve
/// the NexJob dashboard. Designed for Worker Services, Console Applications,
/// and any host that does not expose its own HTTP pipeline.
/// The server starts with the host and stops gracefully when the host shuts down.
/// </summary>
internal sealed class StandaloneDashboardHostedService : IHostedService
{
    private readonly StandaloneDashboardOptions _options;
    private readonly IServiceProvider _rootProvider;
    private readonly ILogger<StandaloneDashboardHostedService> _logger;
    private WebApplication? _app;

    /// <summary>
    /// Initializes a new instance of the <see cref="StandaloneDashboardHostedService"/> class.
    /// </summary>
    /// <param name="options">The standalone dashboard options.</param>
    /// <param name="rootProvider">The root service provider.</param>
    /// <param name="logger">The logger.</param>
    public StandaloneDashboardHostedService(
        StandaloneDashboardOptions options,
        IServiceProvider rootProvider,
        ILogger<StandaloneDashboardHostedService> logger)
    {
        _options = options;
        _rootProvider = rootProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var listenUrl = _options.LocalhostOnly
            ? $"http://localhost:{_options.Port}"
            : $"http://0.0.0.0:{_options.Port}";

        // The embedded server has its own container, so a handler registered in the parent host is forwarded.
        var hasAuthorizationHandler = _rootProvider.GetService<IServiceProviderIsService>()
            ?.IsService(typeof(IDashboardAuthorizationHandler)) == true;

        var builder = WebApplication.CreateBuilder();

        // Silence the embedded server's startup banner and reduce log noise
        builder.Logging.ClearProviders();
        builder.WebHost.SuppressStatusMessages(true);
        builder.WebHost.UseUrls(listenUrl);

        var rootNexJobOptions = _rootProvider.GetRequiredService<NexJobOptions>();
        if (_options.DisableWorkers)
        {
            rootNexJobOptions.Workers = 0;
            _logger.LogInformation("Standalone dashboard running in dedicated ops host mode (workers disabled: Workers = 0).");
        }

        // Re-use the IStorageProvider, IRuntimeSettingsStore and NexJobOptions
        // already registered in the parent host — single source of truth
        builder.Services.AddSingleton(
            _rootProvider.GetRequiredService<IStorageProvider>());
        builder.Services.AddSingleton(
            _rootProvider.GetRequiredService<IJobStorage>());
        builder.Services.AddSingleton(
            _rootProvider.GetRequiredService<IRecurringStorage>());
        builder.Services.AddSingleton(
            _rootProvider.GetRequiredService<IDashboardStorage>());
        builder.Services.AddSingleton(
            _rootProvider.GetRequiredService<IJobControlService>());
        var rootScheduler = _rootProvider.GetService<IScheduler>();
        if (rootScheduler is not null)
        {
            builder.Services.AddSingleton(rootScheduler);
        }

        builder.Services.AddSingleton(rootNexJobOptions);
        builder.Services.AddSingleton(
            _rootProvider.GetRequiredService<NexJob.Configuration.IRuntimeSettingsStore>());

        if (hasAuthorizationHandler)
        {
            builder.Services.AddSingleton<IDashboardAuthorizationHandler>(
                new RootScopedDashboardAuthorizationHandler(_rootProvider));
        }

        // Dashboard requires IMemoryCache for metrics caching
        builder.Services.AddMemoryCache();

        _app = builder.Build();
        _app.UseNexJobDashboard(_options.Path, opt =>
        {
            opt.Title = _options.Title;
            opt.DefaultTheme = _options.DefaultTheme;
            opt.EnvironmentName = _options.EnvironmentName;
            opt.EnablePlayground = _options.EnablePlayground;
            opt.Queues = _options.Queues;
            foreach (var cluster in _options.Clusters)
            {
                opt.AddCluster(cluster);
            }
        });

        _app.MapGet("/", (Microsoft.AspNetCore.Http.HttpContext ctx) => ctx.Response.Redirect(_options.Path));

        await _app.StartAsync(cancellationToken);

        _logger.LogInformation(
            "NexJob dashboard listening on {Url}{Path}",
            listenUrl, _options.Path);

        if (!_options.LocalhostOnly && !hasAuthorizationHandler)
        {
            _logger.LogWarning(
                "NexJob dashboard is reachable from the network on {Url}{Path} and has no authorization " +
                "(no IDashboardAuthorizationHandler is registered). Anyone who can reach this port can read job " +
                "payloads and run actions. Set LocalhostOnly = true, or register an IDashboardAuthorizationHandler.",
                listenUrl, _options.Path);
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_app is not null)
        {
            await _app.StopAsync(cancellationToken);
            await _app.DisposeAsync();
        }
    }
}
