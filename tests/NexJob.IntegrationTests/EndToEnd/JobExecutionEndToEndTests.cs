using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Storage;
using Xunit;

namespace NexJob.IntegrationTests.EndToEnd;

/// <summary>
/// End-to-end tests that spin up the full NexJob worker pipeline against
/// the in-memory provider and assert that jobs actually execute.
/// </summary>
public sealed class JobExecutionEndToEndTests
{
    [Fact]
    public async Task Enqueued_job_is_executed_end_to_end()
    {
        var executed = new TaskCompletionSource<string>();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = 2;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(50);
                });
                services.AddTransient(_ => new SignalJob(executed));
            })
            .Build();

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<SignalJob, SignalInput>(new("hello"));

        var result = await executed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        result.Should().Be("hello");

        await host.StopAsync();
    }

    [Fact]
    public async Task Failed_job_is_retried_and_eventually_succeeds()
    {
        var attempts = 0;
        var succeeded = new TaskCompletionSource<bool>();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = 1;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(50);
                    opt.MaxAttempts = 3;
                });
                services.AddTransient(_ => new FlakyJob(ref attempts, succeeded));
            })
            .Build();

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<FlakyJob, FlakyInput>(new());

        var result = await succeeded.Task.WaitAsync(TimeSpan.FromSeconds(30));
        result.Should().BeTrue();
        attempts.Should().BeGreaterThan(1);

        await host.StopAsync();
    }

    [Fact]
    public async Task Continuation_runs_after_parent_succeeds()
    {
        var parentDone  = new TaskCompletionSource<bool>();
        var childDone   = new TaskCompletionSource<bool>();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = 2;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(50);
                });
                services.AddTransient(_ => new ParentJob(parentDone));
                services.AddTransient(_ => new ChildJob(childDone));
            })
            .Build();

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var parentId = await scheduler.EnqueueAsync<ParentJob, ParentInput>(new());
        await scheduler.ContinueWithAsync<ChildJob, ChildInput>(parentId, new());

        await parentDone.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await childDone.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await host.StopAsync();
    }
}

// ── Stub jobs ──────────────────────────────────────────────────────────────────

public record SignalInput(string Value);

public class SignalJob(TaskCompletionSource<string> signal) : IJob<SignalInput>
{
    public Task ExecuteAsync(SignalInput input, CancellationToken ct)
    {
        signal.TrySetResult(input.Value);
        return Task.CompletedTask;
    }
}

public record FlakyInput;

public class FlakyJob(ref int attempts, TaskCompletionSource<bool> done) : IJob<FlakyInput>
{
    private int _attempts = attempts;

    public Task ExecuteAsync(FlakyInput input, CancellationToken ct)
    {
        _attempts++;
        if (_attempts < 3) throw new InvalidOperationException("not yet");
        done.TrySetResult(true);
        return Task.CompletedTask;
    }
}

public record ParentInput;
public record ChildInput;

public class ParentJob(TaskCompletionSource<bool> done) : IJob<ParentInput>
{
    public Task ExecuteAsync(ParentInput input, CancellationToken ct)
    {
        done.TrySetResult(true);
        return Task.CompletedTask;
    }
}

public class ChildJob(TaskCompletionSource<bool> done) : IJob<ChildInput>
{
    public Task ExecuteAsync(ChildInput input, CancellationToken ct)
    {
        done.TrySetResult(true);
        return Task.CompletedTask;
    }
}
