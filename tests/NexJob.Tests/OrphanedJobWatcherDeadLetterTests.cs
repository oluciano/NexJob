using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// The orphan watcher hands every job it failed because of a crash to the dead-letter dispatcher (issue #340).
/// </summary>
public sealed class OrphanedJobWatcherDeadLetterTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Watcher_JobExhaustedByCrash_IsDispatchedOnceWithAnOrphanedJobException()
    {
        // N1 (Positive)
        var (storage, dispatcher, watcher) = Build();
        var id = await LeaveOrphanAsync(storage, maxAttempts: 1);

        await watcher.StartAsync(CancellationToken.None);
        await WaitAsync(() => dispatcher.Calls.Count >= 1);
        await Task.Delay(300); // more watcher cycles
        await watcher.StopAsync(CancellationToken.None);

        var call = dispatcher.Calls.Should().ContainSingle().Subject;
        call.Job.Id.Should().Be(id);
        call.Exception.Should().BeOfType<OrphanedJobException>().Which.JobId.Should().Be(id);
    }

    [Fact]
    public async Task Watcher_JobWithAttemptsLeft_IsRequeuedAndNotDispatched()
    {
        // N2 (Negative)
        var (storage, dispatcher, watcher) = Build();
        var id = await LeaveOrphanAsync(storage, maxAttempts: 3);

        await watcher.StartAsync(CancellationToken.None);
        await WaitAsync(() => storage.GetJobByIdAsync(id).Result!.Status == JobStatus.Enqueued);
        await Task.Delay(200);
        await watcher.StopAsync(CancellationToken.None);

        dispatcher.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Watcher_WhenTheDispatcherThrows_StillDispatchesTheNextJobAndKeepsRunning()
    {
        // N3 (Invalid): dead-letter handling must never stop the watcher.
        var (storage, dispatcher, watcher) = Build();
        dispatcher.ThrowOnFirstCall = true;
        var first = await LeaveOrphanAsync(storage, maxAttempts: 1);
        var second = await LeaveOrphanAsync(storage, maxAttempts: 1);

        await watcher.StartAsync(CancellationToken.None);
        await WaitAsync(() => dispatcher.Calls.Count >= 2);
        await watcher.StopAsync(CancellationToken.None);

        dispatcher.Calls.Select(c => c.Job.Id).Should().BeEquivalentTo(new[] { first, second });
    }

    [Fact]
    public async Task Watcher_WhenTheStorageDoesNotOverrideTheReportingMethod_DoesNotDispatchAnything()
    {
        // N3 (Boundary): a custom provider written before #340 keeps working, without dead-letter handling.
        var dispatcher = new RecordingDispatcher();
        var requeued = 0;
        var legacy = new Mock<IJobStorage>();
        legacy.Setup(s => s.RequeueOrphanedJobsAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref requeued))
            .Returns(Task.CompletedTask);
        var watcher = new OrphanedJobWatcherService(legacy.Object, new InMemoryStorageProvider(), dispatcher, Options(), NullLogger<OrphanedJobWatcherService>.Instance);

        await watcher.StartAsync(CancellationToken.None);
        await WaitAsync(() => Volatile.Read(ref requeued) > 0);
        await watcher.StopAsync(CancellationToken.None);

        dispatcher.Calls.Should().BeEmpty();
    }

    private static NexJobOptions Options() => new() { HeartbeatTimeout = TimeSpan.FromMilliseconds(100) };

    private static (InMemoryStorageProvider Storage, RecordingDispatcher Dispatcher, OrphanedJobWatcherService Watcher) Build()
    {
        var storage = new InMemoryStorageProvider();
        var dispatcher = new RecordingDispatcher();
        var watcher = new OrphanedJobWatcherService(storage, storage, dispatcher, Options(), NullLogger<OrphanedJobWatcherService>.Instance);
        return (storage, dispatcher, watcher);
    }

    private static async Task<JobId> LeaveOrphanAsync(InMemoryStorageProvider storage, int maxAttempts)
    {
        var job = new JobRecord
        {
            Id = JobId.New(),
            JobType = typeof(OrphanedJobWatcherDeadLetterTests).AssemblyQualifiedName!,
            Queue = "default",
            Priority = JobPriority.Normal,
            Status = JobStatus.Enqueued,
            CreatedAt = DateTimeOffset.UtcNow,
            MaxAttempts = maxAttempts,
        };
        await storage.EnqueueAsync(job);
        (await storage.FetchNextAsync(["default"])).Should().NotBeNull();
        await Task.Delay(150); // older than the heartbeat timeout
        return job.Id;
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("condition not reached");
            }

            await Task.Delay(25);
        }
    }

    private sealed class RecordingDispatcher : IDeadLetterDispatcher
    {
        private int _calls;

        public List<(JobRecord Job, Exception Exception)> Calls { get; } = [];

        public bool ThrowOnFirstCall { get; set; }

        public Task DispatchAsync(JobRecord job, Exception lastException, CancellationToken ct = default)
        {
            lock (Calls)
            {
                Calls.Add((job, lastException));
            }

            if (ThrowOnFirstCall && Interlocked.Increment(ref _calls) == 1)
            {
                throw new InvalidOperationException("dispatcher failure");
            }

            return Task.CompletedTask;
        }
    }
}
