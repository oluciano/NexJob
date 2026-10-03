using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob.Internal;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Base class for distributed reliability tests providing common setup and utilities.
/// Differs from ReliabilityTestBase by accepting a storage provider registration action.
/// </summary>
public abstract class DistributedReliabilityTestBase
{
    /// <summary>
    /// Builds a host with distributed reliability test configuration.
    /// </summary>
    protected static IHost BuildHost(
        Action<IServiceCollection> registerStorage,
        Action<IServiceCollection> registerJobs,
        int workers = 2,
        TimeSpan? pollingInterval = null,
        IReadOnlyList<string>? queues = null,
        TimeSpan? heartbeatTimeout = null,
        TimeSpan? heartbeatInterval = null,
        int maxAttempts = 3)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Debug);
            })
            .ConfigureServices(services =>
            {
                registerStorage(services);
                services.AddNexJob(opt =>
                {
                    opt.Workers = workers;
                    opt.MaxAttempts = maxAttempts;
                    if (queues is not null)
                    {
                        opt.Queues = queues;
                    }

                    if (heartbeatTimeout is not null)
                    {
                        opt.HeartbeatTimeout = heartbeatTimeout.Value;
                    }

                    if (heartbeatInterval is not null)
                    {
                        opt.HeartbeatInterval = heartbeatInterval.Value;
                    }

                    opt.PollingInterval = pollingInterval ?? TimeSpan.FromMilliseconds(100);
                    opt.RetryDelayFactory = _ => TimeSpan.FromMilliseconds(200); // Fast retries for tests
                });
                registerJobs(services);
            })
            .Build();
    }

    /// <summary>
    /// Waits for a job to reach a specific status, polling the storage.
    /// </summary>
    protected static async Task<JobRecord?> WaitForJobStatus(
        IHost host,
        JobId jobId,
        JobStatus expectedStatus,
        TimeSpan timeout)
    {
        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            var job = await storage.GetJobByIdAsync(jobId);
            if (job?.Status == expectedStatus)
            {
                return job;
            }

            await Task.Delay(50);
        }

        return null;
    }

    /// <summary>
    /// Gets current job count by status.
    /// </summary>
    protected static async Task<int> GetJobCountByStatus(
        IHost host,
        JobStatus status)
    {
        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();
        var filter = new JobFilter { Status = status };
        var page = await storage.GetJobsAsync(
            filter: filter,
            page: 1,
            pageSize: 1000);
        return page.TotalCount;
    }

    /// <summary>
    /// Polls <paramref name="condition"/> until it is true or <paramref name="timeout"/> elapses.
    /// </summary>
    protected static async Task<bool> WaitUntil(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }

    /// <summary>
    /// Waits until every job in <paramref name="jobIds"/> is <see cref="JobStatus.Succeeded"/>.
    /// </summary>
    protected static Task<bool> WaitForAllSucceeded(IHost host, IReadOnlyCollection<JobId> jobIds, TimeSpan timeout)
    {
        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();

        return WaitUntil(
            async () =>
            {
                foreach (var jobId in jobIds)
                {
                    var job = await storage.GetJobByIdAsync(jobId);
                    if (job?.Status != JobStatus.Succeeded)
                    {
                        return false;
                    }
                }

                return true;
            },
            timeout);
    }
}
