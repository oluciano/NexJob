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
        job.InputJson.Should().Contain("\"OrderId\":\"ORD-INTEG-999\"");
        job.InputJson.Should().Contain("\"Amount\":199.95");

        // Verify Replay ID store persisted the offset
        var persistedReplayId = await replayStore.GetLastReplayIdAsync("/data/OrderChangeEvent");
        persistedReplayId.Should().Equal(0x10, 0x20, 0x30, 0x40);

        Directory.Delete(replayDirectory, recursive: true);
    }
}
