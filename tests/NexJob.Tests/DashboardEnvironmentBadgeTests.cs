using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Dashboard;
using NexJob.Dashboard.Standalone;
using Xunit;

namespace NexJob.Tests;

/// <summary>Tests for the dashboard environment badge and tab-title prefix (issue #357).</summary>
public sealed class DashboardEnvironmentBadgeTests
{
    private static string Page() => HtmlShell.Wrap("NexJob", "/dashboard", "overview", "<p>body</p>");

    // ─── N1: Positive ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Production", "env-prod", "[PROD] ")]
    [InlineData("prod", "env-prod", "[PROD] ")]
    [InlineData("PRODUCTION", "env-prod", "[PROD] ")]
    [InlineData("Staging", "env-stage", "[STAGING] ")]
    [InlineData("qa", "env-stage", "[QA] ")]
    [InlineData("Development", "env-dev", "[DEVELOPMENT] ")]
    [InlineData("local", "env-dev", "[LOCAL] ")]
    public void ApplyEnvironment_KnownName_ShowsSemanticBadgeAndTitlePrefix(string name, string cssClass, string titlePrefix)
    {
        var html = HtmlShell.ApplyEnvironment(Page(), name);

        html.Should().Contain($"env-badge {cssClass}", "known environments get a semantic colour");
        html.Should().Contain($"class=\"env-ribbon {cssClass}\"", "narrow screens show the name in a ribbon above the header");
        html.Should().Contain($">{name}</span>", "the badge shows the name as configured");
        html.Should().Contain($"<title>{titlePrefix}NexJob</title>");
    }

    [Fact]
    public async Task StandaloneDashboard_WithEnvironmentName_RendersBadgeAndPrefixedTitle()
    {
        var html = await GetDashboardHtmlAsync("Production");

        html.Should().Contain("env-badge env-prod");
        html.Should().Contain("<title>[PROD] ");
    }

    // ─── N2: Negative ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ApplyEnvironment_NoName_LeavesThePageWithoutBadgeOrPrefix(string? name)
    {
        var html = HtmlShell.ApplyEnvironment(Page(), name);

        html.Should().NotContain("class=\"env-badge", "the badge element is absent (the CSS rule may still be in the page)");
        html.Should().NotContain("nexjob:env-badge", "the placeholder must not leak into the page");
        html.Should().NotContain("nexjob:env-ribbon");
        html.Should().NotContain("class=\"env-ribbon");
        html.Should().Contain("<title>NexJob</title>");
    }

    [Fact]
    public async Task StandaloneDashboard_WithoutEnvironmentName_RendersNoBadge()
    {
        var html = await GetDashboardHtmlAsync(null);

        html.Should().NotContain("class=\"env-badge", "the badge element is absent (the CSS rule may still be in the page)");
        html.Should().Contain("<title>NexJob");
    }

    // ─── N3: Invalid input and boundaries ─────────────────────────────────────

    [Fact]
    public void ApplyEnvironment_NameWithHtml_IsEscapedInBadgeAndTitle()
    {
        var html = HtmlShell.ApplyEnvironment(Page(), "<script>alert(1)</script>");

        html.Should().NotContain("<script>alert(1)</script>");
        html.Should().Contain("&lt;script&gt;");
        html.Should().Contain("env-badge env-other", "an unknown name gets the neutral style");
    }

    [Fact]
    public void ApplyEnvironment_VeryLongName_IsCappedInTheTitle()
    {
        var name = new string('A', 200);

        var html = HtmlShell.ApplyEnvironment(Page(), name);

        html.Should().Contain("env-badge env-other");
        html.Should().NotContain($"<title>[{name}]", "the tab title prefix must stay short");
    }

    [Fact]
    public void ApplyEnvironment_PageWithoutHeaderOrTitle_ReturnsItUnchanged()
    {
        const string fragment = "<html><body>no shell here</body></html>";

        var html = HtmlShell.ApplyEnvironment(fragment, "Production");

        html.Should().Be(fragment);
    }

    // N1 (regression guard for the narrow-screen fix): the shell carries the rules that keep the page inside the viewport.
    [Fact]
    public void Wrap_ShellCss_HasNarrowScreenRulesAndNoFixedMinimumGrids()
    {
        var html = Page();

        html.Should().Contain("@media (max-width: 768px)");
        html.Should().Contain(".env-ribbon ~ .top-header", "the ribbon pushes the fixed header down");
        html.Should().Contain("#overview-grid { grid-template-columns: 1fr !important; }");
        html.Should().NotMatchRegex(@"minmax\(\d+px, ?1fr\)", "a fixed minimum column width overflows narrow screens; use minmax(min(Npx, 100%), 1fr)");
    }

    private static async Task<string> GetDashboardHtmlAsync(string? environmentName)
    {
        var port = TestPorts.Next();
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob();
                services.AddNexJobStandaloneDashboard(o =>
                {
                    o.Port = port;
                    o.EnvironmentName = environmentName;
                });
            })
            .Build();

        try
        {
            await host.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(10) };
            return await client.GetStringAsync("/dashboard");
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
