using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Dashboard.Pages;
using NexJob.Dashboard.Standalone;
using Xunit;

namespace NexJob.Tests;

public sealed class DashboardEmptyStateIconTests
{
    private static readonly Regex PathData = new("<path d=\"(?<d>[^\"]*)\"", RegexOptions.Compiled | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

    [Theory]
    [InlineData("/dashboard/jobs")]
    [InlineData("/dashboard/queues")]
    [InlineData("/dashboard/recurring")]
    [InlineData("/dashboard/listeners")]
    [InlineData("/dashboard/failed")]
    [InlineData("/dashboard/catalog")]
    [InlineData("/dashboard/servers")]
    public async Task EmptyPages_RenderOnlyValidSvgPathData(string route)
    {
        // N1 (Positive): every page path attribute starts with a moveto command.
        var html = await GetAsync(route);

        var paths = PathData.Matches(html).Select(m => m.Groups["d"].Value).ToList();
        paths.Should().NotBeEmpty();
        paths.Should().OnlyContain(d => d.StartsWith('M') || d.StartsWith('m'));
    }

    [Fact]
    public async Task JobDetail_UnknownJobId_ShowsEmptyStateWithValidIcon()
    {
        // N2 (Negative): a job that does not exist renders the empty state with a valid icon.
        var html = await GetAsync($"/dashboard/jobs/{Guid.NewGuid()}");

        html.Should().Contain("Job not found");
        PathData.Matches(html).Select(m => m.Groups["d"].Value)
            .Should().OnlyContain(d => d.StartsWith('M') || d.StartsWith('m'));
    }

    [Fact]
    public void EmptyState_MaliciousPathData_IsHtmlEncoded()
    {
        // N3 (Invalid Input): path data and message can never break out of the attribute.
        var html = HtmlFragments.EmptyState("M0 0\"/><script>alert(1)</script>", "<b>none</b>");

        html.Should().NotContain("<script>");
        html.Should().NotContain("<b>none</b>");
    }

    private static async Task<string> GetAsync(string route)
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
            return await client.GetStringAsync(route);
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
