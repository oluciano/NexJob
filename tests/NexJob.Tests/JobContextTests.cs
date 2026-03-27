using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Unit tests for <see cref="IJobContext"/> injection, progress reporting,
/// tag storage/retrieval, and the <see cref="JobContextExtensions"/> helpers.
/// All tests use <see cref="InMemoryStorageProvider"/> and the real DI host
/// so the full execution pipeline runs without external dependencies.
/// </summary>
public sealed class JobContextTests
{
    // ─── helpers ─────────────────────────────────────────────────────────────

    private static IHost BuildHost(Action<IServiceCollection> registerJobs) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = 2;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(20);
                });
                registerJobs(services);
            })
            .Build();

    // ─── IJobContext injection ────────────────────────────────────────────────

    [Fact]
    public async Task IJobContext_IsInjectable_DuringExecution()
    {
        var tcs = new TaskCompletionSource<bool>();
        using var host = BuildHost(s => s.AddTransient(sp =>
            new ContextInjectableJob(tcs, sp.GetRequiredService<IJobContext>())));
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<ContextInjectableJob, JcSimpleInput>(new JcSimpleInput());

        var resolved = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        resolved.Should().BeTrue("IJobContext should be resolvable during job execution");

        await host.StopAsync();
    }

    [Fact]
    public async Task IJobContext_JobId_MatchesEnqueuedJob()
    {
        var capturedJobId = new TaskCompletionSource<JobId>();
        using var host = BuildHost(s => s.AddTransient(sp =>
            new ContextJobIdCaptureJob(capturedJobId, sp.GetRequiredService<IJobContext>())));
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var enqueued = await scheduler.EnqueueAsync<ContextJobIdCaptureJob, JcSimpleInput>(new JcSimpleInput());

        var captured = await capturedJobId.Task.WaitAsync(TimeSpan.FromSeconds(5));
        captured.Should().Be(enqueued, "IJobContext.JobId must equal the enqueued JobId");

        await host.StopAsync();
    }

    [Fact]
    public async Task IJobContext_Attempt_StartsAt1()
    {
        var capturedAttempt = new TaskCompletionSource<int>();
        using var host = BuildHost(s => s.AddTransient(sp =>
            new ContextAttemptCaptureJob(capturedAttempt, sp.GetRequiredService<IJobContext>())));
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<ContextAttemptCaptureJob, JcSimpleInput>(new JcSimpleInput());

        var attempt = await capturedAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5));
        attempt.Should().Be(1, "first execution attempt must be 1");

        await host.StopAsync();
    }

    [Fact]
    public async Task IJobContext_Queue_MatchesEnqueuedQueue()
    {
        const string expectedQueue = "ctx-queue";
        var capturedQueue = new TaskCompletionSource<string>();
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = 2;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(20);
                    opt.Queues = [expectedQueue];
                });
                services.AddTransient(sp =>
                    new ContextQueueCaptureJob(capturedQueue, sp.GetRequiredService<IJobContext>()));
            })
            .Build();
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<ContextQueueCaptureJob, JcSimpleInput>(
            new JcSimpleInput(), queue: expectedQueue);

        var queue = await capturedQueue.Task.WaitAsync(TimeSpan.FromSeconds(5));
        queue.Should().Be(expectedQueue);

        await host.StopAsync();
    }

    [Fact]
    public async Task IJobContext_Tags_ContainsEnqueuedTags()
    {
        var capturedTags = new TaskCompletionSource<IReadOnlyList<string>>();
        using var host = BuildHost(s => s.AddTransient(sp =>
            new ContextTagsCaptureJob(capturedTags, sp.GetRequiredService<IJobContext>())));
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var tags = new[] { "tenant:acme", "region:us" };
        await scheduler.EnqueueAsync<ContextTagsCaptureJob, JcSimpleInput>(
            new JcSimpleInput(), tags: tags);

        var captured = await capturedTags.Task.WaitAsync(TimeSpan.FromSeconds(5));
        captured.Should().BeEquivalentTo(tags);

        await host.StopAsync();
    }

    [Fact]
    public async Task IJobContext_OutsideExecution_ThrowsInvalidOperationException()
    {
        using var host = BuildHost(_ => { });
        await host.StartAsync();

        var act = () =>
        {
            using var scope = host.Services.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<IJobContext>();
        };

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IJobContext*only available during job execution*");

        await host.StopAsync();
    }

    // ─── ReportProgress ───────────────────────────────────────────────────────

    [Fact]
    public async Task ReportProgress_UpdatesStorageCorrectly()
    {
        var reportDone = new TaskCompletionSource<bool>();
        using var host = BuildHost(s => s.AddTransient(sp =>
            new ProgressReportingJob(reportDone, sp.GetRequiredService<IJobContext>())));
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<ProgressReportingJob, JcSimpleInput>(new JcSimpleInput());

        await reportDone.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Give the storage update a tick to propagate
        await Task.Delay(50);

        var storage = host.Services.GetRequiredService<IStorageProvider>();
        var job = await storage.GetJobByIdAsync(jobId);
        job.Should().NotBeNull();
        job!.ProgressPercent.Should().Be(50);
        job.ProgressMessage.Should().Be("halfway");

        await host.StopAsync();
    }

    // ─── WithProgress extensions ──────────────────────────────────────────────

    [Fact]
    public async Task WithProgress_IEnumerable_ReportsCorrectPercentages()
    {
        var capturedJobId = new TaskCompletionSource<JobId>();
        var jobDone = new TaskCompletionSource<bool>();
        using var host = BuildHost(s => s.AddTransient(sp =>
            new SyncProgressJob(capturedJobId, jobDone, sp.GetRequiredService<IJobContext>())));
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<SyncProgressJob, JcSimpleInput>(new JcSimpleInput());

        var jobId = await capturedJobId.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await jobDone.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // After completion the final progress should be 100%
        var storage = host.Services.GetRequiredService<IStorageProvider>();
        var job = await storage.GetJobByIdAsync(jobId);
        job.Should().NotBeNull();
        job!.ProgressPercent.Should().Be(100, "last item yields 100%");

        await host.StopAsync();
    }

    [Fact]
    public async Task WithProgress_IAsyncEnumerable_ReportsCorrectPercentages()
    {
        var capturedJobId = new TaskCompletionSource<JobId>();
        var jobDone = new TaskCompletionSource<bool>();
        using var host = BuildHost(s => s.AddTransient(sp =>
            new AsyncProgressJob(capturedJobId, jobDone, sp.GetRequiredService<IJobContext>())));
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<AsyncProgressJob, JcSimpleInput>(new JcSimpleInput());

        var jobId = await capturedJobId.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await jobDone.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var storage = host.Services.GetRequiredService<IStorageProvider>();
        var job = await storage.GetJobByIdAsync(jobId);
        job.Should().NotBeNull();
        job!.ProgressPercent.Should().Be(100, "last item yields 100%");

        await host.StopAsync();
    }

    // ─── Tags storage / retrieval ─────────────────────────────────────────────

    [Fact]
    public async Task Enqueue_WithTags_StoresTagsCorrectly()
    {
        using var host = BuildHost(s => s.AddTransient<JcNoopJob>());
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var tags = new[] { "env:prod", "team:backend" };
        var jobId = await scheduler.EnqueueAsync<JcNoopJob, JcSimpleInput>(
            new JcSimpleInput(), tags: tags);

        var storage = host.Services.GetRequiredService<IStorageProvider>();
        var job = await storage.GetJobByIdAsync(jobId);
        job.Should().NotBeNull();
        job!.Tags.Should().BeEquivalentTo(tags);

        await host.StopAsync();
    }

    [Fact]
    public async Task GetJobsByTag_ReturnsOnlyMatchingJobs()
    {
        using var host = BuildHost(s => s.AddTransient<JcNoopJob>());
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<JcNoopJob, JcSimpleInput>(
            new JcSimpleInput(), tags: ["env:prod", "team:backend"]);
        await scheduler.EnqueueAsync<JcNoopJob, JcSimpleInput>(
            new JcSimpleInput(), tags: ["env:staging"]);
        await scheduler.EnqueueAsync<JcNoopJob, JcSimpleInput>(new JcSimpleInput());

        var storage = host.Services.GetRequiredService<IStorageProvider>();
        var results = await storage.GetJobsByTagAsync("env:prod");
        results.Should().HaveCount(1);
        results[0].Tags.Should().Contain("env:prod");

        await host.StopAsync();
    }

    [Fact]
    public async Task GetJobsByTag_WithNoMatches_ReturnsEmpty()
    {
        using var host = BuildHost(s => s.AddTransient<JcNoopJob>());
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<JcNoopJob, JcSimpleInput>(
            new JcSimpleInput(), tags: ["env:prod"]);

        var storage = host.Services.GetRequiredService<IStorageProvider>();
        var results = await storage.GetJobsByTagAsync("env:nonexistent");
        results.Should().BeEmpty();

        await host.StopAsync();
    }
}

// ─── test input ───────────────────────────────────────────────────────────────

internal sealed record JcSimpleInput;

// ─── test jobs ────────────────────────────────────────────────────────────────

internal sealed class ContextInjectableJob(TaskCompletionSource<bool> tcs, IJobContext context) : IJob<JcSimpleInput>
{
    public Task ExecuteAsync(JcSimpleInput input, CancellationToken cancellationToken)
    {
        tcs.TrySetResult(context is not null);
        return Task.CompletedTask;
    }
}

internal sealed class ContextJobIdCaptureJob(TaskCompletionSource<JobId> tcs, IJobContext context) : IJob<JcSimpleInput>
{
    public Task ExecuteAsync(JcSimpleInput input, CancellationToken cancellationToken)
    {
        tcs.TrySetResult(context.JobId);
        return Task.CompletedTask;
    }
}

internal sealed class ContextAttemptCaptureJob(TaskCompletionSource<int> tcs, IJobContext context) : IJob<JcSimpleInput>
{
    public Task ExecuteAsync(JcSimpleInput input, CancellationToken cancellationToken)
    {
        tcs.TrySetResult(context.Attempt);
        return Task.CompletedTask;
    }
}

internal sealed class ContextQueueCaptureJob(TaskCompletionSource<string> tcs, IJobContext context) : IJob<JcSimpleInput>
{
    public Task ExecuteAsync(JcSimpleInput input, CancellationToken cancellationToken)
    {
        tcs.TrySetResult(context.Queue);
        return Task.CompletedTask;
    }
}

internal sealed class ContextTagsCaptureJob(TaskCompletionSource<IReadOnlyList<string>> tcs, IJobContext context) : IJob<JcSimpleInput>
{
    public Task ExecuteAsync(JcSimpleInput input, CancellationToken cancellationToken)
    {
        tcs.TrySetResult(context.Tags);
        return Task.CompletedTask;
    }
}

internal sealed class ProgressReportingJob(TaskCompletionSource<bool> tcs, IJobContext context) : IJob<JcSimpleInput>
{
    public async Task ExecuteAsync(JcSimpleInput input, CancellationToken cancellationToken)
    {
        await context.ReportProgressAsync(50, "halfway", cancellationToken);
        tcs.TrySetResult(true);
    }
}

internal sealed class SyncProgressJob(
    TaskCompletionSource<JobId> jobIdTcs,
    TaskCompletionSource<bool> doneTcs,
    IJobContext context) : IJob<JcSimpleInput>
{
    public Task ExecuteAsync(JcSimpleInput input, CancellationToken cancellationToken)
    {
        jobIdTcs.TrySetResult(context.JobId);
        var items = new[] { "a", "b", "c", "d" };
        foreach (var item in items.WithProgress(context))
        {
            // consume item
            _ = item;
        }

        doneTcs.TrySetResult(true);
        return Task.CompletedTask;
    }
}

internal sealed class AsyncProgressJob(
    TaskCompletionSource<JobId> jobIdTcs,
    TaskCompletionSource<bool> doneTcs,
    IJobContext context) : IJob<JcSimpleInput>
{
    public async Task ExecuteAsync(JcSimpleInput input, CancellationToken cancellationToken)
    {
        jobIdTcs.TrySetResult(context.JobId);
        await foreach (var item in AsyncItems().WithProgress(context, cancellationToken))
        {
            // consume item
            _ = item;
        }

        doneTcs.TrySetResult(true);
    }

    private static async IAsyncEnumerable<string> AsyncItems()
    {
        foreach (var item in new[] { "a", "b", "c", "d" })
        {
            await Task.Yield();
            yield return item;
        }
    }
}

internal sealed class JcNoopJob : IJob<JcSimpleInput>
{
    public Task ExecuteAsync(JcSimpleInput input, CancellationToken cancellationToken) => Task.CompletedTask;
}
