using System.Text.Json;
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
}
