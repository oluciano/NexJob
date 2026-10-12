using System.Text.Json;
using Eventbus.V1;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Tests;
using Xunit;

namespace NexJob.Trigger.Salesforce.IntegrationTests;

public sealed class SalesforceTriggerIntegrationTests : IAsyncLifetime
{
    private readonly MockSalesforceServer _server = new();

    public async Task InitializeAsync()
    {
        await _server.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
    }

    /// <summary>N1 (#481): the job the trigger enqueues actually runs, and receives the decoded event as JSON text.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task SalesforceTrigger_EndToEnd_JobRunsAndReceivesThePayloadAsJsonText()
    {
        var replayDirectory = Path.Combine(Path.GetTempPath(), $"nexjob-replay-{Guid.NewGuid():N}");
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(o =>
                {
                    o.Queues = ["salesforce-run-queue"];
                    o.PollingInterval = TimeSpan.FromMilliseconds(50);
                    o.MaxAttempts = 1;
                });
                services.AddSingleton(received);
                services.AddSalesforceTrigger<RecordingJob>(options =>
                {
                    options.Topic = "/data/OrderChangeEvent";
                    options.ReplayStoreDirectory = replayDirectory;
                    options.TargetQueue = "salesforce-run-queue";
                    options.ClientId = "test-client-id";
                    options.ClientSecret = "test-client-secret";
                    options.AuthEndpoint = $"{_server.OAuthAddress}/services/oauth2/token";
                    options.PubSubEndpoint = _server.GrpcAddress;
                });
            })
            .Build();

        await host.StartAsync();
        var text = await received.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await host.StopAsync();

        using var document = JsonDocument.Parse(text);
        document.RootElement.GetProperty("OrderId").GetString().Should().Be("ORD-INTEG-999");
        Directory.Delete(replayDirectory, recursive: true);
    }

    /// <summary>N1 (#482): with the default queue configuration, nothing to list in Queues, the job still runs.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task SalesforceTrigger_WithDefaultQueueConfiguration_JobRuns()
    {
        var replayDirectory = Path.Combine(Path.GetTempPath(), $"nexjob-replay-{Guid.NewGuid():N}");
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(o =>
                {
                    o.PollingInterval = TimeSpan.FromMilliseconds(50);
                    o.MaxAttempts = 1;
                });
                services.AddSingleton(received);
                services.AddSalesforceTrigger<RecordingJob>(options =>
                {
                    options.Topic = "/data/OrderChangeEvent";
                    options.ReplayStoreDirectory = replayDirectory;
                    options.ClientId = "test-client-id";
                    options.ClientSecret = "test-client-secret";
                    options.AuthEndpoint = $"{_server.OAuthAddress}/services/oauth2/token";
                    options.PubSubEndpoint = _server.GrpcAddress;
                });
            })
            .Build();

        await host.StartAsync();
        var text = await received.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await host.StopAsync();

        text.Should().Contain("ORD-INTEG-999");
        Directory.Delete(replayDirectory, recursive: true);
    }

    private sealed class RecordingJob(TaskCompletionSource<string> received) : IJob<string>
    {
        public Task ExecuteAsync(string input, CancellationToken cancellationToken)
        {
            received.TrySetResult(input);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task SalesforceTrigger_EndToEnd_ConsumesEventAndPersistsReplayId()
    {
        // Arrange
        // A directory of its own: the default one lives next to the binaries and keeps the replay id of earlier runs.
        var replayDirectory = Path.Combine(Path.GetTempPath(), $"nexjob-replay-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexJob();
        services.AddSalesforceTrigger(options =>
        {
            options.Topic = "/data/OrderChangeEvent";
            options.ReplayStoreDirectory = replayDirectory;
            options.TargetQueue = "salesforce-integration-queue";
            options.ClientId = "test-client-id";
            options.ClientSecret = "test-client-secret";
            options.AuthEndpoint = $"{_server.OAuthAddress}/services/oauth2/token";
            options.PubSubEndpoint = _server.GrpcAddress;
        });

        using var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<IScheduler>();
        var replayStore = provider.GetRequiredService<IReplayIdStore>();
        var trigger = provider.GetRequiredService<IHostedService>();

        // Act
        await trigger.StartAsync(CancellationToken.None);

        JobRecord? job = null;
        await TestWait.UntilAsync(
            async () =>
            {
                var jobs = await scheduler.GetJobsByTagAsync("trigger:salesforce");
                job = jobs.FirstOrDefault(j => j.IdempotencyKey == "evt-integ-001");
                return job is not null;
            },
            because: "the event to become a job");

        // The replay id is committed after the job is enqueued, so seeing the job is not enough.
        await TestWait.UntilAsync(
            async () => await replayStore.GetLastReplayIdAsync("/data/OrderChangeEvent") is not null,
            because: "the replay id to be committed");

        await trigger.StopAsync(CancellationToken.None);

        // Assert
        job.Should().NotBeNull();
        job!.Queue.Should().Be("salesforce-integration-queue");
        job.IdempotencyKey.Should().Be("evt-integ-001");
        job.TraceParent.Should().Be("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");

        // Behavior changed in v6.3: the payload is stored as a JSON string, like the other triggers (#481).
        var payload = JsonSerializer.Deserialize<string>(job.InputJson);
        payload.Should().Contain("\"OrderId\":\"ORD-INTEG-999\"");
        payload.Should().Contain("\"Amount\":199.95");

        // Verify Replay ID store persisted the offset
        var persistedReplayId = await replayStore.GetLastReplayIdAsync("/data/OrderChangeEvent");
        persistedReplayId.Should().Equal(0x10, 0x20, 0x30, 0x40);

        Directory.Delete(replayDirectory, recursive: true);
    }

    /// <summary>
    /// A saved replay id that Salesforce refuses (the fake answers <c>InvalidArgument</c>): the reset policies drop it and
    /// resume from the tip or from the oldest event, and the event still becomes a job.
    /// </summary>
    /// <param name="policy">The fallback policy.</param>
    /// <param name="expectedPreset">The preset the second subscribe must use.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData(ReplayFallbackPolicy.ResetToLatest, ReplayPreset.Latest)]
    [InlineData(ReplayFallbackPolicy.ResetToEarliest, ReplayPreset.Earliest)]
    public async Task RejectedReplayId_WithAResetPolicy_ResumesFromTheTipOrTheEarliestAndKeepsDelivering(ReplayFallbackPolicy policy, ReplayPreset expectedPreset)
    {
        var replayDirectory = Path.Combine(Path.GetTempPath(), $"nexjob-replay-{Guid.NewGuid():N}");
        using var host = BuildHost(replayDirectory, policy, out var replayStore);
        await replayStore().SaveReplayIdAsync("/data/OrderChangeEvent", MockSalesforceServer.RejectedReplayId);
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await TestWait.UntilAsync(
            async () => (await scheduler.GetJobsByTagAsync("trigger:salesforce")).Any(j => j.IdempotencyKey == "evt-integ-001"),
            timeout: TimeSpan.FromSeconds(30),
            because: "the event to become a job after the replay id was reset");
        await host.StopAsync();

        var requests = _server.SubscribeRequests.ToArray();
        requests[0].ReplayId.ToByteArray().Should().Equal(MockSalesforceServer.RejectedReplayId);
        requests[1].ReplayPreset.Should().Be(expectedPreset);
        requests[1].ReplayId.Should().BeEmpty("the refused position was dropped");
        Directory.Delete(replayDirectory, recursive: true);
    }

    /// <summary>
    /// A saved replay id that Salesforce refuses, with the default FailFast: the trigger faults and, with the default host
    /// settings, the whole application is asked to stop. No job is created.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task RejectedReplayId_WithFailFast_StopsTheHostAndCreatesNoJob()
    {
        var replayDirectory = Path.Combine(Path.GetTempPath(), $"nexjob-replay-{Guid.NewGuid():N}");
        using var host = BuildHost(replayDirectory, ReplayFallbackPolicy.FailFast, out var replayStore);
        await replayStore().SaveReplayIdAsync("/data/OrderChangeEvent", MockSalesforceServer.RejectedReplayId);
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        await host.StartAsync();

        await TestWait.UntilAsync(
            () => Task.FromResult(lifetime.ApplicationStopping.IsCancellationRequested),
            timeout: TimeSpan.FromSeconds(30),
            because: "the default host to stop when a background service throws");
        (await host.Services.GetRequiredService<IScheduler>().GetJobsByTagAsync("trigger:salesforce")).Should().BeEmpty();
        Directory.Delete(replayDirectory, recursive: true);
    }

    private IHost BuildHost(string replayDirectory, ReplayFallbackPolicy policy, out Func<IReplayIdStore> replayStore)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(options => options.Workers = 0);
                services.AddSalesforceTrigger(options =>
                {
                    options.Topic = "/data/OrderChangeEvent";
                    options.ReplayStoreDirectory = replayDirectory;
                    options.FallbackPolicy = policy;
                    options.ClientId = "test-client-id";
                    options.ClientSecret = "test-client-secret";
                    options.AuthEndpoint = $"{_server.OAuthAddress}/services/oauth2/token";
                    options.PubSubEndpoint = _server.GrpcAddress;
                });
            })
            .Build();
        replayStore = () => host.Services.GetRequiredService<IReplayIdStore>();
        return host;
    }
}
