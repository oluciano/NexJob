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

// Embedded standalone HTTP server hosting dashboard at http://localhost:5005/dashboard
builder.Services.AddNexJobStandaloneDashboard(builder.Configuration);

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
