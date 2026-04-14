namespace NexJob;

/// <summary>Aggregated metrics snapshot for the NexJob dashboard overview.</summary>
public sealed class JobMetrics
{
    /// <summary>Jobs waiting to be picked up.</summary>
    public int Enqueued { get; init; }

    /// <summary>Jobs currently being executed by a worker.</summary>
    public int Processing { get; init; }

    /// <summary>Jobs that completed successfully.</summary>
    public int Succeeded { get; init; }

    /// <summary>
    /// Jobs that exhausted all retry attempts and reached the dead-letter state.
    /// </summary>
    /// <remarks>
    /// This count reflects <see cref="JobStatus.Failed"/> jobs with no pending retry
    /// (i.e. <see cref="JobRecord.RetryAt"/> is <see langword="null"/>).
    /// Jobs that failed but still have retries remaining are counted in <see cref="Enqueued"/>.
    /// </remarks>
    public int Failed { get; init; }

    /// <summary>Jobs scheduled to run at a future time.</summary>
    public int Scheduled { get; init; }

    /// <summary>Jobs that were not executed before their deadline.</summary>
    public int Expired { get; init; }

    /// <summary>Number of registered recurring job definitions.</summary>
    public int Recurring { get; init; }

    /// <summary>Per-hour throughput for the last 24 hours (succeeded + failed).</summary>
    public IReadOnlyList<HourlyThroughput> HourlyThroughput { get; init; } = [];

    /// <summary>
    /// The 10 most recently dead-lettered jobs, ordered by completion time descending.
    /// </summary>
    /// <remarks>
    /// Used by the dashboard overview page. Not paginated — for full list use
    /// <see cref="NexJob.Storage.IDashboardStorage.GetJobsAsync"/> with
    /// <see cref="JobFilter.Status"/> set to <see cref="JobStatus.Failed"/>.
    /// </remarks>
    public IReadOnlyList<JobRecord> RecentFailures { get; init; } = [];
}
