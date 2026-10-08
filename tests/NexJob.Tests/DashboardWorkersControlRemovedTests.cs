using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Configuration;
using NexJob.Dashboard.Standalone;
using Xunit;

namespace NexJob.Tests;

public sealed class DashboardWorkersControlRemovedTests
{
    [Fact]
    public async Task SettingsPage_HasNoWorkersControl()
    {
        // N1: the worker count is a deployment decision, so the dashboard offers no way to change it.
        var (html, _) = await RunAsync(async (client, _) => await client.GetStringAsync("/dashboard/settings"));

        html.Should().Contain("Polling Interval");
        html.Should().NotContain("settings/workers");
        html.Should().NotContain("name=\"workers\"");
        html.Should().NotContain("Runtime override active");
    }

    [Fact]
    public async Task PostWorkers_IsNotHandled_AndStoresNothing()
    {
        // N2: the old endpoint no longer changes the runtime settings.
        var (_, workers) = await RunAsync(async (client, _) =>
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["workers"] = "50", });
            using var response = await client.PostAsync("/dashboard/settings/workers", content);
            return response.StatusCode.ToString();
        });

        workers.Should().BeNull();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("abc")]
    public async Task PostWorkers_InvalidValues_DoNotThrowOrStore(string value)
    {
        // N3 (Invalid Input): garbage never reaches the store and never becomes a server error.
        var (status, workers) = await RunAsync(async (client, _) =>
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["workers"] = value, });
            using var response = await client.PostAsync("/dashboard/settings/workers", content);
            return ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);
        });

        status.Should().NotStartWith("5");
        workers.Should().BeNull();
    }

    [Fact]
    public async Task StoredRuntimeWorkers_IsIgnoredWithoutError()
    {
        // N3: a value saved by an older version stays in storage and is simply not used.
        var (html, workers) = await RunAsync(
            async (client, _) => await client.GetStringAsync("/dashboard/settings"),
            seed: s => s.Workers = 99);

        html.Should().Contain("&quot;Workers&quot;: 10,", "the page reports the configured worker count");
        html.Should().NotContain("&quot;Workers&quot;: 99");
        workers.Should().Be(99, "the stored value is left alone, only ignored");
    }

    private static async Task<(string Result, int? StoredWorkers)> RunAsync(
        Func<HttpClient, IServiceProvider, Task<string>> act,
        Action<RuntimeSettings>? seed = null)
    {
        var port = GetFreeTcpPort();
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob();
                services.AddNexJobStandaloneDashboard(o => o.Port = port);
            })
            .Build();

        try
        {
            await host.StartAsync();
            var store = host.Services.GetRequiredService<IRuntimeSettingsStore>();
            if (seed is not null)
            {
                var current = await store.GetAsync();
                seed(current);
                await store.SaveAsync(current);
            }

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            var result = await act(client, host.Services);
            return (result, (await store.GetAsync()).Workers);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static int GetFreeTcpPort() => TestPorts.Next();
}
