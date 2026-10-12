using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexJob.Configuration;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Queue policies end to end on a real database: an execution window and a circuit breaker decide whether a fetched
/// queue runs. Shared by every database provider. A subclass only says how to register its storage.
/// </summary>
public abstract class QueuePolicyScenarios : DistributedReliabilityTestBase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

    /// <summary>Gets the action that registers the storage under test.</summary>
    /// <returns>The registration action.</returns>
    protected abstract Action<IServiceCollection> Storage();

    [Fact]
    public async Task ExecutionWindow_JobOnAClosedQueueWaits_JobOnAnOpenQueueRuns()
    {
        // N1 (Positive) and N2 (Negative) in one host: the two queues differ only by their window.
        var open = $"window-open-{Guid.NewGuid():N}";
        var closed = $"window-closed-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<PolicyProbeJob>(),
            queues: [open, closed],
            configureOptions: options =>
            {
                options.ConfigureQueue(open, q => q.ExecutionWindow = new ExecutionWindowSettings
                {
                    StartTime = new TimeOnly(0, 0),
                    EndTime = new TimeOnly(23, 59, 59),
                    TimeZone = "UTC",
                });
                options.ConfigureQueue(closed, q => q.ExecutionWindow = new ExecutionWindowSettings
                {
                    StartTime = TimeOnly.FromDateTime(now.AddHours(2).UtcDateTime),
                    EndTime = TimeOnly.FromDateTime(now.AddHours(3).UtcDateTime),
                    TimeZone = "UTC",
                });
            });
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var openJob = await scheduler.EnqueueAsync<PolicyProbeJob>(queue: open);
        var closedJob = await scheduler.EnqueueAsync<PolicyProbeJob>(queue: closed);

        (await WaitForJobStatus(host, openJob, JobStatus.Succeeded, Timeout)).Should().NotBeNull("the window of the open queue includes now");
        await Task.Delay(Grace);
        var stillWaiting = await host.Services.GetRequiredService<Storage.IStorageProvider>().GetJobByIdAsync(closedJob);
        stillWaiting!.Status.Should().Be(JobStatus.Enqueued, "the window of the closed queue starts hours from now");
        stillWaiting.Attempts.Should().Be(0);
        await host.StopAsync();
    }

    [Fact]
    public async Task CircuitBreaker_AfterConsecutiveFailuresTheQueueStopsRunning_AndRunsAgainOnceItRecovers()
    {
        // N1 (Positive) and N2 (Negative): failures open the breaker, a healthy job waits, then it runs.
        var queue = $"breaker-{Guid.NewGuid():N}";
        using var host = BuildHost(
            Storage(),
            s =>
            {
                s.AddTransient<PolicyProbeJob>();
                s.AddTransient<AlwaysBreaksJob>();
            },
            queues: [queue],
            maxAttempts: 1,
            configureOptions: options => options.ConfigureQueue(queue, q => q.EnableCircuitBreaker(breaker =>
            {
                breaker.ConsecutiveFailuresThreshold = 3;
                breaker.OpenDuration = TimeSpan.FromSeconds(4);
                breaker.RecoveryDuration = TimeSpan.FromSeconds(1);
                breaker.BreakOn<InvalidOperationException>();
            })));
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var failing = new List<JobId>();
        for (var i = 0; i < 3; i++)
        {
            failing.Add(await scheduler.EnqueueAsync<AlwaysBreaksJob>(queue: queue));
        }

        foreach (var id in failing)
        {
            (await WaitForJobStatus(host, id, JobStatus.Failed, Timeout)).Should().NotBeNull("each failing job exhausts its only attempt");
        }

        var healthy = await scheduler.EnqueueAsync<PolicyProbeJob>(queue: queue);
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        var whileOpen = await host.Services.GetRequiredService<Storage.IStorageProvider>().GetJobByIdAsync(healthy);
        whileOpen!.Status.Should().Be(JobStatus.Enqueued, "the breaker is open: the queue is not fetched");

        (await WaitForJobStatus(host, healthy, JobStatus.Succeeded, Timeout)).Should().NotBeNull("the breaker closes after OpenDuration and the recovery window");
        await host.StopAsync();
    }

    [Fact]
    public async Task ExecutionWindow_WithAnUnknownTimeZone_RefusesToStartAndNamesTheQueue()
    {
        // N3 (Invalid input): a window that cannot be read is a configuration error found at start, not a silent closed queue.
        var broken = $"window-broken-{Guid.NewGuid():N}";
        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<PolicyProbeJob>(),
            queues: [broken],
            configureOptions: options => options.ConfigureQueue(broken, q => q.ExecutionWindow = new ExecutionWindowSettings
            {
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59, 59),
                TimeZone = "Mars/Olympus_Mons",
            }));

        var start = () => host.StartAsync();

        var thrown = await start.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Contain(broken).And.Contain("Mars/Olympus_Mons");
    }

    private sealed class PolicyProbeJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class AlwaysBreaksJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("the downstream is down");
    }
}
