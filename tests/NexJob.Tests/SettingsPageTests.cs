using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Configuration;
using NexJob.Dashboard.Standalone;
using Xunit;

namespace NexJob.Tests;

public sealed class SettingsPageTests
{
    // N2 (#472): a queue whose execution window has an unreadable time zone must not break the page for the other queues.
    [Theory]
    [InlineData("Not/AZone")]
    [InlineData("")]
    public async Task Settings_WithAnUnreadableWindowTimeZone_StillRenders(string timeZone)
    {
        var port = TestPorts.Next();
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(o =>
                {
                    o.Workers = 0; // a dashboard-only host never evaluates the window, so the start does not reject it
                    o.Queues = ["batch", "other"];
                    o.ConfigureQueue("batch", q => q.ExecutionWindow = new ExecutionWindowSettings
                    {
                        StartTime = new TimeOnly(0, 0),
                        EndTime = new TimeOnly(23, 59),
                        TimeZone = timeZone,
                    });
                });
                services.AddNexJobStandaloneDashboard(o => o.Port = port);
            })
            .Build();
        await host.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync("/dashboard/settings");
            var html = await response.Content.ReadAsStringAsync();

            response.IsSuccessStatusCode.Should().BeTrue();
            html.Should().Contain("batch").And.Contain("other");
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
