using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob.Internal;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Base class for reliability tests providing common setup and utilities.
/// </summary>
public abstract class ReliabilityTestBase
{
    /// <summary>
    /// Builds a host with standard reliability test configuration.
    /// </summary>
    protected static IHost BuildHost(
        Action<IServiceCollection> registerJobs,
        int workers = 2,
        TimeSpan? pollingInterval = null)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Debug);
            })
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = workers;
                    opt.MaxAttempts = 3;
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
    /// Resets all test job and handler static state.
    /// </summary>
    protected static void ResetTestState()
    {
        TrackingJob.ExecutionCount = 0;
        FailOnceThenSucceedJob.ExecutionCount = 0;
        CancellableJob.CancellationCount = 0;
        RecordingDeadLetterHandler<AlwaysFailJob>.Reset();
    }
}
