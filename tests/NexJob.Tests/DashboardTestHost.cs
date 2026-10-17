using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Dashboard.Standalone;
using NexJob.Storage;

namespace NexJob.Tests;

/// <summary>Boots the standalone dashboard on the in-memory provider, seeds it and returns one page.</summary>
internal static class DashboardTestHost
{
    public static async Task<string> GetAsync(string route, Func<IStorageProvider, Task>? seed = null)
    {
        var port = TestPorts.Next();
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob();
                services.AddNexJobStandaloneDashboard(options =>
                {
                    options.Port = port;
                    options.Path = "/dashboard";
                    options.LocalhostOnly = true;
                });
            })
            .Build();

        try
        {
            await host.StartAsync();
            if (seed is not null)
            {
                await seed(host.Services.GetRequiredService<IStorageProvider>());
            }

            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{port}"),
                Timeout = TimeSpan.FromSeconds(5),
            };
            return await client.GetStringAsync(route);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    public static JobRecord Job(string type, string queue, JobStatus status, string? recurringId = null) => new()
    {
        Id = JobId.New(),
        JobType = type,
        Queue = queue,
        Status = status,
        RecurringJobId = recurringId,
        CreatedAt = DateTimeOffset.UtcNow,
        ProcessingStartedAt = DateTimeOffset.UtcNow.AddSeconds(-2),
        CompletedAt = DateTimeOffset.UtcNow,
    };
}
