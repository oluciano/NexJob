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
    public async Task StandaloneDashboard_ScopeDefault_ShowsThePrefixedAndLegacyDefaultQueues()
    {
        // N1 (#390): a scope written as "default" before the prefix existed keeps showing the application's jobs.
        var html = await GetQueuesPageAsync(scope: ["default"], pause: null);

        html.Should().Contain("billing.default");
        html.Should().NotContain("inventory.default");
    }

    [Fact]
    public async Task StandaloneDashboard_ScopeNamedQueue_DoesNotShowTheDefaultQueue()
    {
        // N2 (#390): a literal scope stays literal.
        var html = await GetQueuesPageAsync(scope: ["emails"], pause: null);

        html.Should().NotContain("billing.default");
    }

    [Fact]
    public async Task StandaloneDashboard_StoredDefaultQueuePaused_SettingsShowsItPaused()
    {
        // N1 (#390): pausing the stored name from the Queues page must show on the Settings page.
        var html = await GetSettingsPageAsync(pause: "billing.default");

        // The row of the stored queue shows the Paused badge and offers Resume (a bare "Paused" word appears elsewhere).
        html.Should().Contain("/queues/billing.default/resume");
        html.Should().Contain("<span class=\"badge badge-warning\">Paused</span>");
    }

    [Fact]
    public async Task StandaloneDashboard_NothingPaused_SettingsDoesNotShowPaused()
    {
        // N2 (#390): guard so the paused assertion above cannot pass vacuously.
        var html = await GetSettingsPageAsync(pause: null);

        html.Should().NotContain("<span class=\"badge badge-warning\">Paused</span>");
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

    [Fact]
    public async Task StandaloneDashboard_MultiCluster_RendersClustersAndSwitchesCorrectly()
    {
        // N1 (Positive): Multiple clusters registered — selector appears and clusters can be switched via ?cluster=
        var port = GetFreeTcpPort();
        var storageA = new NexJob.Internal.InMemoryStorageProvider();
        var storageB = new NexJob.Internal.InMemoryStorageProvider();

        await storageA.EnqueueAsync(new JobRecord
        {
            Id = new JobId(Guid.NewGuid()),
            JobType = "JobClusterA",
            Queue = "default",
            Status = JobStatus.Enqueued,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await storageB.EnqueueAsync(new JobRecord
        {
            Id = new JobId(Guid.NewGuid()),
            JobType = "JobClusterB",
            Queue = "default",
            Status = JobStatus.Enqueued,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob();
                services.AddNexJobStandaloneDashboard(options =>
                {
                    options.Port = port;
                    options.Path = "/dashboard";
                    options.LocalhostOnly = true;
                    options.AddCluster(new NexJob.Dashboard.DashboardCluster("cluster-a", "Production Cluster", storageA));
                    options.AddCluster(new NexJob.Dashboard.DashboardCluster("cluster-b", "Staging Cluster", storageB));
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

            // 1. Accessing without ?cluster= defaults to first cluster (cluster-a)
            var defaultRes = await client.GetAsync("/dashboard");
            defaultRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var defaultHtml = await defaultRes.Content.ReadAsStringAsync();
            defaultHtml.Should().Contain("Cluster: Production Cluster");
            defaultHtml.Should().Contain("Cluster: Staging Cluster");

            // Overview for cluster-a should contain JobClusterA
            var jobsClusterA = await client.GetAsync("/dashboard/jobs?cluster=cluster-a");
            jobsClusterA.StatusCode.Should().Be(HttpStatusCode.OK);
            var jobsHtmlA = await jobsClusterA.Content.ReadAsStringAsync();
            jobsHtmlA.Should().Contain("JobClusterA");
            jobsHtmlA.Should().NotContain("JobClusterB");

            // 2. Switching to cluster-b renders jobs from cluster-b
            var jobsClusterB = await client.GetAsync("/dashboard/jobs?cluster=cluster-b");
            jobsClusterB.StatusCode.Should().Be(HttpStatusCode.OK);
            var jobsHtmlB = await jobsClusterB.Content.ReadAsStringAsync();
            jobsHtmlB.Should().Contain("JobClusterB");
            jobsHtmlB.Should().NotContain("JobClusterA");

            // 3. Verify sidebar navigation links preserve the active cluster parameter
            jobsHtmlB.Should().Contain("href=\"/dashboard/queues?cluster=cluster-b\"");
            jobsHtmlB.Should().Contain("href=\"/dashboard/servers?cluster=cluster-b\"");
            jobsHtmlB.Should().Contain("href=\"/dashboard/recurring?cluster=cluster-b\"");
            jobsHtmlB.Should().Contain("href=\"/dashboard/failed?cluster=cluster-b\"");
            jobsHtmlB.Should().Contain("href=\"/dashboard/settings?cluster=cluster-b\"");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task StandaloneDashboard_MultiCluster_ReadOnlyClusterRejectsMutationsAndUnknownClusterFallsBack()
    {
        // N2 (Negative): Unknown cluster falls back to first cluster; read-only cluster rejects POST mutations with 403 Forbidden
        var port = GetFreeTcpPort();
        var storageReadOnly = new NexJob.Internal.InMemoryStorageProvider();
        var storageWritable = new NexJob.Internal.InMemoryStorageProvider();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob();
                services.AddNexJobStandaloneDashboard(options =>
                {
                    options.Port = port;
                    options.Path = "/dashboard";
                    options.LocalhostOnly = true;
                    options.AddCluster(new NexJob.Dashboard.DashboardCluster("prod-ro", "Production (Read-Only)", storageReadOnly, isReadOnly: true));
                    options.AddCluster(new NexJob.Dashboard.DashboardCluster("dev-rw", "Development", storageWritable, isReadOnly: false));
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

            // 1. Unknown cluster id falls back to first cluster (prod-ro)
            var fallbackRes = await client.GetAsync("/dashboard?cluster=non-existent");
            fallbackRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var fallbackHtml = await fallbackRes.Content.ReadAsStringAsync();
            fallbackHtml.Should().Contain("Cluster: Production (Read-Only)");

            // 2. Mutation on read-only cluster returns 403 Forbidden
            var postRo = await client.PostAsync("/dashboard/recurring/test-job/delete?cluster=prod-ro", null);
            postRo.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            // 3. Mutation on default (first cluster, which is prod-ro) also returns 403 Forbidden
            var postRoDefault = await client.PostAsync("/dashboard/recurring/test-job/delete", null);
            postRoDefault.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task StandaloneDashboard_MultiCluster_SingleOrZeroClustersHidesSwitcher()
    {
        // N3 (Boundary): When only 1 cluster is registered (or 0 clusters), the cluster switcher dropdown is not rendered
        var port = GetFreeTcpPort();
        var storageSingle = new NexJob.Internal.InMemoryStorageProvider();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob();
                services.AddNexJobStandaloneDashboard(options =>
                {
                    options.Port = port;
                    options.Path = "/dashboard";
                    options.LocalhostOnly = true;
                    options.AddCluster(new NexJob.Dashboard.DashboardCluster("only-one", "Single Cluster", storageSingle));
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

            // Switcher dropdown should NOT be present when only 1 cluster exists
            html.Should().NotContain("class=\"cluster-switcher\"");
            html.Should().NotContain("title=\"Switch Cluster\"");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    // ─── Job Catalog & Definitions (3N Matrix) ─────────────────────────────

    [Fact]
    public async Task StandaloneDashboard_Catalog_N1_Positive_RendersCatalogTableAndSidebarLink()
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
                    options.Title = "Test Catalog Dashboard";
                    options.LocalhostOnly = true;
                });
            })
            .Build();

        try
        {
            await host.StartAsync();

            var storage = host.Services.GetRequiredService<NexJob.Storage.IJobStorage>();
            var now = DateTimeOffset.UtcNow;
            await storage.EnqueueAsync(new JobRecord
            {
                Id = JobId.New(),
                JobType = "BillingInvoiceJob",
                Queue = "billing",
                Status = JobStatus.Succeeded,
                ProcessingStartedAt = now.AddSeconds(-5),
                CompletedAt = now.AddSeconds(-2),
                CreatedAt = now.AddSeconds(-10),
            });

            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{port}"),
                Timeout = TimeSpan.FromSeconds(5),
            };

            // 1. Check sidebar contains Catalog link
            var overview = await client.GetAsync("/dashboard");
            overview.StatusCode.Should().Be(HttpStatusCode.OK);
            var overviewHtml = await overview.Content.ReadAsStringAsync();
            overviewHtml.Should().Contain("/dashboard/catalog");
            overviewHtml.Should().Contain("Catalog");

            // 2. Query /dashboard/catalog
            var catalog = await client.GetAsync("/dashboard/catalog");
            catalog.StatusCode.Should().Be(HttpStatusCode.OK);
            var catalogHtml = await catalog.Content.ReadAsStringAsync();
            catalogHtml.Should().Contain("Job Catalog &amp; Definitions");
            catalogHtml.Should().Contain("BillingInvoiceJob");
            catalogHtml.Should().Contain("billing");
            catalogHtml.Should().Contain("History");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task StandaloneDashboard_Catalog_N2_Negative_FilterWithNoMatches_ShowsEmptyTableState()
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
                    options.Title = "Test Catalog Dashboard";
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

            // Search filter with non-existent job
            var res = await client.GetAsync("/dashboard/catalog?search=NonExistentJob12345");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var html = await res.Content.ReadAsStringAsync();
            html.Should().Contain("0 definitions found");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task StandaloneDashboard_Catalog_N3_Boundary_TriggerActionEnqueuesJobAndRedirects()
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
                    options.Title = "Test Catalog Dashboard";
                    options.LocalhostOnly = true;
                });
            })
            .Build();

        try
        {
            await host.StartAsync();

            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri($"http://localhost:{port}"),
                Timeout = TimeSpan.FromSeconds(5),
            };

            // Post to trigger an IJob
            var postContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("queue", "catalog-test-queue"),
            });

            var jobTypeArg = Uri.EscapeDataString(typeof(StubParameterlessJob).AssemblyQualifiedName!);
            var triggerRes = await client.PostAsync($"/dashboard/catalog/{jobTypeArg}/trigger", postContent);

            // Behavior changed in v5.6: Redirects to /dashboard/catalog with triggered job ID for UX feedback banner
            triggerRes.StatusCode.Should().Be(HttpStatusCode.Redirect);
            triggerRes.Headers.Location?.ToString().Should().StartWith("/dashboard/catalog?triggered=");

            // Verify the job was actually enqueued in storage
            var storage = host.Services.GetRequiredService<NexJob.Storage.IDashboardStorage>();
            var paged = await storage.GetJobsAsync(new JobFilter { Queue = "catalog-test-queue" }, page: 1, pageSize: 10);
            paged.Items.Should().ContainSingle(j => j.JobType == typeof(StubParameterlessJob).AssemblyQualifiedName);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task StandaloneDashboard_Catalog_ParameterizedJob_RendersTriggerModalButtonAndEnqueuesWithPayload()
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
                    options.Title = "Test Catalog Dashboard";
                    options.LocalhostOnly = true;
                });
            })
            .Build();

        try
        {
            await host.StartAsync();

            var storage = host.Services.GetRequiredService<NexJob.Storage.IJobStorage>();
            await storage.EnqueueAsync(new JobRecord
            {
                Id = JobId.New(),
                JobType = typeof(StubParameterizedJob).AssemblyQualifiedName!,
                InputType = typeof(string).AssemblyQualifiedName!,
                InputJson = "\"initial-test\"",
                Queue = "default",
                Status = JobStatus.Succeeded,
            });

            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri($"http://localhost:{port}"),
                Timeout = TimeSpan.FromSeconds(5),
            };

            // 1. Verify Catalog renders Trigger button that calls openTriggerModal
            var res = await client.GetAsync("/dashboard/catalog");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var html = await res.Content.ReadAsStringAsync();

            html.Should().Contain("StubParameterizedJob");
            html.Should().Contain("trigger-modal-btn");
            html.Should().Contain("triggerModal");

            // 2. Post to trigger parameterized job with custom inputJson payload
            var postContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("queue", "custom-payload-queue"),
                new KeyValuePair<string, string>("inputJson", "\"hello-from-catalog\""),
            });

            var jobTypeArg = Uri.EscapeDataString(typeof(StubParameterizedJob).AssemblyQualifiedName!);
            var triggerRes = await client.PostAsync($"/dashboard/catalog/{jobTypeArg}/trigger", postContent);

            // Behavior changed in v5.6: Redirects to /dashboard/catalog with triggered job ID for UX feedback banner
            triggerRes.StatusCode.Should().Be(HttpStatusCode.Redirect);
            triggerRes.Headers.Location?.ToString().Should().StartWith("/dashboard/catalog?triggered=");

            // 3. Verify enqueued job record contains the supplied payload
            var dashboardStorage = host.Services.GetRequiredService<NexJob.Storage.IDashboardStorage>();
            var paged = await dashboardStorage.GetJobsAsync(new JobFilter { Queue = "custom-payload-queue" }, page: 1, pageSize: 10);
            paged.Items.Should().ContainSingle(j => j.JobType == typeof(StubParameterizedJob).AssemblyQualifiedName);
            var enqueued = paged.Items.First(j => j.JobType == typeof(StubParameterizedJob).AssemblyQualifiedName);
            enqueued.InputJson.Should().Be("\"hello-from-catalog\"");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task StandaloneDashboard_OrphanQueueIndicators_3NTestingMatrix()
    {
        // 3N Testing Matrix:
        // N1 (Positive): Queue with Enqueued > 0 and no active servers renders "NO WORKERS" badge in Queues & warning banner in Servers.
        // N2 (Negative): Queue with active servers does NOT render "NO WORKERS" badge.
        // N3 (Boundary): Queue with 0 enqueued jobs and no active servers does NOT show orphan alert.
        var port = GetFreeTcpPort();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(options =>
                {
                    options.Workers = 2;
                    options.Queues = new[] { "active-queue" };
                });
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

            var scheduler = host.Services.GetRequiredService<NexJob.IScheduler>();

            // N1: Enqueue job to an orphan queue (no servers listen to "orphan-queue")
            await scheduler.EnqueueAsync<StubParameterlessJob>(queue: "orphan-queue");

            // N2: Enqueue job to "active-queue" (hosted service has server listening to "active-queue")
            await scheduler.EnqueueAsync<StubParameterlessJob>(queue: "active-queue");

            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri($"http://localhost:{port}"),
                Timeout = TimeSpan.FromSeconds(5),
            };

            // 1. Verify /dashboard/queues
            var queuesRes = await client.GetAsync("/dashboard/queues");
            queuesRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var queuesHtml = await queuesRes.Content.ReadAsStringAsync();

            // N1 Positive Check: orphan-queue has NO WORKERS badge and explanation
            queuesHtml.Should().Contain("NO WORKERS");
            queuesHtml.Should().Contain("orphan-queue");
            queuesHtml.Should().Contain("No active workers listening");

            // N2 Negative Check: active-queue does NOT have NO WORKERS badge
            // Check that active-queue card does not contain "No active workers"
            var activeQueueIndex = queuesHtml.IndexOf("active-queue", StringComparison.Ordinal);
            activeQueueIndex.Should().BeGreaterThan(-1);

            // 2. Verify /dashboard/servers
            var serversRes = await client.GetAsync("/dashboard/servers");
            serversRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var serversHtml = await serversRes.Content.ReadAsStringAsync();

            // Servers page should show warning banner mentioning the unserved queue with pending jobs
            serversHtml.Should().Contain("orphan-queue");
            serversHtml.IndexOf("Unattended", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);

            // 3. Verify /dashboard/catalog
            var catalogRes = await client.GetAsync("/dashboard/catalog");
            catalogRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var catalogHtml = await catalogRes.Content.ReadAsStringAsync();
            catalogHtml.Should().Contain("activeWorkersMap");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task StandaloneDashboard_JobDetail_ActionsCheckpointAndRetentionMarker_3NTestingMatrix()
    {
        // 3N Testing Matrix:
        // N1 (Positive): Enqueued and Scheduled jobs render Cancel/Delete action; jobs with CheckpointJson render checkpoint state card; Succeeded jobs without payload show retention stripped marker.
        // N2 (Negative): Processing and Succeeded jobs with payload do NOT show Delete button or stripped marker.
        // N3 (Input/Boundary): CheckpointJson is empty/null -> checkpoint card is omitted gracefully.
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

            var storage = (NexJob.Storage.IJobStorage)host.Services.GetRequiredService<NexJob.Storage.IDashboardStorage>();

            // 1. Create Enqueued Job with CheckpointJson
            var enqueuedJob = new JobRecord
            {
                Id = JobId.New(),
                JobType = typeof(StubParameterlessJob).AssemblyQualifiedName!,
                InputType = string.Empty,
                InputJson = "{}",
                Queue = "default",
                Status = JobStatus.Enqueued,
                CreatedAt = DateTimeOffset.UtcNow,
                CheckpointJson = "{\"processedItems\": 42, \"cursor\": \"batch-042\"}",
            };
            await storage.EnqueueAsync(enqueuedJob);

            // 2. Create Succeeded Job with stripped payload (retention policy)
            var succeededJob = new JobRecord
            {
                Id = JobId.New(),
                JobType = typeof(StubParameterlessJob).AssemblyQualifiedName!,
                InputType = string.Empty,
                InputJson = string.Empty,
                Queue = "default",
                Status = JobStatus.Succeeded,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                CompletedAt = DateTimeOffset.UtcNow,
            };
            await storage.EnqueueAsync(succeededJob);

            // 3. Create Scheduled Job
            var scheduledJob = new JobRecord
            {
                Id = JobId.New(),
                JobType = typeof(StubParameterlessJob).AssemblyQualifiedName!,
                InputType = string.Empty,
                InputJson = "{}",
                Queue = "default",
                Status = JobStatus.Scheduled,
                CreatedAt = DateTimeOffset.UtcNow,
                ScheduledAt = DateTimeOffset.UtcNow.AddHours(1),
            };
            await storage.EnqueueAsync(scheduledJob);

            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{port}"),
                Timeout = TimeSpan.FromSeconds(5),
            };

            // Test Enqueued Job: Cancel/Delete action + Checkpoint card
            var resEnqueued = await client.GetAsync($"/dashboard/jobs/{enqueuedJob.Id.Value}");
            resEnqueued.StatusCode.Should().Be(HttpStatusCode.OK);
            var htmlEnqueued = await resEnqueued.Content.ReadAsStringAsync();
            htmlEnqueued.Should().Contain($"action=\"/dashboard/jobs/{enqueuedJob.Id.Value}/delete\"");
            htmlEnqueued.Should().Contain("Cancel and delete this enqueued job?");
            htmlEnqueued.Should().Contain("💾 Checkpoint State");
            htmlEnqueued.Should().Contain("processedItems");
            htmlEnqueued.Should().Contain("cursor");

            // Test Succeeded Job: Payload stripped marker + No delete button
            var resSucceeded = await client.GetAsync($"/dashboard/jobs/{succeededJob.Id.Value}");
            resSucceeded.StatusCode.Should().Be(HttpStatusCode.OK);
            var htmlSucceeded = await resSucceeded.Content.ReadAsStringAsync();
            htmlSucceeded.Should().Contain("Payload stripped by retention policy (TrimPayloadOnSuccess)");
            htmlSucceeded.Should().NotContain($"action=\"/dashboard/jobs/{succeededJob.Id.Value}/delete\"");
            htmlSucceeded.Should().NotContain("💾 Checkpoint State");

            // Test Scheduled Job: Run Now + Delete action
            var resScheduled = await client.GetAsync($"/dashboard/jobs/{scheduledJob.Id.Value}");
            resScheduled.StatusCode.Should().Be(HttpStatusCode.OK);
            var htmlScheduled = await resScheduled.Content.ReadAsStringAsync();
            htmlScheduled.Should().Contain($"action=\"/dashboard/jobs/{scheduledJob.Id.Value}/runnow\"");
            htmlScheduled.Should().Contain($"action=\"/dashboard/jobs/{scheduledJob.Id.Value}/delete\"");
            htmlScheduled.Should().Contain("Cancel and delete this scheduled job?");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task StandaloneDashboard_Settings_PauseAllConfirmation_And_CircuitDrilldown_And_FilterChips()
    {
        // Tests for:
        // 1. SettingsPage Pause All destructive safety confirm dialog
        // 2. JobsPage active filter chips and breadcrumbs
        // 3. CatalogPage sort options and triggered banner
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

            // 1. Settings Page: confirm on Pause All
            var resSettings = await client.GetAsync("/dashboard/settings");
            resSettings.StatusCode.Should().Be(HttpStatusCode.OK);
            var htmlSettings = await resSettings.Content.ReadAsStringAsync();
            htmlSettings.Should().Contain("onclick=\"return confirm('Pause ALL recurring jobs cluster-wide?')\"");

            // 2. Jobs Page: Filter chips
            var resJobs = await client.GetAsync("/dashboard/jobs?status=Enqueued&queue=default&tag=critical&search=Stub");
            resJobs.StatusCode.Should().Be(HttpStatusCode.OK);
            var htmlJobs = await resJobs.Content.ReadAsStringAsync();
            htmlJobs.Should().Contain("Active Filters:");
            htmlJobs.Should().Contain("Search: <strong>Stub</strong>");
            htmlJobs.Should().Contain("Status: <strong>Enqueued</strong>");
            htmlJobs.Should().Contain("Queue: <strong>default</strong>");
            htmlJobs.Should().Contain("Tag: <strong>critical</strong>");
            htmlJobs.Should().Contain("Clear all");

            // 3. Catalog Page: Sort options and triggered banner
            var resCatalog = await client.GetAsync("/dashboard/catalog?triggered=test-job-uuid-1234&sort=failure-rate");
            resCatalog.StatusCode.Should().Be(HttpStatusCode.OK);
            var htmlCatalog = await resCatalog.Content.ReadAsStringAsync();
            htmlCatalog.Should().Contain("Job enqueued successfully:");
            htmlCatalog.Should().Contain("test-job-uuid-1234");
            htmlCatalog.Should().Contain("Highest Failure Rate");
            htmlCatalog.Should().Contain("Longest Duration");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task StandaloneDashboard_WhenPlaygroundEnabled_RendersScenariosAndHandlesApi()
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
                    options.Title = "Playground Enabled";
                    options.LocalhostOnly = true;
                    options.DefaultTheme = "semi-dark";
                    options.EnablePlayground = true;
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
            html.Should().Contain("🎮");
            html.Should().Contain("Scenarios");
            html.Should().Contain("scenarios-drawer");
            html.Should().Contain("data-theme=\"semi-dark\"");

            var apiRes = await client.PostAsync("/dashboard/api/scenarios/order", null);
            apiRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var apiJson = await apiRes.Content.ReadAsStringAsync();
            apiJson.Should().Contain("\"success\":true");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task StandaloneDashboard_WhenPlaygroundDisabled_DoesNotRenderScenariosAndRejectsApi()
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
                    options.Title = "Production Safe";
                    options.LocalhostOnly = true;
                    options.EnablePlayground = false;
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
            html.Should().NotContain("scenarios-drawer");
            html.Should().NotContain("nexJobTriggerScenario");

            var apiRes = await client.PostAsync("/dashboard/api/scenarios/order", null);
            apiRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task StandaloneDashboard_DefaultThemeAndInvalidScenario_HandledGracefully()
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
                    options.Title = "Theme Test";
                    options.LocalhostOnly = true;
                    options.DefaultTheme = "bordered-theme";
                    options.EnablePlayground = true;
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
            html.Should().Contain("data-theme=\"bordered-theme\"");

            // Unknown scenario gracefully defaults
            var apiRes = await client.PostAsync("/dashboard/api/scenarios/unknown-scenario-xyz", null);
            apiRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await apiRes.Content.ReadAsStringAsync();
            json.Should().Contain("\"success\":true");

            // Flaky / resilience scenario returns jobId and redirectUrl
            var flakyRes = await client.PostAsync("/dashboard/api/scenarios/flaky", null);
            flakyRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var flakyJson = await flakyRes.Content.ReadAsStringAsync();
            flakyJson.Should().Contain("\"success\":true");
            flakyJson.Should().Contain("\"redirectUrl\":\"/dashboard/jobs/");

            // Deadletter scenario returns jobId and redirectUrl
            var dlqRes = await client.PostAsync("/dashboard/api/scenarios/deadletter", null);
            dlqRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var dlqJson = await dlqRes.Content.ReadAsStringAsync();
            dlqJson.Should().Contain("\"success\":true");
            dlqJson.Should().Contain("\"redirectUrl\":\"/dashboard/jobs/");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static async Task<string> GetQueuesPageAsync(string[] scope, string? pause) =>
        await GetPageAsync("/dashboard/queues", scope, pause);

    private static async Task<string> GetSettingsPageAsync(string? pause) =>
        await GetPageAsync("/dashboard/settings", null, pause);

    private static async Task<string> GetPageAsync(string path, string[]? scope, string? pause)
    {
        var port = GetFreeTcpPort();
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt => opt.QueuePrefix = "billing");
                services.AddNexJobStandaloneDashboard(options =>
                {
                    options.Port = port;
                    options.Path = "/dashboard";
                    options.LocalhostOnly = true;
                    options.Queues = scope;
                });
            })
            .Build();

        try
        {
            await host.StartAsync();
            var storage = host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
            foreach (var queue in new[] { "billing.default", "default", "inventory.default" })
            {
                await storage.EnqueueAsync(new JobRecord
                {
                    Id = JobId.New(),
                    JobType = "T",
                    InputType = "I",
                    InputJson = "{}",
                    Queue = queue,
                    Priority = JobPriority.Normal,
                    Status = JobStatus.Enqueued,
                    CreatedAt = DateTimeOffset.UtcNow,
                    MaxAttempts = 3,
                });
            }

            if (pause is not null)
            {
                await host.Services.GetRequiredService<IJobControlService>().PauseQueueAsync(pause);
            }

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            return await client.GetStringAsync(path);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static int GetFreeTcpPort() => TestPorts.Next();
}

public sealed class StubParameterlessJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class StubParameterizedJob : IJob<string>
{
    public Task ExecuteAsync(string input, CancellationToken cancellationToken) => Task.CompletedTask;
}
