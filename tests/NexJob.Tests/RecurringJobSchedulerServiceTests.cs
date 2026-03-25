using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NexJob;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests;

public sealed class RecurringJobSchedulerServiceTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    private static RecurringJobSchedulerService MakeService(
        InMemoryStorageProvider storage,
        TimeSpan? pollingInterval = null)
    {
        var options = new NexJobOptions
        {
            PollingInterval = pollingInterval ?? TimeSpan.FromMilliseconds(30),
        };
        return new RecurringJobSchedulerService(
            storage, options, NullLogger<RecurringJobSchedulerService>.Instance);
    }

    private static RecurringJobRecord MakeRecurring(
        string id,
        DateTimeOffset nextExecution,
        RecurringConcurrencyPolicy policy = RecurringConcurrencyPolicy.SkipIfRunning) =>
        new()
        {
            RecurringJobId    = id,
            JobType           = "FakeJob",
            InputType         = "System.String",
            InputJson         = "\"go\"",
            Cron              = "* * * * *",   // every minute
            Queue             = "default",
            NextExecution     = nextExecution,
            CreatedAt         = DateTimeOffset.UtcNow,
            ConcurrencyPolicy = policy,
        };

    // ─── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DueJob_IsEnqueuedOnFirstPollCycle()
    {
        var storage = new InMemoryStorageProvider();
        await storage.UpsertRecurringJobAsync(
            MakeRecurring("r1", DateTimeOffset.UtcNow.AddSeconds(-1)));

        var svc = (IHostedService)MakeService(storage);
        await svc.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await svc.StopAsync(CancellationToken.None);

        var job = await storage.FetchNextAsync(["default"]);
        job.Should().NotBeNull();
        job!.JobType.Should().Be("FakeJob");
    }

    [Fact]
    public async Task DueJob_EnqueuedWithRecurringJobIdSet()
    {
        var storage = new InMemoryStorageProvider();
        await storage.UpsertRecurringJobAsync(
            MakeRecurring("r-link", DateTimeOffset.UtcNow.AddSeconds(-1)));

        var svc = (IHostedService)MakeService(storage);
        await svc.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await svc.StopAsync(CancellationToken.None);

        var job = await storage.FetchNextAsync(["default"]);
        job!.RecurringJobId.Should().Be("r-link");
    }

    [Fact]
    public async Task DueJob_NextExecutionIsAdvancedToFuture()
    {
        var storage = new InMemoryStorageProvider();
        var before = DateTimeOffset.UtcNow;
        await storage.UpsertRecurringJobAsync(
            MakeRecurring("r-next", before.AddSeconds(-1)));

        var svc = (IHostedService)MakeService(storage);
        await svc.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await svc.StopAsync(CancellationToken.None);

        var all = await storage.GetRecurringJobsAsync();
        all.Single(r => r.RecurringJobId == "r-next")
           .NextExecution.Should().BeAfter(before);
    }

    [Fact]
    public async Task NonDueJob_IsNotEnqueued()
    {
        var storage = new InMemoryStorageProvider();
        await storage.UpsertRecurringJobAsync(
            MakeRecurring("r-future", DateTimeOffset.UtcNow.AddHours(1)));

        var svc = (IHostedService)MakeService(storage);
        await svc.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await svc.StopAsync(CancellationToken.None);

        var job = await storage.FetchNextAsync(["default"]);
        job.Should().BeNull("future recurring job must not be enqueued early");
    }

    [Fact]
    public async Task CronFiringWhileJobIsRunning_DoesNotStartSecondInstance()
    {
        // Simulates cron overlap: job is still Processing when the scheduler fires again.
        var storage = new InMemoryStorageProvider();
        await storage.UpsertRecurringJobAsync(
            MakeRecurring("r-overlap", DateTimeOffset.UtcNow.AddSeconds(-1)));

        var svc = (IHostedService)MakeService(storage, pollingInterval: TimeSpan.FromMilliseconds(30));
        await svc.StartAsync(CancellationToken.None);
        await Task.Delay(50); // first cycle enqueues + advances NextExecution

        // Claim the job (simulates worker picking it up — status → Processing)
        var running = await storage.FetchNextAsync(["default"]);
        running.Should().NotBeNull();

        // Force NextExecution back to the past so scheduler fires again while job is "running"
        await storage.SetRecurringJobNextExecutionAsync("r-overlap", DateTimeOffset.UtcNow.AddSeconds(-1));
        await Task.Delay(100); // let scheduler fire a second time

        await svc.StopAsync(CancellationToken.None);

        // There must be no second Enqueued job — idempotency key blocks the duplicate
        var second = await storage.FetchNextAsync(["default"]);
        second.Should().BeNull("second instance must be blocked while first is Processing");
    }

    [Fact]
    public async Task AllowConcurrentPolicy_SpawnsSecondInstanceWhileFirstIsRunning()
    {
        // With AllowConcurrent, no idempotency key — a second instance IS created even
        // when the first job is still Processing.
        var storage = new InMemoryStorageProvider();
        await storage.UpsertRecurringJobAsync(
            MakeRecurring("r-concurrent", DateTimeOffset.UtcNow.AddSeconds(-1),
                RecurringConcurrencyPolicy.AllowConcurrent));

        var svc = (IHostedService)MakeService(storage, pollingInterval: TimeSpan.FromMilliseconds(30));
        await svc.StartAsync(CancellationToken.None);
        await Task.Delay(50); // first cycle enqueues

        // Claim the first job (status → Processing)
        var first = await storage.FetchNextAsync(["default"]);
        first.Should().NotBeNull();

        // Force NextExecution back to the past so scheduler fires again
        await storage.SetRecurringJobNextExecutionAsync("r-concurrent", DateTimeOffset.UtcNow.AddSeconds(-1));
        await Task.Delay(100); // let scheduler fire a second time

        await svc.StopAsync(CancellationToken.None);

        // There MUST be a second enqueued job — no idempotency key with AllowConcurrent
        var second = await storage.FetchNextAsync(["default"]);
        second.Should().NotBeNull("AllowConcurrent must spawn a second instance while first is running");
    }

    [Fact]
    public async Task MultipleJobsDue_AllAreEnqueued()
    {
        var storage = new InMemoryStorageProvider();
        await storage.UpsertRecurringJobAsync(
            MakeRecurring("r-a", DateTimeOffset.UtcNow.AddSeconds(-1)));
        await storage.UpsertRecurringJobAsync(
            MakeRecurring("r-b", DateTimeOffset.UtcNow.AddSeconds(-1)));

        var svc = (IHostedService)MakeService(storage);
        await svc.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await svc.StopAsync(CancellationToken.None);

        var first  = await storage.FetchNextAsync(["default"]);
        var second = await storage.FetchNextAsync(["default"]);
        first.Should().NotBeNull();
        second.Should().NotBeNull();
    }
}
