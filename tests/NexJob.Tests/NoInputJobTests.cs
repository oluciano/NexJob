using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Tests for <see cref="IJob"/> (no-input interface) support in NexJob.
/// Covers enqueue, schedule, discovery, and context injection for jobs without input parameters.
/// </summary>
public sealed class NoInputJobTests
{
    private static IHost BuildHost(Action<IServiceCollection> register) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.UseInMemory();
                    opt.Workers = 2;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(20);
                });
                register(services);
            })
            .Build();

    /// <summary>
    /// Verifies that <see cref="NexJobOptions.UseInMemory()"/> compiles and does not throw.
    /// </summary>
    [Fact]
    public void UseInMemory_DoesNotThrow()
    {
        var options = new NexJobOptions();
        var act = () => options.UseInMemory();
        act.Should().NotThrow();
    }

    /// <summary>
    /// Verifies that <see cref="NexJobOptions.UseInMemory()"/> returns the same instance for fluent chaining.
    /// </summary>
    [Fact]
    public void UseInMemory_ReturnsThis_ForChaining()
    {
        var options = new NexJobOptions();
        var result = options.UseInMemory();
        result.Should().BeSameAs(options);
    }

    /// <summary>
    /// Verifies that a simple <see cref="IJob"/> (no-input) executes successfully end-to-end.
    /// </summary>
    [Fact]
    public async Task NoInputJob_ExecutesSuccessfully()
    {
        var tcs = new TaskCompletionSource<bool>();
        using var host = BuildHost(s =>
            s.AddTransient(_ => new SimpleNoInputJob(tcs)));
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<SimpleNoInputJob>();

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeTrue();

        await host.StopAsync();
    }

    /// <summary>
    /// Verifies that <see cref="AddNexJobJobs"/> discovers classes implementing <see cref="IJob"/>.
    /// </summary>
    [Fact]
    public void AddNexJobJobs_DiscoversNoInputJobs()
    {
        var services = new ServiceCollection();
        services.AddNexJob(opt => opt.UseInMemory());
        services.AddNexJobJobs(typeof(NoInputJobTests).Assembly);

        var provider = services.BuildServiceProvider();
        var job = provider.GetService<StubNoInputJob>();
        job.Should().NotBeNull("AddNexJobJobs must register IJob implementations");
    }

    /// <summary>
    /// Verifies that a simple <see cref="IJob"/> (no-input) executes and can be scheduled at a specific time.
    /// </summary>
    [Fact]
    public async Task NoInputJob_AtTime_SchedulesAndExecutes()
    {
        var tcs = new TaskCompletionSource<bool>();
        using var host = BuildHost(s =>
            s.AddTransient(_ => new SimpleNoInputJob(tcs)));

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var runAt = DateTimeOffset.UtcNow.AddMilliseconds(50);
        await scheduler.ScheduleAtAsync<SimpleNoInputJob>(runAt);

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeTrue();

        await host.StopAsync();
    }

    /// <summary>
    /// Verifies that <see cref="IScheduler.EnqueueAsync{TJob}"/> returns a valid <see cref="JobId"/>.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_NoInput_ReturnsValidJobId()
    {
        using var host = BuildHost(s => s.AddTransient<SimpleNoInputJob>());
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<SimpleNoInputJob>();

        jobId.Value.Should().NotBeEmpty();

        await host.StopAsync();
    }

    /// <summary>
    /// Verifies that <see cref="IScheduler.ScheduleAsync{TJob}"/> schedules a no-input job correctly.
    /// </summary>
    [Fact]
    public async Task ScheduleAsync_NoInput_SchedulesJob()
    {
        var tcs = new TaskCompletionSource<bool>();
        using var host = BuildHost(s =>
            s.AddTransient(_ => new SimpleNoInputJob(tcs)));
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.ScheduleAsync<SimpleNoInputJob>(TimeSpan.FromMilliseconds(50));

        jobId.Value.Should().NotBeEmpty();

        // Job should execute after the delay
        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeTrue();

        await host.StopAsync();
    }

    /// <summary>
    /// Verifies that <see cref="IScheduler.EnqueueAsync{TJob}"/> respects idempotency keys.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_NoInput_WithIdempotencyKey_ReturnsSameJobId()
    {
        using var host = BuildHost(s => s.AddTransient<SimpleNoInputJob>());
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        const string idempotencyKey = "test-key";

        var jobId1 = await scheduler.EnqueueAsync<SimpleNoInputJob>(idempotencyKey: idempotencyKey);
        var jobId2 = await scheduler.EnqueueAsync<SimpleNoInputJob>(idempotencyKey: idempotencyKey);

        jobId1.Should().Be(jobId2);

        await host.StopAsync();
    }
}

// ─── test job implementations ────────────────────────────────────────────────

/// <summary>
/// Simple no-input job that signals completion via a TaskCompletionSource.
/// </summary>
internal sealed class SimpleNoInputJob(TaskCompletionSource<bool> tcs) : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        tcs.TrySetResult(true);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Stub no-input job for discovery testing. No dependencies.
/// </summary>
internal sealed class StubNoInputJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
