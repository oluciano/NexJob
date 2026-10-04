using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexJob;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// The registration overloads that take an object the application owns (a data source, a multiplexer, a database) instead of
/// a connection string. #308 hid for three releases because no test ever connected through one. Each test builds the object,
/// registers it, and runs a real host against a real database. A subclass says how to create the object and register it.
/// </summary>
public abstract class LiveObjectOverloadScenarios : DistributedReliabilityTestBase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(700);

    /// <summary>Creates the object the application owns and the registration that uses it.</summary>
    /// <returns>The registration and the owned object.</returns>
    protected abstract (Action<IServiceCollection> Register, IAsyncDisposable Owned) CreateOwnedObject();

    /// <summary>Asserts that the owned object still works after NexJob was shut down.</summary>
    /// <param name="owned">The object returned by <see cref="CreateOwnedObject"/>.</param>
    /// <returns>A task that completes when the check is done.</returns>
    protected abstract Task AssertStillUsableAsync(IAsyncDisposable owned);

    [Fact]
    public async Task LiveObject_ProcessesAJob_AndTheRuntimeSettingsStoreReadsAndWrites()
    {
        // N1 (Positive): the job runs, and a paused queue stays paused until resumed, which goes through the settings store.
        var queue = $"live-{Guid.NewGuid():N}";
        var counter = new ExecutionCounter();
        var (register, owned) = CreateOwnedObject();
        await using var ownedLifetime = owned;
        using var host = BuildHost(
            register,
            s => s.AddTransient<SuccessJob>(sp => new SuccessJob(counter.Increment, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SuccessJob>>())),
            workers: 2,
            queues: [queue]);

        await host.StartAsync(); // the schema may only exist once the host has started
        var control = host.Services.GetRequiredService<IJobControlService>();
        await control.PauseQueueAsync(queue);
        await Task.Delay(Grace); // a pause is read on the next polling cycle (100 ms here)
        var jobId = await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<SuccessJob>(queue: queue);

        await Task.Delay(Grace);
        counter.Count.Should().Be(0, "the pause was written through the settings store and the dispatcher read it back");

        await control.ResumeQueueAsync(queue);
        (await WaitForJobStatus(host, jobId, JobStatus.Succeeded, Timeout)).Should().NotBeNull();
        counter.Count.Should().Be(1);

        await host.StopAsync();
    }

    [Fact]
    public async Task LiveObject_StaysUsable_AfterNexJobShutsDown()
    {
        // N2 (Negative): the application owns the object, so NexJob must not dispose or close it.
        var (register, owned) = CreateOwnedObject();
        await using var ownedLifetime = owned;
        using (var host = BuildHost(register, s => s.AddTransient<SuccessJob>(sp => new SuccessJob(() => { }, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SuccessJob>>())), workers: 1, queues: [$"live-{Guid.NewGuid():N}"]))
        {
            await host.StartAsync();
            await Task.Delay(TimeSpan.FromSeconds(1.5)); // several polling cycles with nothing to do
            await host.StopAsync();
        }

        await AssertStillUsableAsync(owned);
    }
}
