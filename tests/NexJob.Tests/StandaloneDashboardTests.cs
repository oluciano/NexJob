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

            var listenersPage = await client.GetAsync("/dashboard/listeners");
            listenersPage.StatusCode.Should().Be(HttpStatusCode.OK);
            var content = await listenersPage.Content.ReadAsStringAsync();
            content.Should().Contain("Event Listeners");

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

    [Fact]
    public async Task StandaloneDashboard_MaxtonLayout_RendersHeaderThemesAndCategories()
    {
        // N1 (Positive): Overview HTML must contain Maxton layout elements (top header, 5 themes, categorized navigation, theme drawer)
        var port = GetFreeTcpPort();
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob();
                services.AddNexJobStandaloneDashboard(options =>
                {
                    options.Port = port;
                    options.Path = "/dashboard";
                    options.Title = "NexJob Enterprise";
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

            var res = await client.GetAsync("/dashboard");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var html = await res.Content.ReadAsStringAsync();

            // Header & shortcuts
            html.Should().Contain("top-header");
            html.Should().Contain("btn-toggle-sidebar");
            html.Should().Contain("Ctrl + K");
            html.Should().Contain("theme-customizer-btn");

            // Categorized navigation
            html.Should().Contain("MONITORING");
            html.Should().Contain("EXECUTION");
            html.Should().Contain("SYSTEM");

            // 5 Maxton Themes in customizer drawer
            html.Should().Contain("data-theme=\"blue-theme\"");
            html.Should().Contain("data-theme=\"semi-dark\"");
            html.Should().Contain("data-theme=\"bordered-theme\"");
            html.Should().Contain("theme-drawer");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task StandaloneDashboard_NotFound_RendersWithinMaxtonShell()
    {
        // N2 (Negative): Unknown route or 404 still renders within resilient Maxton shell without exploding
        var port = GetFreeTcpPort();
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

            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{port}"),
                Timeout = TimeSpan.FromSeconds(5),
            };

            var res = await client.GetAsync("/dashboard/unknown-page-route-404");
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var html = await res.Content.ReadAsStringAsync();
            html.Should().Contain("404 Not Found");
            html.Should().Contain("top-header");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public void StandaloneDashboard_HtmlShellWrap_HandlesNullAndBoundaryInputsGracefully()
    {
        // N3 (Boundary / Inputs): HtmlShell.Wrap with null counters, null metrics, and empty body
        var output = NexJob.Dashboard.HtmlShell.Wrap(
            title: "Boundary Dashboard",
            pathPrefix: "/dashboard",
            activeRoute: "custom",
            body: string.Empty,
            counters: null,
            metrics: null);

        output.Should().NotBeNullOrWhiteSpace();
        output.Should().Contain("<title>Boundary Dashboard</title>");
        output.Should().Contain("HEALTHY");
        output.Should().Contain("theme-drawer");
        output.Should().Contain("top-header");
    }

    [Fact]
    public async Task StandaloneDashboard_Overview_RendersClusterTopologyMap()
    {
        // N1 (Positive): Overview HTML renders visual cluster topology flowchart
        var port = GetFreeTcpPort();
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

            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{port}"),
                Timeout = TimeSpan.FromSeconds(5),
            };

            var res = await client.GetAsync("/dashboard");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var html = await res.Content.ReadAsStringAsync();

            html.Should().Contain("Cluster Pipeline Topology");
            html.Should().Contain("topology-diagram");
            html.Should().Contain("Ingress & Triggers");
            html.Should().Contain("Queue Buffers");
            html.Should().Contain("Processing Workers");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task StandaloneDashboard_JobsPage_AcceptsPeriodAndQueueFilters()
    {
        // N2 (Positive/Inputs): Jobs page accepts period (1h, 6h, 24h, 7d) and queue query parameters
        var port = GetFreeTcpPort();
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

            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{port}"),
                Timeout = TimeSpan.FromSeconds(5),
            };

            var res = await client.GetAsync("/dashboard/jobs?period=24h&queue=default");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var html = await res.Content.ReadAsStringAsync();

            html.Should().Contain("Last 24 hours");
            html.Should().Contain("value=\"24h\" selected");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public void StandaloneDashboard_HtmlFragments_TopologyMap_HandlesNullAndEmptySafely()
    {
        // N3 (Boundary): TopologyMap renders gracefully with null listeners, null queues, and null servers
        var html = NexJob.Dashboard.Pages.HtmlFragments.TopologyMap(null, null, null, "/dashboard");

        html.Should().NotBeNullOrWhiteSpace();
        html.Should().Contain("Cluster Pipeline Topology");
        html.Should().Contain("Direct Enqueue / Cron");
        html.Should().Contain("Queue: default");
        html.Should().Contain("Worker Nodes");
    }

    [Fact]
    public async Task StandaloneDashboard_WithDisableWorkers_SetsWorkersToZero()
    {
        // N1 (Positive): DisableWorkers = true sets root NexJobOptions.Workers to 0 for dedicated ops host
        var port = GetFreeTcpPort();
        NexJobOptions? capturedOptions = null;

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = 10;
                });
                services.AddNexJobStandaloneDashboard(options =>
                {
                    options.Port = port;
                    options.Path = "/dashboard";
                    options.LocalhostOnly = true;
                    options.DisableWorkers = true;
                });
            })
            .Build();

        capturedOptions = host.Services.GetRequiredService<NexJobOptions>();

        try
        {
            await host.StartAsync();

            capturedOptions.Workers.Should().Be(0);

            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{port}"),
                Timeout = TimeSpan.FromSeconds(5),
            };

            var res = await client.GetAsync("/dashboard");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task StandaloneDashboard_WithQueueScoping_ScopesQueuesAndNavCounters()
    {
        // N1 (Positive): When Queues is configured, QueuesPage and nav counters reflect scoped queues
        var port = GetFreeTcpPort();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Queues = ["payments", "reports", "emails"];
                });
                services.AddNexJobStandaloneDashboard(options =>
                {
                    options.Port = port;
                    options.Path = "/dashboard";
                    options.LocalhostOnly = true;
                    options.Queues = ["payments"];
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

            // Verify navigation counters: total queues in counter is 1, not 3
            var overviewRes = await client.GetAsync("/dashboard");
            overviewRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var overviewHtml = await overviewRes.Content.ReadAsStringAsync();
            overviewHtml.Should().Contain("/1<"); // Queues counter: 0/1

            // Verify Queues page: shows payments queue
            var queuesRes = await client.GetAsync("/dashboard/queues");
            queuesRes.StatusCode.Should().Be(HttpStatusCode.OK);

            // Verify default queue filter on Jobs page: defaults to single scoped queue
            var jobsRes = await client.GetAsync("/dashboard/jobs");
            jobsRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var jobsHtml = await jobsRes.Content.ReadAsStringAsync();
            jobsHtml.Should().Contain("value=\"payments\" selected");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task StandaloneDashboard_WithoutScoping_PreservesGlobalQueues()
    {
        // N2 (Negative): Without Queues scoping (null), all cluster queues are preserved in counters and pages
        var port = GetFreeTcpPort();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Queues = ["orders", "billing"];
                });
                services.AddNexJobStandaloneDashboard(options =>
                {
                    options.Port = port;
                    options.Path = "/dashboard";
                    options.LocalhostOnly = true;
                    options.Queues = null;
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

            var res = await client.GetAsync("/dashboard");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var html = await res.Content.ReadAsStringAsync();
            html.Should().Contain("/2<"); // Queues counter: 0/2
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task StandaloneDashboard_WithEmptyQueuesList_HandlesGracefully()
    {
        // N3 (Boundary): Empty Queues list falls back gracefully without exception
        var port = GetFreeTcpPort();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Queues = ["alpha", "beta"];
                });
                services.AddNexJobStandaloneDashboard(options =>
                {
                    options.Port = port;
                    options.Path = "/dashboard";
                    options.LocalhostOnly = true;
                    options.Queues = Array.Empty<string>();
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

            var res = await client.GetAsync("/dashboard");
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var jobsRes = await client.GetAsync("/dashboard/jobs");
            jobsRes.StatusCode.Should().Be(HttpStatusCode.OK);
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
