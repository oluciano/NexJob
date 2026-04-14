using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Tests for <see cref="JobStatus.Expired"/> deadline enforcement.
/// Covers enqueue-time deadline calculation, expiration checks, and storage persistence.
/// </summary>
public sealed class DeadlineTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    private static IHost BuildHost(
        Action<IServiceCollection> registerJobs,
        int workers = 1)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = workers;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(50);
                });
                registerJobs(services);
            })
            .Build();
    }

    // ─── deadline calculation ─────────────────────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_WithDeadline_CalculatesExpiresAt()
    {
        var storage = new InMemoryStorageProvider();
        var scheduler = new DefaultScheduler(storage, storage, storage, new NexJobOptions(), new JobWakeUpChannel());

        var deadline = TimeSpan.FromSeconds(10);
        var beforeEnqueue = DateTimeOffset.UtcNow;

        await scheduler.EnqueueAsync<SimpleJob>(deadlineAfter: deadline);

        var fetched = await storage.FetchNextAsync(["default"]);

        fetched!.ExpiresAt.Should().NotBeNull();
        var expectedDeadline = beforeEnqueue + deadline;
        (fetched.ExpiresAt!.Value - expectedDeadline).Duration().Should().BeLessThan(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task EnqueueAsync_WithoutDeadline_HasNullExpiresAt()
    {
        var storage = new InMemoryStorageProvider();
        var scheduler = new DefaultScheduler(storage, storage, storage, new NexJobOptions(), new JobWakeUpChannel());

        await scheduler.EnqueueAsync<SimpleJob>();

        var fetched = await storage.FetchNextAsync(["default"]);

        fetched!.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task EnqueueAsync_WithInput_CalculatesExpiresAt()
    {
        var storage = new InMemoryStorageProvider();
        var scheduler = new DefaultScheduler(storage, storage, storage, new NexJobOptions(), new JobWakeUpChannel());

        var deadline = TimeSpan.FromSeconds(5);

        await scheduler.EnqueueAsync<SimpleInputJob, string>("input", deadlineAfter: deadline);

        var fetched = await storage.FetchNextAsync(["default"]);

        fetched!.ExpiresAt.Should().NotBeNull();
        (fetched.ExpiresAt!.Value - fetched.CreatedAt).Should().BeCloseTo(deadline, TimeSpan.FromMilliseconds(100));
    }

    // ─── expiration enforcement ──────────────────────────────────────────────

    [Fact]
    public async Task ExpiredJob_IsNotExecuted()
    {
        var executed = false;

        using var host = BuildHost(s => s.AddTransient(_ => new MockJob(() => executed = true)));
        await host.StartAsync();

        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        var jobId = JobId.New();

        // Enqueue a job that already expired
        await storage.EnqueueAsync(new JobRecord
        {
            Id = jobId,
            JobType = typeof(MockJob).AssemblyQualifiedName!,
            InputType = typeof(NoInput).AssemblyQualifiedName!,
            InputJson = "{}",
            Queue = "default",
            Priority = JobPriority.Normal,
            Status = JobStatus.Enqueued,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1), // Already expired
            CreatedAt = DateTimeOffset.UtcNow,
            MaxAttempts = 10,
        });

        // Give the dispatcher time to process
        await Task.Delay(500);

        executed.Should().BeFalse("expired job must not execute");
        var job = await storage.GetJobByIdAsync(jobId);
        job!.Status.Should().Be(JobStatus.Expired);

        await host.StopAsync();
    }

    [Fact]
    public async Task ValidJob_WithDeadlineInFuture_Executes()
    {
        var tcs = new TaskCompletionSource<bool>();

        using var host = BuildHost(s => s.AddTransient(_ => new QuickSuccessJob(tcs)));
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();

        // Enqueue with deadline in the future
        await scheduler.EnqueueAsync<QuickSuccessJob, QuickInput>(
            new(),
            deadlineAfter: TimeSpan.FromSeconds(5));

        var completed = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        completed.Should().BeTrue("job with valid deadline should execute");

        await host.StopAsync();
    }

    [Fact]
    public async Task ExpiredJob_StatusPersisted()
    {
        var storage = new InMemoryStorageProvider();
        var jobId = JobId.New();

        // Create an already-expired job
        await storage.EnqueueAsync(new JobRecord
        {
            Id = jobId,
            JobType = typeof(SimpleJob).AssemblyQualifiedName!,
            InputType = typeof(NoInput).AssemblyQualifiedName!,
            InputJson = "{}",
            Queue = "default",
            Priority = JobPriority.Normal,
            Status = JobStatus.Enqueued,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            CreatedAt = DateTimeOffset.UtcNow,
            MaxAttempts = 10,
        });

        // Fetch and check (simulating dispatcher)
        var job = await storage.FetchNextAsync(["default"]);
        job!.Status.Should().Be(JobStatus.Processing);

        // Mark as expired
        await storage.SetExpiredAsync(jobId);

        // Verify persistence
        var retrieved = await storage.GetJobByIdAsync(jobId);
        retrieved!.Status.Should().Be(JobStatus.Expired);
        retrieved.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ExpiredMetric_IsIncremented()
    {
        using var host = BuildHost(s => s.AddTransient<SimpleJob>());
        await host.StartAsync();

        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();

        // Enqueue already-expired job
        await storage.EnqueueAsync(new JobRecord
        {
            Id = JobId.New(),
            JobType = typeof(SimpleJob).AssemblyQualifiedName!,
            InputType = typeof(NoInput).AssemblyQualifiedName!,
            InputJson = "{}",
            Queue = "default",
            Priority = JobPriority.Normal,
            Status = JobStatus.Enqueued,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            CreatedAt = DateTimeOffset.UtcNow,
            MaxAttempts = 10,
        });

        // Give dispatcher time to process and record metric
        await Task.Delay(500);

        // Verify that the job was marked as Expired (metric internally incremented)
        var metrics = await storage.GetMetricsAsync();
        metrics.Expired.Should().Be(1);

        await host.StopAsync();
    }

    [Fact]
    public async Task NoDeadline_NeverExpires()
    {
        var executed = false;

        using var host = BuildHost(s => s.AddTransient(_ => new MockJob(() => executed = true)));
        await host.StartAsync();

        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        var jobId = JobId.New();

        // Enqueue a job with no deadline
        await storage.EnqueueAsync(new JobRecord
        {
            Id = jobId,
            JobType = typeof(MockJob).AssemblyQualifiedName!,
            InputType = typeof(NoInput).AssemblyQualifiedName!,
            InputJson = "{}",
            Queue = "default",
            Priority = JobPriority.Normal,
            Status = JobStatus.Enqueued,
            ExpiresAt = null, // No deadline
            CreatedAt = DateTimeOffset.UtcNow,
            MaxAttempts = 10,
        });

        // Give the dispatcher time to process
        await Task.Delay(500);

        executed.Should().BeTrue("job without deadline must execute");

        await host.StopAsync();
    }

    [Fact]
    public async Task DeadlineAppliesOnlyToImmediate_NotScheduled()
    {
        var storage = new InMemoryStorageProvider();
        var scheduler = new DefaultScheduler(storage, storage, storage, new NexJobOptions(), new JobWakeUpChannel());

        // Scheduled jobs should not have deadline applied (API shape ensures this)
        var jobId = await scheduler.ScheduleAsync<SimpleJob>(
            TimeSpan.FromSeconds(10));

        var job = await storage.GetJobByIdAsync(jobId);
        job!.ExpiresAt.Should().BeNull("scheduled jobs should not have deadline");
        job.ScheduledAt.Should().NotBeNull();
    }

    // ─── helper jobs ──────────────────────────────────────────────────────────

    private sealed class SimpleJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SimpleInputJob : IJob<string>
    {
        public Task ExecuteAsync(string input, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MockJob : IJob
    {
        private readonly Action _execute;

        public MockJob(Action execute) => _execute = execute;

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            _execute();
            return Task.CompletedTask;
        }
    }

    private sealed class QuickInput
    {
    }

    private sealed class QuickSuccessJob : IJob<QuickInput>
    {
        private readonly TaskCompletionSource<bool> _tcs;

        public QuickSuccessJob(TaskCompletionSource<bool> tcs) => _tcs = tcs;

        public Task ExecuteAsync(QuickInput input, CancellationToken cancellationToken)
        {
            _tcs.TrySetResult(true);
            return Task.CompletedTask;
        }
    }
}
