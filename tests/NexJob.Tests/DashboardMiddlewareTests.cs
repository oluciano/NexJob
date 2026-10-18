using System.Net;
using FluentAssertions;
using Xunit;

namespace NexJob.Tests;

public sealed class DashboardMiddlewareTests
{
    [Theory]
    [InlineData("Sec-Fetch-Site", "same-origin")]
    [InlineData("Sec-Fetch-Site", "none")]
    [InlineData("Origin", "SAME")]
    [InlineData(null, null)]
    public async Task Post_FromTheSameOriginOrANonBrowserClient_PerformsTheAction(string? header, string? value)
    {
        // N1 (Positive, #594): the browser's own forms (same origin) and scripts that send no browser metadata still work.
        var (status, exists) = await DeleteAsync(header, value);

        ((int)status).Should().BeLessThan(400);
        exists.Should().BeFalse();
    }

    [Theory]
    [InlineData("Sec-Fetch-Site", "cross-site")]
    [InlineData("Sec-Fetch-Site", "same-site")]
    [InlineData("Origin", "http://evil.example")]
    [InlineData("Origin", "null")]
    [InlineData("Origin", "OTHERPORT")]
    [InlineData("Origin", "::::")]
    [InlineData("Origin", "javascript:alert(1)")]
    public async Task Post_FromAnotherSiteOrWithAnUntrustedOrigin_IsRejectedAndChangesNothing(string header, string value)
    {
        // N2 + N3 (Negative and Invalid Input, #594): a cross-site POST, or one whose Origin is not an http(s) origin
        // of this host, gets 403 and the job is still there.
        var (status, exists) = await DeleteAsync(header, value);

        status.Should().Be(HttpStatusCode.Forbidden);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task Get_WithACrossSiteHeader_IsStillServed()
    {
        // N3 (Boundary, #594): reading a page is not an action, so only POST is checked.
        var status = await DashboardTestHost.UseAsync<HttpStatusCode>(null, async (client, _) =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/dashboard/jobs");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        });

        status.Should().Be(HttpStatusCode.OK);
    }

    private static Task<(HttpStatusCode Status, bool Exists)> DeleteAsync(string? header, string? value)
    {
        var job = DashboardTestHost.Job("Acme.Jobs.ReportJob", "default", JobStatus.Failed);
        return DashboardTestHost.UseAsync(s => s.EnqueueAsync(job), async (client, storage) =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/dashboard/jobs/{job.Id.Value}/delete")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>()),
            };
            if (header is not null)
            {
                var host = client.BaseAddress!;
                var sent = value switch
                {
                    "SAME" => host.GetLeftPart(UriPartial.Authority),
                    "OTHERPORT" => $"http://localhost:{host.Port + 1}",
                    _ => value!,
                };
                request.Headers.TryAddWithoutValidation(header, sent);
            }

            using var response = await client.SendAsync(request);
            var after = await storage.GetJobByIdAsync(job.Id);
            return (response.StatusCode, after is not null);
        });
    }

    [Theory]
    [InlineData("/dashboard")]
    [InlineData("/dashboard/jobs")]
    [InlineData("/dashboard/queues")]
    [InlineData("/dashboard/catalog")]
    [InlineData("/dashboard/recurring")]
    [InlineData("/dashboard/nope")]
    public async Task Responses_CarryTheSecurityHeaders(string route)
    {
        // N1 + N3 (#595): every response of the dashboard, a page or a 404, forbids framing and sniffing.
        var headers = await HeadersOfAsync(route);

        headers["X-Content-Type-Options"].Should().Be("nosniff");
        headers["X-Frame-Options"].Should().Be("DENY");
        headers["Referrer-Policy"].Should().Be("same-origin");
        var csp = headers["Content-Security-Policy"];
        csp.Should().Contain("frame-ancestors 'none'").And.Contain("object-src 'none'")
            .And.Contain("base-uri 'self'").And.Contain("form-action 'self'").And.Contain("default-src 'self'");
    }

    [Fact]
    public void SecurityHeaders_AlreadySetByTheHost_AreKept()
    {
        // N2 (Negative, #595): a header that the application or a proxy set is not overwritten.
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";

        NexJob.Dashboard.DashboardSecurityHeaders.Apply(context.Response.Headers);

        context.Response.Headers["X-Frame-Options"].ToString().Should().Be("SAMEORIGIN");
        context.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
    }

    private static Task<Dictionary<string, string>> HeadersOfAsync(string route) =>
        DashboardTestHost.UseAsync<Dictionary<string, string>>(null, async (client, _) =>
        {
            using var response = await client.GetAsync(route);
            return response.Headers.Concat(response.Content.Headers)
                .ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);
        });
}
