using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexJob;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// A connection pool smaller than the number of workers. Workers then wait for a connection instead of getting one at once:
/// everything has to finish, slower, with no deadlock and no job lost or failed because it waited (#256).
/// </summary>
public abstract class PoolScenarios : DistributedReliabilityTestBase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    /// <summary>Gets the registration of the storage with a connection pool limited to <paramref name="maxPoolSize"/>.</summary>
    /// <param name="maxPoolSize">The largest number of pooled connections.</param>
    /// <returns>The registration action.</returns>
    protected abstract Action<IServiceCollection> StorageWithPoolOf(int maxPoolSize);

    [Fact]
    public async Task PoolSmallerThanWorkers_StillCompletesEveryJob_WithoutFailingAny()
    {
        // N2 (Negative): pressure on the pool must not cost a job its attempt.
        const int workers = 10;
        const int jobs = 60;
        var queue = $"pool-{Guid.NewGuid():N}";
        using var host = BuildHost(
            StorageWithPoolOf(3),
            s => s.AddTransient<SuccessJob>(sp => new SuccessJob(() => { }, sp.GetRequiredService<ILogger<SuccessJob>>())),
            workers: workers,
            queues: [queue],
            maxAttempts: 1);
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var ids = new List<JobId>();
        for (var i = 0; i < jobs; i++)
        {
            ids.Add(await scheduler.EnqueueAsync<SuccessJob>(queue: queue));
        }

        (await WaitForAllSucceeded(host, ids, Timeout)).Should().BeTrue("with fewer connections than workers everything still finishes");

        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();
        foreach (var id in ids)
        {
            var job = await storage.GetJobByIdAsync(id);
            job!.Status.Should().Be(JobStatus.Succeeded);
            job.Attempts.Should().Be(1, "waiting for a connection is not a failed attempt");
        }

        await host.StopAsync();
    }
}
