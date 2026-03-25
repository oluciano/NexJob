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

    private static RecurringJobRecord MakeRecurring(string id, DateTimeOffset nextExecution) =>
        new()
        {
            RecurringJobId = id,
            JobType        = "FakeJob",
            InputType      = "System.String",
            InputJson      = "\"go\"",
            Cron           = "* * * * *",   // every minute
            Queue          = "default",
            NextExecution  = nextExecution,
            CreatedAt      = DateTimeOffset.UtcNow,
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
