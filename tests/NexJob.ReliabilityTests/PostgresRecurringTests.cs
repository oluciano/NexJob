using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob;
using NexJob.Postgres;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Distributed reliability tests for Recurring Jobs on Postgres.
/// Verifies that multiple nodes do not double-enqueue the same recurring occurrence.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class PostgresRecurringTests
    : DistributedReliabilityTestBase,
      IClassFixture<PostgresReliabilityFixture>
{
    private readonly PostgresReliabilityFixture _fixture;

    public PostgresRecurringTests(PostgresReliabilityFixture fixture)
        => _fixture = fixture;

    private Action<IServiceCollection> Storage() =>
        s => s.AddNexJobPostgres(_fixture.ConnectionString);

    [Fact]
    public async Task MultipleNodes_RunningSameRecurringJob_OnlyOneEnqueuesPerOccurrence()
    {
        // ─── Arrange ────────────────────────────────────────────────────────
        // Create 3 nodes all targeting the same Postgres database.
        // Use a very aggressive polling interval to maximize contention.
        var pollingInterval = TimeSpan.FromMilliseconds(50);

        // Use a cron that fires every second to ensure we hit a window during the test.
        var cron = "* * * * * *";
        var jobId = "multi-node-recurring";

        IHost BuildNode() => Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning))
            .ConfigureServices(services =>
            {
                Storage()(services);
                services.AddNexJob(opt =>
                {
                    opt.Workers = 1;
                    opt.PollingInterval = pollingInterval;
                    opt.AddRecurringJob<SuccessJob>(jobId, cron);
                });
                services.AddTransient<SuccessJob>(sp => new SuccessJob(() => { }, sp.GetRequiredService<ILogger<SuccessJob>>()));
            })
            .Build();

        using var host1 = BuildNode();
        using var host2 = BuildNode();

        // ─── Act ───────────────────────────────────────────────────────────
        // Start both hosts simultaneously
        await Task.WhenAll(host1.StartAsync(), host2.StartAsync());

        // Wait for at least one or two occurrences.
        // With a 1s cron, 5 seconds is enough to catch multiple cycles.
        await Task.Delay(5000);

        await Task.WhenAll(host1.StopAsync(), host2.StopAsync());

        // ─── Assert ────────────────────────────────────────────────────────
        var storage = host1.Services.GetRequiredService<Storage.IStorageProvider>();

        // The cron fires once per second and the lock lets only one node enqueue each occurrence.
        // If the lock failed, two nodes would enqueue the same occurrence and two jobs would be created
        // within the same second. (The idempotency key is "recurring:{id}" with no timestamp by design:
        // it only blocks a new instance while the previous one is still active, so it cannot tell
        // occurrences apart.)

        var filter = new JobFilter { RecurringJobId = jobId };
        var page = await storage.GetJobsAsync(filter, page: 1, pageSize: 100);

        page.TotalCount.Should().BeGreaterThan(0, "at least one occurrence should have fired");

        var perSecond = page.Items.GroupBy(j => j.CreatedAt.ToUnixTimeSeconds());
        foreach (var group in perSecond)
        {
            group.Count().Should().Be(1, $"occurrence at second {group.Key} should be enqueued by only one node.");
        }
    }
}
