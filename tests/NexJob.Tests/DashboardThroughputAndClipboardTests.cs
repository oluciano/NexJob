using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Dashboard.Standalone;
using NexJob.Storage;
using Xunit;

namespace NexJob.Tests;

public sealed class DashboardThroughputAndClipboardTests
{
    private static readonly Regex BarRegex = new(@"class=""bar(?: [^""]*)?""", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    [Fact]
    public async Task Overview_RendersClean24hThroughputCardWithNormalizedBars()
    {
        // N1 (Positive): Overview page renders the clean 24h Hourly Trend chart with 24 evenly spaced bars
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

        await host.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            var res = await client.GetAsync("/dashboard");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var html = await res.Content.ReadAsStringAsync();

            // Clean 24h Hourly Trend card
            html.Should().Contain("Throughput — last 24h");
            html.Should().Contain("chart-bars");
            html.Should().Contain("Total: <strong");
            var barMatches = BarRegex.Matches(html);
            barMatches.Count.Should().Be(24);

            // Verified absence of experimental ECG canvas / vitals-card
            html.Should().NotContain("ecg-canvas");
            html.Should().NotContain("vitals-card");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Overview_WithSingleHourlyThroughput_NormalizesTo24HoursWithoutBlowout()
    {
        // N2 (Negative / Sparse data): Storage with only a single recorded hour does not cause 1 giant blowout block
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

        await host.StartAsync();
        try
        {
            // Enqueue 1 job so exactly 1 record is created
            using (var scope = host.Services.CreateScope())
            {
                var scheduler = scope.ServiceProvider.GetRequiredService<IScheduler>();
                await scheduler.EnqueueAsync<SampleJob>(queue: "test-queue");
            }

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            var res = await client.GetAsync("/dashboard");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var html = await res.Content.ReadAsStringAsync();

            // Must have exactly 24 bars rendered, never a single 100% wide bar
            var barMatches = BarRegex.Matches(html);
            barMatches.Count.Should().Be(24);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task JobDetail_RendersCleanCopyButtonWithoutStyleLeakage()
    {
        // N3 (Boundary / Copy UX): Job detail page renders clean copy button with nexJobCopyCode
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

        await host.StartAsync();
        try
        {
            JobId jobId;
            using (var scope = host.Services.CreateScope())
            {
                var scheduler = scope.ServiceProvider.GetRequiredService<IScheduler>();
                jobId = await scheduler.EnqueueAsync<SampleJobWithPayload, SamplePayload>(
                    new SamplePayload("Order-999", 42),
                    queue: "default");
            }

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            var res = await client.GetAsync($"/dashboard/jobs/{jobId.Value}");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var html = await res.Content.ReadAsStringAsync();

            // Payload copy button
            html.Should().Contain("payload.json");
            html.Should().Contain("nexJobCopyCode(this)");
            html.Should().Contain("📋 Copy JSON");

            // Global plain-text copy sanitizer
            html.Should().Contain("e.clipboardData.setData('text/plain', plainText)");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task OverviewAndServers_RendersCleanServerIdWithoutGiantGuid()
    {
        // Positive: Composite server IDs (Host:PID:Guid) are rendered cleanly as Host:PID #shortGuid with full ID in title
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

        await host.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

            // 1. Overview page
            var overviewRes = await client.GetAsync("/dashboard");
            overviewRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var overviewHtml = await overviewRes.Content.ReadAsStringAsync();

            // MachineName:PID should be present
            overviewHtml.Should().Contain($"{Environment.MachineName}:{Environment.ProcessId}");
            // Muted short GUID prefix should be present
            overviewHtml.Should().Contain("<span style=\"font-size:11px;color:var(--text-tertiary);font-weight:400\">#");
            // Mini radial gauges present on Overview
            overviewHtml.Should().Contain("mini-gauge-wrap");
            overviewHtml.Should().Contain("<span class=\"mini-gauge-title\">CPU</span>");
            overviewHtml.Should().Contain("<span class=\"mini-gauge-title\">RAM</span>");

            // Links to official documentation site and clean sidebar footer
            overviewHtml.Should().Contain("https://oluciano.github.io/NexJob/");
            overviewHtml.Should().Contain("<div class=\"sidebar-footer\">");
            overviewHtml.Should().NotContain("NexJob Core v");

            // 2. Servers page
            var serversRes = await client.GetAsync("/dashboard/servers");
            serversRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var serversHtml = await serversRes.Content.ReadAsStringAsync();

            serversHtml.Should().Contain($"{Environment.MachineName}:{Environment.ProcessId}");
            serversHtml.Should().Contain("<span style=\"font-size:11px;color:var(--text-tertiary);font-weight:400\">#");
            serversHtml.Should().Contain("<th style=\"width:15%\">CPU</th>");
            serversHtml.Should().Contain("<th style=\"width:15%\">Memory</th>");
            serversHtml.Should().Contain("mini-gauge-wrap");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static int GetFreeTcpPort() => TestPorts.Next();

    private sealed class SampleJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed record SamplePayload(string Name, int Quantity);

    private sealed class SampleJobWithPayload : IJob<SamplePayload>
    {
        public Task ExecuteAsync(SamplePayload input, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
