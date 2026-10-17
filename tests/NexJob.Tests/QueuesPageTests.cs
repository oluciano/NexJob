using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Dashboard.Standalone;
using NexJob.Storage;
using Xunit;

namespace NexJob.Tests;

public sealed class QueuesPageTests
{
    [Fact]
    public async Task IdleQueueWithActiveWorker_IsListedWithZeroJobs()
    {
        // N1 (Positive): a queue with no Enqueued/Processing job but an active worker is still listed (#554).
        var html = await GetQueuesPageAsync("tracker-idle");

        html.Should().Contain("tracker-idle");
        html.Should().NotContain("No active queues.");
    }

    [Fact]
    public async Task QueueNoOneServes_IsNotListed()
    {
        // N2 (Negative): a queue with no jobs and no worker stays out of the page.
        var html = await GetQueuesPageAsync("tracker-idle");

        html.Should().NotContain("ghost-queue");
    }

    [Fact]
    public async Task WorkerQueueName_IsHtmlEncoded()
    {
        // N3 (Invalid Input): a hostile queue name from a heartbeat cannot inject markup.
        var html = await GetQueuesPageAsync("<script>alert(1)</script>");

        html.Should().NotContain("<script>alert(1)</script>");
    }

    private static async Task<string> GetQueuesPageAsync(string workerQueue)
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
            var storage = host.Services.GetRequiredService<IStorageProvider>();
            await storage.RegisterServerAsync(new ServerRecord
            {
                Id = "queues-page-test-server",
                WorkerCount = 1,
                Queues = new[] { workerQueue },
                StartedAt = DateTimeOffset.UtcNow,
                HeartbeatAt = DateTimeOffset.UtcNow,
            });

            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{port}"),
                Timeout = TimeSpan.FromSeconds(5),
            };
            return await client.GetStringAsync("/dashboard/queues");
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
