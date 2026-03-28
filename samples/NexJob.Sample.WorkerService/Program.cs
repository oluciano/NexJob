using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob;
using NexJob.Dashboard.Standalone;
using NexJob.Sample.WorkerService.Jobs;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddNexJob(builder.Configuration)
    .AddNexJobJobs(typeof(Program).Assembly);

// One line — dashboard available at http://localhost:5005/dashboard
builder.Services.AddNexJobStandaloneDashboard(builder.Configuration);

builder.Services.AddTransient<ProcessOrderJob>();
builder.Services.AddTransient<CleanupJob>();

var host = builder.Build();

var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(() =>
{
    using var scope = host.Services.CreateScope();
    var scheduler = scope.ServiceProvider.GetRequiredService<IScheduler>();

    scheduler.RecurringAsync<CleanupJob, CleanupInput>(
        "nightly-cleanup",
        new CleanupInput("temp-files"),
        cron: "0 2 * * *").GetAwaiter().GetResult();

    for (int i = 0; i < 50; i++)
    {
        scheduler.EnqueueAsync<ProcessOrderJob, ProcessOrderInput>(
            new ProcessOrderInput(Guid.NewGuid(), 100m)).GetAwaiter().GetResult();
    }
});

await host.RunAsync();
