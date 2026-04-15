using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Integration tests for <see cref="IJobControlService"/> against
/// InMemoryStorageProvider + real dispatcher.
/// Verifies that control actions have observable effects on job execution.
/// </summary>
public sealed class JobControlServiceIntegrationTests
{
    private static IHost BuildHost(Action<IServiceCollection> registerJobs, int workers = 2, int maxAttempts = 10)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = workers;
                    opt.MaxAttempts = maxAttempts;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(20);
                });
                registerJobs(services);
            })
            .Build();
    }

    [Fact]
    public async Task PauseQueueAsync_PreventsJobExecution()
    {
        // 1. Build host, start it
        var tcs = new TaskCompletionSource<bool>();
        using var host = BuildHost(s => s.AddTransient(_ => new QuickSuccessJob(tcs)));
        await host.StartAsync();

        var control = host.Services.GetRequiredService<IJobControlService>();
        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();

        // 2. Get IJobControlService, pause "default" queue
        await control.PauseQueueAsync("default");

        // 3. Enqueue a job to "default"
        var jobId = await scheduler.EnqueueAsync<QuickSuccessJob, QuickInput>(new());

        // 4. Wait 500ms
        await Task.Delay(500);

        // 5. Assert: job is still Enqueued (not Succeeded) — dispatcher skipped it
        var job = await storage.GetJobByIdAsync(jobId);
        job.Should().NotBeNull();
        job!.Status.Should().Be(JobStatus.Enqueued);
        tcs.Task.IsCompleted.Should().BeFalse();

        // 6. StopAsync
        await host.StopAsync();
    }

    [Fact]
    public async Task ResumeQueueAsync_AllowsJobExecutionAfterPause()
    {
        // 1. Build host, start it
        var tcs = new TaskCompletionSource<bool>();
        using var host = BuildHost(s => s.AddTransient(_ => new QuickSuccessJob(tcs)));
        await host.StartAsync();

        var control = host.Services.GetRequiredService<IJobControlService>();
        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();

        // 2. Pause "default" queue
        await control.PauseQueueAsync("default");

        // 3. Enqueue a job
        var jobId = await scheduler.EnqueueAsync<QuickSuccessJob, QuickInput>(new());

        // 4. Wait 200ms — assert still Enqueued
        await Task.Delay(200);
        var jobBefore = await storage.GetJobByIdAsync(jobId);
        jobBefore!.Status.Should().Be(JobStatus.Enqueued);

        // 5. Resume "default" queue
        await control.ResumeQueueAsync("default");

        // 6. Wait for Succeeded (timeout 5s)
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);

        // 7. Assert: job reached Succeeded
        var jobAfter = await storage.GetJobByIdAsync(jobId);
        jobAfter!.Status.Should().Be(JobStatus.Succeeded);

        await host.StopAsync();
    }

    [Fact]
    public async Task RequeueJobAsync_ReEnqueuesFailedJob()
    {
        // 1. Build host, start it (MaxAttempts=1)
        var tcs = new TaskCompletionSource<bool>();
        using var host = BuildHost(s => s.AddTransient(_ => new AlwaysFailJob(tcs)), maxAttempts: 1);
        await host.StartAsync();

        var control = host.Services.GetRequiredService<IJobControlService>();
        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();

        // 2. Enqueue a failing job
        var jobId = await scheduler.EnqueueAsync<AlwaysFailJob, FailInput>(new());

        // 3. Wait for Failed status
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);
        var jobFailed = await storage.GetJobByIdAsync(jobId);
        jobFailed!.Status.Should().Be(JobStatus.Failed);

        // 4. Call IJobControlService.RequeueJobAsync
        await control.RequeueJobAsync(jobId);

        // 5. Assert: job status is Enqueued again, Attempts == 0
        var jobRequeued = await storage.GetJobByIdAsync(jobId);
        jobRequeued!.Status.Should().Be(JobStatus.Enqueued);
        jobRequeued.Attempts.Should().Be(0);

        await host.StopAsync();
    }

    [Fact]
    public async Task DeleteJobAsync_RemovesJob()
    {
        // 1. Build host, start it
        using var host = BuildHost(s => s.AddTransient<QuickSuccessJob>());
        await host.StartAsync();

        var control = host.Services.GetRequiredService<IJobControlService>();
        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();

        // 2. Pause "default" queue (so job stays Enqueued)
        await control.PauseQueueAsync("default");

        // 3. Enqueue a job
        var jobId = await scheduler.EnqueueAsync<QuickSuccessJob, QuickInput>(new());

        // 4. Call IJobControlService.DeleteJobAsync
        await control.DeleteJobAsync(jobId);

        // 5. Get storage, assert GetJobByIdAsync returns null
        var job = await storage.GetJobByIdAsync(jobId);
        job.Should().BeNull();

        await host.StopAsync();
    }

    [Fact]
    public async Task PauseQueueAsync_IsIdempotent()
    {
        using var host = BuildHost(s => { });
        await host.StartAsync();

        var control = host.Services.GetRequiredService<IJobControlService>();

        // Call PauseQueueAsync("default") twice
        await control.PauseQueueAsync("default");
        var act = () => control.PauseQueueAsync("default");

        // Assert: no exception
        await act.Should().NotThrowAsync();

        await host.StopAsync();
    }
}
