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

    public StandaloneDashboardHostedService(
        StandaloneDashboardOptions options,
        IServiceProvider rootProvider,
        ILogger<StandaloneDashboardHostedService> logger)
    {
        _options = options;
        _rootProvider = rootProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var listenUrl = _options.LocalhostOnly
            ? $"http://localhost:{_options.Port}"
            : $"http://0.0.0.0:{_options.Port}";

        var builder = WebApplication.CreateBuilder();

        // Silence the embedded server's startup banner and reduce log noise
        builder.Logging.ClearProviders();
        builder.WebHost.SuppressStatusMessages(true);
        builder.WebHost.UseUrls(listenUrl);

        // Re-use the IStorageProvider, IRuntimeSettingsStore and NexJobOptions
        // already registered in the parent host — single source of truth
        builder.Services.AddSingleton(
            _rootProvider.GetRequiredService<IStorageProvider>());
        builder.Services.AddSingleton(
            _rootProvider.GetRequiredService<NexJobOptions>());
        builder.Services.AddSingleton(
            _rootProvider.GetRequiredService<NexJob.Configuration.IRuntimeSettingsStore>());


        _app = builder.Build();
        _app.UseNexJobDashboard(_options.Path, opt =>
        {
            opt.Title = _options.Title;
            opt.RequireAuth = false;   // auth not supported in standalone mode
        });

        await _app.StartAsync(cancellationToken);

        _logger.LogInformation(
            "NexJob dashboard listening on {Url}{Path}",
            listenUrl, _options.Path);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_app is not null)
        {
            await _app.StopAsync(cancellationToken);
            await _app.DisposeAsync();
        }
    }
}
