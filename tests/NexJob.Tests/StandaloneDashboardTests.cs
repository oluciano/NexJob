using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Dashboard.Standalone;
using Xunit;

namespace NexJob.Tests;

public sealed class StandaloneDashboardTests
{
    [Fact]
    public async Task StandaloneDashboard_ServesOverviewAndStream()
    {
        var port = GetFreeTcpPort();
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob();
                services.AddNexJobStandaloneDashboard(options =>
                {
                    options.Port = port;
                    options.Path = "/dashboard";
                    options.Title = "Test Dashboard";
                    options.LocalhostOnly = true;
                });
            })
            .Build();

        try
        {
            await host.StartAsync();

            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{port}"),
                Timeout = TimeSpan.FromSeconds(5),
            };

            var overview = await client.GetAsync("/dashboard");
            overview.StatusCode.Should().Be(HttpStatusCode.OK);

            using var stream = await client.GetAsync(
                "/dashboard/stream",
                HttpCompletionOption.ResponseHeadersRead);
            stream.StatusCode.Should().Be(HttpStatusCode.OK);
            stream.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
