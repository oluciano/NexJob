using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob;
using NexJob.Storage;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Crash recovery scenarios shared by every database provider, through the real dispatcher and orphan watcher. A job left
/// in <c>Processing</c> by a node that died is what a crash leaves behind: the host that took it will never finish it.
/// The state is recreated by taking the job straight from storage and never committing it. Each test uses its own queue.
/// </summary>
public abstract class OrphanScenarios : DistributedReliabilityTestBase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Gets the action that registers the storage under test.</summary>
    /// <returns>The registration action.</returns>
    protected abstract Action<IServiceCollection> Storage();

    [Fact]
    public async Task OrphanedJob_IsRequeuedByTheWatcherAndRunsOnce_ItCostOneAttempt()
    {
        // N1 (Positive)
        var queue = NewQueue();
        var log = new ExecutionLog();
        var orphan = await LeaveOrphanAsync(queue, log, maxAttempts: 3);

        using var host = BuildHost(Storage(), s => Register(s, log), workers: 1, queues: [queue], heartbeatTimeout: HeartbeatTimeout);
        await host.StartAsync();

        (await WaitForJobStatus(host, orphan, JobStatus.Succeeded, Timeout)).Should().NotBeNull("the watcher gives the job back and it runs");
        await Task.Delay(Grace);

        log.Count($"done:{orphan.Value}").Should().Be(1, "the job runs exactly once after being recovered");
        var stored = await host.Services.GetRequiredService<IDashboardStorage>().GetJobByIdAsync(orphan);
        stored!.Attempts.Should().Be(2, "the attempt taken by the node that died is not given back: it may have partly run");

        await host.StopAsync();
    }

    [Fact]
    public async Task OrphanedJobOnItsLastAttempt_IsFailed_AndNeverRuns()
    {
        // N2 (Negative): a job that already used all its attempts is not requeued forever.
        var queue = NewQueue();
        var log = new ExecutionLog();
        var orphan = await LeaveOrphanAsync(queue, log, maxAttempts: 1);

        using var host = BuildHost(Storage(), s => Register(s, log), workers: 1, queues: [queue], heartbeatTimeout: HeartbeatTimeout, maxAttempts: 1);
        await host.StartAsync();

        (await WaitForJobStatus(host, orphan, JobStatus.Failed, Timeout)).Should().NotBeNull("with no attempts left the job fails");
        await Task.Delay(Grace);

        log.Count($"done:{orphan.Value}").Should().Be(0, "it must not run again");
        await host.StopAsync();
    }

    [Fact]
    public async Task OrphanedJobOnItsLastAttempt_CallsTheDeadLetterHandlerExactlyOnce_WithAnOrphanedJobException()
    {
        // N1 (Positive, issue #340): a job that kills its node every time must reach the operator's dead-letter handler.
        var queue = NewQueue();
        var log = new ExecutionLog();
        var recorder = new DeadLetterRecorder();
        var orphan = await LeaveOrphanAsync(queue, log, maxAttempts: 1);

        using var host = BuildHost(
            Storage(),
            s =>
            {
                Register(s, log);
                s.AddSingleton(recorder);
                s.AddTransient<IDeadLetterHandler<StepJob>, RecordingDeadLetterHandler<StepJob>>();
            },
            workers: 1,
            queues: [queue],
            heartbeatTimeout: HeartbeatTimeout,
            maxAttempts: 1);
        await host.StartAsync();

        (await WaitUntil(() => Task.FromResult(recorder.InvocationCount > 0), Timeout)).Should().BeTrue("the handler is called when the watcher fails the job");
        await Task.Delay(Grace + HeartbeatTimeout + HeartbeatTimeout); // more watcher cycles: it must not be called again

        recorder.InvocationCount.Should().Be(1);
        recorder.LastFailedJob!.Id.Should().Be(orphan);
        recorder.LastException.Should().BeOfType<OrphanedJobException>();
        await host.StopAsync();
    }

    [Fact]
    public async Task OrphanedJobWithAttemptsLeft_IsRequeued_AndTheDeadLetterHandlerIsNotCalled()
    {
        // N2 (Negative): giving the job back is not a failure.
        var queue = NewQueue();
        var log = new ExecutionLog();
        var recorder = new DeadLetterRecorder();
        var orphan = await LeaveOrphanAsync(queue, log, maxAttempts: 3);

        using var host = BuildHost(
            Storage(),
            s =>
            {
                Register(s, log);
                s.AddSingleton(recorder);
                s.AddTransient<IDeadLetterHandler<StepJob>, RecordingDeadLetterHandler<StepJob>>();
            },
            workers: 1,
            queues: [queue],
            heartbeatTimeout: HeartbeatTimeout);
        await host.StartAsync();

        (await WaitForJobStatus(host, orphan, JobStatus.Succeeded, Timeout)).Should().NotBeNull();

        recorder.InvocationCount.Should().Be(0);
        await host.StopAsync();
    }

    [Fact]
    public async Task OrphanedJobsOnTheirLastAttempt_WithAThrowingDeadLetterHandler_StillFailAndTheWatcherKeepsRunning()
    {
        // N3 (Invalid): a handler that throws never stops the watcher or the other jobs found in the same scan.
        var queue = NewQueue();
        var log = new ExecutionLog();
        var first = await LeaveOrphanAsync(queue, log, maxAttempts: 1);
        var second = await LeaveOrphanAsync(queue, log, maxAttempts: 1);

        using var host = BuildHost(
            Storage(),
            s =>
            {
                Register(s, log);
                s.AddTransient<IDeadLetterHandler<StepJob>, ThrowingDeadLetterHandler<StepJob>>();
            },
            workers: 1,
            queues: [queue],
            heartbeatTimeout: HeartbeatTimeout,
            maxAttempts: 1);
        var stopped = false;
        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => stopped = true);
        await host.StartAsync();

        (await WaitForJobStatus(host, first, JobStatus.Failed, Timeout)).Should().NotBeNull();
        (await WaitForJobStatus(host, second, JobStatus.Failed, Timeout)).Should().NotBeNull();
        await Task.Delay(HeartbeatTimeout + HeartbeatTimeout);

        stopped.Should().BeFalse();
        await host.StopAsync();
    }

    [Fact]
    public async Task LiveJob_IsNotMistakenForAnOrphan_WhileItsHeartbeatIsFresh()
    {
        // N2 (Negative): a slow job on a healthy node must not be requeued under it.
        var queue = NewQueue();
        var log = new ExecutionLog();
        var gate = new ParentGate();
        using var host = BuildHost(
            Storage(),
            s =>
            {
                s.AddSingleton(log);
                s.AddSingleton(gate);
                s.AddTransient<GatedParentJob>();
            },
            workers: 2, // a second free worker, so a job wrongly given back to the queue would be started again
            queues: [queue],
            heartbeatTimeout: HeartbeatTimeout,
            heartbeatInterval: TimeSpan.FromMilliseconds(200));
        await host.StartAsync();
        var jobId = await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<GatedParentJob>(queue: queue);
        (await WaitUntil(() => Task.FromResult(log.Count($"start:{jobId.Value}") == 1), Timeout)).Should().BeTrue();

        await Task.Delay(TimeSpan.FromSeconds(3.5)); // several watcher cycles with the job still running
        gate.OpenToSucceed();

        (await WaitForJobStatus(host, jobId, JobStatus.Succeeded, Timeout)).Should().NotBeNull();
        log.Count($"start:{jobId.Value}").Should().Be(1, "a job whose node is alive is never started a second time");

        await host.StopAsync();
    }

    [Fact]
    public async Task Watcher_OnAnEmptyQueue_DoesNothingAndKeepsRunning()
    {
        // N3 (Boundary)
        using var host = BuildHost(Storage(), s => Register(s, new ExecutionLog()), workers: 1, queues: [NewQueue()], heartbeatTimeout: HeartbeatTimeout);
        var stopped = false;
        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => stopped = true);
        await host.StartAsync();

        await Task.Delay(TimeSpan.FromSeconds(2.5));

        stopped.Should().BeFalse();
        await host.StopAsync();
    }

    [Fact]
    public async Task RecurringLock_LeftByACrashedNode_IsTakenOverAfterItsTtl()
    {
        // The lock of a node that died is never released: its time to live has to free it.
        using var host = BuildHost(Storage(), s => Register(s, new ExecutionLog()), workers: 1);
        var storage = host.Services.GetRequiredService<IRecurringStorage>();
        var id = $"crashed-{Guid.NewGuid():N}";

        (await storage.TryAcquireRecurringJobLockAsync(id, TimeSpan.FromSeconds(1))).Should().BeTrue("the crashed node took the lock");
        (await storage.TryAcquireRecurringJobLockAsync(id, TimeSpan.FromSeconds(1))).Should().BeFalse("while the lock is alive nobody else gets it");

        var taken = await WaitUntil(
            async () => await storage.TryAcquireRecurringJobLockAsync(id, TimeSpan.FromSeconds(30)),
            TimeSpan.FromSeconds(10));

        taken.Should().BeTrue("after its TTL the lock of a dead node is free");
    }

    private static string NewQueue() => $"orphan-{Guid.NewGuid():N}";

    private static void Register(IServiceCollection services, ExecutionLog log)
    {
        services.AddSingleton(log);
        services.AddTransient<StepJob>();
    }

    // Enqueues a job and takes it straight from storage without ever committing it, then waits until its heartbeat is older
    // than the timeout the recovering host will use.
    private async Task<JobId> LeaveOrphanAsync(string queue, ExecutionLog log, int maxAttempts)
    {
        using var seed = BuildHost(Storage(), s => Register(s, log), workers: 1, queues: [queue], maxAttempts: maxAttempts);
        var jobId = await seed.Services.GetRequiredService<IScheduler>().EnqueueAsync<StepJob>(queue: queue);
        var taken = await seed.Services.GetRequiredService<IStorageProvider>().FetchNextAsync([queue]);
        taken.Should().NotBeNull();
        taken!.Attempts.Should().Be(1);
        await Task.Delay(HeartbeatTimeout + TimeSpan.FromMilliseconds(500));
        return jobId;
    }
}
