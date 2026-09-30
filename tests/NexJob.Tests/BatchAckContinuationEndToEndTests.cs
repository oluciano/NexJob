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
