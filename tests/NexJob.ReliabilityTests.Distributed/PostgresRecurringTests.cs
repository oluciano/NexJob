using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexJob;
using NexJob.Postgres;
using Xunit;

namespace NexJob.ReliabilityTests.Distributed;

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

        var servicesNode1 = new ServiceCollection();
        Storage()(servicesNode1);
        servicesNode1.AddNexJob(opt => {
            opt.Workers = 1;
            opt.PollingInterval = pollingInterval;
            opt.AddRecurringJob<SuccessJob>(jobId, cron);
        });
        servicesNode1.AddTransient<SuccessJob>(_ => new SuccessJob(() => { }, null!));
        var host1 = servicesNode1.BuildServiceProvider().GetRequiredService<IHost>();

        var servicesNode2 = new ServiceCollection();
        Storage()(servicesNode2);
        servicesNode2.AddNexJob(opt => {
            opt.Workers = 1;
            opt.PollingInterval = pollingInterval;
            opt.AddRecurringJob<SuccessJob>(jobId, cron);
        });
        servicesNode2.AddTransient<SuccessJob>(_ => new SuccessJob(() => { }, null!));
        var host2 = servicesNode2.BuildServiceProvider().GetRequiredService<IHost>();

        // ─── Act ───────────────────────────────────────────────────────────
        // Start both hosts simultaneously
        await Task.WhenAll(host1.StartAsync(), host2.StartAsync());

        // Wait for at least one or two occurrences. 
        // With a 1s cron, 5 seconds is enough to catch multiple cycles.
        await Task.Delay(5000);

        await Task.WhenAll(host1.StopAsync(), host2.StopAsync());

        // ─── Assert ────────────────────────────────────────────────────────
        var storage = host1.Services.GetRequiredService<Storage.IStorageProvider>();
        
        // We expect multiple jobs to have been created (since cron is 1s and we waited 5s),
        // but for ANY given second, we should NOT have duplicates.
        // A simple way to check if the lock worked:
        // If the lock fails, we'd likely see 2x jobs for the same second.
        // Since SuccessJob is fast, most will be Succeeded.
        
        var filter = new JobFilter { RecurringJobId = jobId };
        var page = await storage.GetJobsAsync(filter, page: 1, pageSize: 100);
        
        page.TotalCount.Should().BeGreaterThan(0, "at least one occurrence should have fired");
        
        // Verifying by idempotency key (which NexJob uses: "recurring:{id}:{timestamp}")
        // but also the lock ensures only one node even ATTEMPTS the enqueue.
        var groups = page.Items.GroupBy(j => j.IdempotencyKey);
        foreach (var group in groups)
        {
            group.Count().Should().Be(1, $"Occurrence with key {group.Key} should only exist once across all nodes.");
        }
    }
}
