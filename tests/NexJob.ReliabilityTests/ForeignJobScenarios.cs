using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Two services share one database. A job that another service enqueued names a type this one cannot load: it must be
/// deferred without using an attempt and without failing, while the jobs this service owns keep running. Shared by
/// every database provider. A subclass only says how to register its storage.
/// </summary>
public abstract class ForeignJobScenarios : DistributedReliabilityTestBase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(40);

    /// <summary>Gets the action that registers the storage under test.</summary>
    /// <returns>The registration action.</returns>
    protected abstract Action<IServiceCollection> Storage();

    [Fact]
    public async Task ForeignJob_IsDeferredWithoutUsingAttempts_AndTheOwnJobsOnTheSameQueueKeepRunning()
    {
        // N1 (own job runs) and N2 (foreign job is not failed) on one queue.
        var queue = $"foreign-{Guid.NewGuid():N}";
        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<OwnJob>(),
            queues: [queue],
            maxAttempts: 2,
            configureOptions: options => options.ForeignJobRetryDelay = TimeSpan.FromMilliseconds(300));
        await host.StartAsync();
        var storage = host.Services.GetRequiredService<IStorageProvider>();
        var foreign = JobRecordFactory.Build(
            jobType: "OtherService.Jobs.ShipOrderJob, OtherService",
            inputType: typeof(string).AssemblyQualifiedName!,
            inputJson: "\"order-1\"",
            options: host.Services.GetRequiredService<NexJobOptions>(),
            queue: queue);
        await storage.EnqueueAsync(foreign, DuplicatePolicy.AllowAfterFailed);
        var own = await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<OwnJob>(queue: queue);

        (await WaitForJobStatus(host, own, JobStatus.Succeeded, Timeout)).Should().NotBeNull("a foreign job does not block the queue");

        var quietObservations = 0;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(4);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var seen = await storage.GetJobByIdAsync(foreign.Id);
            seen!.Status.Should().NotBe(JobStatus.Failed, "a job this service cannot load is not this service's to fail");
            if (seen.Status != JobStatus.Processing)
            {
                seen.Attempts.Should().Be(0, "the attempt is given back every time the job is deferred");
                quietObservations++;
            }

            await Task.Delay(100);
        }

        quietObservations.Should().BeGreaterThan(0);
        await host.StopAsync();
    }

    [Fact]
    public async Task ForeignJobAlone_IsNeverFailed_EvenAfterMoreDeferralsThanMaxAttempts()
    {
        // N3 (Boundary): more deferrals than MaxAttempts must not turn the job into a failure.
        var queue = $"foreign-alone-{Guid.NewGuid():N}";
        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<OwnJob>(),
            queues: [queue],
            maxAttempts: 2,
            configureOptions: options => options.ForeignJobRetryDelay = TimeSpan.FromMilliseconds(200));
        await host.StartAsync();
        var storage = host.Services.GetRequiredService<IStorageProvider>();
        var foreign = JobRecordFactory.Build(
            jobType: "OtherService.Jobs.ShipOrderJob, OtherService",
            inputType: typeof(string).AssemblyQualifiedName!,
            inputJson: "\"order-2\"",
            options: host.Services.GetRequiredService<NexJobOptions>(),
            queue: queue);
        await storage.EnqueueAsync(foreign, DuplicatePolicy.AllowAfterFailed);

        await Task.Delay(TimeSpan.FromSeconds(3));

        var after = await storage.GetJobByIdAsync(foreign.Id);
        after!.Status.Should().NotBe(JobStatus.Failed);
        after.Attempts.Should().BeLessThan(2, "deferrals give their attempt back");
        await host.StopAsync();
    }

    private sealed class OwnJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
