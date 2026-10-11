using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace NexJob.Tests;

/// <summary>End-to-end guard for issue #257: a continuation runs after its parent whether or not batch acknowledgment is on.</summary>
public sealed class BatchAckContinuationEndToEndTests
{
    /// <summary>N1 and N2: the child runs after the parent with batch acknowledgment on and off.</summary>
    /// <param name="batchAck">Whether batch acknowledgment is enabled.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Continuation_RunsAfterParent(bool batchAck)
    {
        var childRan = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.PollingInterval = TimeSpan.FromMilliseconds(20);
                    opt.EnableBatchAcknowledgment = batchAck;
                });
                services.AddTransient<ParentJob>();
                services.AddTransient(_ => new ChildJob(childRan));
            })
            .Build();
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var parentId = await scheduler.EnqueueAsync<ParentJob>();
        await scheduler.ContinueWithAsync<ChildJob>(parentId);

        (await childRan.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
        await host.StopAsync();
    }

    /// <summary>N3 (Invalid input): a failing parent never releases its child, also with batch acknowledgment on.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Continuation_DoesNotRun_WhenParentFails()
    {
        var childRan = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.PollingInterval = TimeSpan.FromMilliseconds(20);
                    opt.MaxAttempts = 1;
                    opt.EnableBatchAcknowledgment = true;
                });
                services.AddTransient<FailingParentJob>();
                services.AddTransient(_ => new ChildJob(childRan));
            })
            .Build();
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var parentId = await scheduler.EnqueueAsync<FailingParentJob>();
        await scheduler.ContinueWithAsync<ChildJob>(parentId);
        await Task.Delay(500);

        childRan.Task.IsCompleted.Should().BeFalse();
        await host.StopAsync();
    }

    /// <summary>N1 (issue #495): a promoted child runs well before the polling interval, with batch acknowledgment on and off.</summary>
    /// <param name="batchAck">Whether batch acknowledgment is enabled.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Continuation_RunsBeforePollingInterval_AfterParentSucceeds(bool batchAck)
    {
        var childRan = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.PollingInterval = TimeSpan.FromSeconds(30);
                    opt.EnableBatchAcknowledgment = batchAck;
                });
                services.AddTransient(_ => new GatedParentJob(gate));
                services.AddTransient(_ => new ChildJob(childRan));
            })
            .Build();
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var parentId = await scheduler.EnqueueAsync<GatedParentJob>();
        await scheduler.ContinueWithAsync<ChildJob>(parentId);
        gate.SetResult(true);

        (await childRan.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        await host.StopAsync();
    }

    /// <summary>N3 (issue #495): a parent without children succeeds and the next job is still picked up at once.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ParentWithoutChildren_Succeeds_AndNextJobStillRuns()
    {
        var secondRan = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt => opt.PollingInterval = TimeSpan.FromSeconds(30));
                services.AddTransient<ParentJob>();
                services.AddTransient(_ => new ChildJob(secondRan));
            })
            .Build();
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        await scheduler.EnqueueAsync<ParentJob>();
        await scheduler.EnqueueAsync<ChildJob>();

        (await secondRan.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        await host.StopAsync();
    }

    private sealed class GatedParentJob(TaskCompletionSource<bool> gate) : IJob
    {
        public async Task ExecuteAsync(CancellationToken cancellationToken) => await gate.Task.WaitAsync(cancellationToken);
    }

    private sealed class ParentJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FailingParentJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("parent fails");
    }

    private sealed class ChildJob(TaskCompletionSource<bool> ran) : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            ran.TrySetResult(true);
            return Task.CompletedTask;
        }
    }
}
