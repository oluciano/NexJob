using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob;
using NexJob.Dashboard.Standalone;
using NexJob.Postgres;
using NexJob.Sample.WorkerService.Jobs;

var builder = Host.CreateApplicationBuilder(args);

// Optionally configure PostgreSQL if connection string is provided, otherwise defaults to InMemory
var postgresConnectionString = builder.Configuration.GetConnectionString("NexJobPostgres");
if (!string.IsNullOrWhiteSpace(postgresConnectionString))
{
    builder.Services.AddNexJobPostgres(postgresConnectionString);
}

builder.Services
    .AddNexJob(builder.Configuration)
    .AddNexJobJobs(typeof(Program).Assembly);

// Embedded standalone HTTP server hosting dashboard
if (args.Contains("--multi-cluster"))
{
    builder.Services.AddNexJobStandaloneDashboard(options =>
    {
        options.Port = 5006;
        options.Path = "/dashboard";
        options.Title = "NexJob Federation Hub (Multi-Cluster Demo)";
        options.LocalhostOnly = false;

        // Register clusters using custom or shared storage
        builder.Services.AddSingleton<IHostedService>(sp => new ClusterSetupHostedService(
            options,
            sp.GetRequiredService<NexJob.Storage.IDashboardStorage>(),
            sp.GetRequiredService<NexJob.Storage.IJobStorage>(),
            sp.GetRequiredService<NexJob.Storage.IRecurringStorage>(),
            sp.GetRequiredService<NexJob.IJobControlService>()));
    });
}
else
{
    builder.Services.AddNexJobStandaloneDashboard(options =>
    {
        options.Port = 5005;
        options.Path = "/dashboard";
        options.Title = "NexJob Monocluster (Single Cluster Demo)";
        options.LocalhostOnly = false;
    });
}

var host = builder.Build();

var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        using var scope = host.Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IScheduler>();

        await scheduler.RecurringAsync<CleanupJob, CleanupInput>(
            "nightly-cleanup",
            new CleanupInput("temp-files"),
            cron: "0 2 * * *");

        for (int i = 0; i < 20; i++)
        {
            await scheduler.EnqueueAsync<ProcessOrderJob, ProcessOrderInput>(
                new ProcessOrderInput(Guid.NewGuid(), 100m + i));
        }
    });
});

await host.RunAsync();

internal sealed class ClusterSetupHostedService : IHostedService
{
    private readonly StandaloneDashboardOptions _options;
    private readonly NexJob.Storage.IDashboardStorage _storage;
    private readonly NexJob.Storage.IJobStorage _jobStorage;
    private readonly NexJob.Storage.IRecurringStorage _recurringStorage;
    private readonly NexJob.IJobControlService _controlService;

    public ClusterSetupHostedService(
        StandaloneDashboardOptions options,
        NexJob.Storage.IDashboardStorage storage,
        NexJob.Storage.IJobStorage jobStorage,
        NexJob.Storage.IRecurringStorage recurringStorage,
        NexJob.IJobControlService controlService)
    {
        _options = options;
        _storage = storage;
        _jobStorage = jobStorage;
        _recurringStorage = recurringStorage;
        _controlService = controlService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _options.AddCluster(new NexJob.Dashboard.DashboardCluster(
            "prod-us",
            "Production (US-East)",
            _storage,
            _jobStorage,
            _recurringStorage,
            _controlService,
            isReadOnly: true));

        _options.AddCluster(new NexJob.Dashboard.DashboardCluster(
            "staging",
            "Staging Cluster",
            _storage,
            _jobStorage,
            _recurringStorage,
            _controlService,
            isReadOnly: false));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
