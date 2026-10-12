using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Tests;
using NexJob.Trigger.SalesforceStreaming;
using Xunit;

namespace NexJob.Trigger.Salesforce.IntegrationTests;

/// <summary>
/// The Streaming API trigger against <see cref="FakeCometDServer"/>. This checks our reading of the CometD protocol, not
/// Salesforce: the real service was not used.
/// </summary>
public sealed class SalesforceStreamingTriggerIntegrationTests : IAsyncLifetime
{
    private const string Channel = "/event/Order__e";
    private readonly FakeCometDServer _server = new();

    public Task InitializeAsync() => _server.StartAsync();

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task Event_BecomesAJob_AndItsReplayIdIsSaved()
    {
        var store = new InMemoryStreamingReplayIdStore();
        using var host = BuildHost(store, SalesforceStreamingReplayPreset.Latest, customReplayId: null);
        await host.StartAsync();

        _server.Publish(Channel, 101, "{\"OrderId\":\"A-1\"}");

        await WaitForJobsAsync(host, 1);
        await TestWait.UntilAsync(
            async () => await store.GetReplayIdAsync(Channel) == 101,
            because: "the replay id to be saved after the enqueue");
        await host.StopAsync();

        (await store.GetReplayIdAsync(Channel)).Should().Be(101);
    }

    [Theory]
    [InlineData(SalesforceStreamingReplayPreset.Latest, null, -1)]
    [InlineData(SalesforceStreamingReplayPreset.Earliest, null, -2)]
    [InlineData(SalesforceStreamingReplayPreset.Custom, 42L, 42)]
    public async Task WithNothingSaved_TheReplayPresetDecidesWhereToStart(SalesforceStreamingReplayPreset preset, long? custom, long expected)
    {
        using var host = BuildHost(new InMemoryStreamingReplayIdStore(), preset, custom);
        await host.StartAsync();

        await TestWait.UntilAsync(() => Task.FromResult(_server.SubscribedReplayIds.Count >= 1), because: "the trigger to subscribe");
        await host.StopAsync();

        _server.SubscribedReplayIds[0].Should().Be(expected);
    }

    [Fact]
    public async Task CustomPresetWithoutAReplayId_IsRefusedWhenTheHostStarts()
    {
        using var host = BuildHost(new InMemoryStreamingReplayIdStore(), SalesforceStreamingReplayPreset.Custom, customReplayId: null);

        var start = () => host.StartAsync();

        (await start.Should().ThrowAsync<Microsoft.Extensions.Options.OptionsValidationException>())
            .Which.Message.Should().Contain("CustomReplayId must be provided");
        _server.SubscribedReplayIds.Should().BeEmpty();
    }

    [Fact]
    public async Task WithAReplayIdSaved_ItWinsOverThePreset()
    {
        var store = new InMemoryStreamingReplayIdStore();
        await store.SaveReplayIdAsync(Channel, 777);
        using var host = BuildHost(store, SalesforceStreamingReplayPreset.Earliest, customReplayId: null);
        await host.StartAsync();

        await TestWait.UntilAsync(() => Task.FromResult(_server.SubscribedReplayIds.Count >= 1), because: "the trigger to subscribe");
        await host.StopAsync();

        _server.SubscribedReplayIds[0].Should().Be(777);
    }

    [Fact]
    public async Task UnknownClient_TheTriggerHandshakesAgain_ResumesFromTheSavedReplayId_AndKeepsDelivering()
    {
        var store = new InMemoryStreamingReplayIdStore();
        using var host = BuildHost(store, SalesforceStreamingReplayPreset.Latest, customReplayId: null);
        await host.StartAsync();
        _server.Publish(Channel, 101, "{\"OrderId\":\"A-1\"}");
        await WaitForJobsAsync(host, 1);
        await TestWait.UntilAsync(async () => await store.GetReplayIdAsync(Channel) == 101, because: "the first replay id to be saved");

        _server.ExpireAllSessions();
        await TestWait.UntilAsync(() => Task.FromResult(_server.Handshakes >= 2), because: "the trigger to handshake again after 403::Unknown client");
        _server.Publish(Channel, 102, "{\"OrderId\":\"A-2\"}");
        await WaitForJobsAsync(host, 2);
        await host.StopAsync();

        _server.SubscribedReplayIds.Should().Equal(-1, 101);
    }

    [Fact]
    public async Task RefusedHandshakes_TheTriggerKeepsTryingUntilOneWorks()
    {
        _server.RefuseNextHandshakes(2);
        using var host = BuildHost(new InMemoryStreamingReplayIdStore(), SalesforceStreamingReplayPreset.Latest, customReplayId: null);
        await host.StartAsync();

        await TestWait.UntilAsync(() => Task.FromResult(_server.Handshakes >= 1), because: "a handshake to be accepted after the refused ones");
        _server.Publish(Channel, 5, "{\"OrderId\":\"A-5\"}");
        await WaitForJobsAsync(host, 1);
        await host.StopAsync();

        _server.Handshakes.Should().Be(1);
    }

    private static async Task WaitForJobsAsync(IHost host, int count)
    {
        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await TestWait.UntilAsync(
            async () => (await scheduler.GetJobsByTagAsync("trigger:salesforce-streaming")).Count >= count,
            because: $"{count} event(s) to become jobs");
    }

    private IHost BuildHost(IStreamingReplayIdStore store, SalesforceStreamingReplayPreset preset, long? customReplayId) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(options => options.Workers = 0);
                services.AddSalesforceStreamingTrigger(options =>
                {
                    options.Channel = Channel;
                    options.ReplayIdStore = store;
                    options.ReplayPreset = preset;
                    options.CustomReplayId = customReplayId;
                    options.ReconnectDelay = TimeSpan.FromMilliseconds(100);
                    options.Authentication.AuthType = SalesforceStreamingAuthType.OAuth2ClientCredentials;
                    options.Authentication.AuthEndpoint = _server.AuthEndpoint;
                    options.Authentication.ClientId = "client";
                    options.Authentication.ClientSecret = "secret";
                });
            })
            .Build();
}
