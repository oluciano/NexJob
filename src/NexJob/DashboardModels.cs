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

    /// <summary>Jobs that exhausted all retry attempts (dead-letter).</summary>
    public int Failed { get; init; }

    /// <summary>Jobs scheduled to run at a future time.</summary>
    public int Scheduled { get; init; }

    /// <summary>Number of registered recurring job definitions.</summary>
    public int Recurring { get; init; }

    /// <summary>Per-hour throughput for the last 24 hours (succeeded + failed).</summary>
    public IReadOnlyList<HourlyThroughput> HourlyThroughput { get; init; } = [];

    /// <summary>The 10 most recently failed jobs.</summary>
    public IReadOnlyList<JobRecord> RecentFailures { get; init; } = [];
}

/// <summary>Number of completed jobs (succeeded + failed) in a single hour window.</summary>
public sealed class HourlyThroughput
{
    /// <summary>The start of the one-hour bucket (UTC, truncated to the hour).</summary>
    public DateTimeOffset Hour { get; init; }

    /// <summary>Number of jobs that completed within this hour.</summary>
    public int Count { get; init; }
}

/// <summary>A page of results from a paginated query.</summary>
public sealed class PagedResult<T>
{
    /// <summary>Items on the current page.</summary>
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>Total number of items across all pages.</summary>
    public int TotalCount { get; init; }

    /// <summary>1-based current page number.</summary>
    public int Page { get; init; }

    /// <summary>Maximum number of items per page.</summary>
    public int PageSize { get; init; }

    /// <summary>Total number of pages.</summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}

/// <summary>Filter criteria for job list queries.</summary>
public sealed class JobFilter
{
    /// <summary>Filter by lifecycle status. <see langword="null"/> returns all statuses.</summary>
    public JobStatus? Status { get; init; }

    /// <summary>
    /// Free-text search matched against <see cref="JobRecord.JobType"/> and
    /// the string representation of <see cref="JobRecord.Id"/>.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>Filter by queue name. <see langword="null"/> returns all queues.</summary>
    public string? Queue { get; init; }
}

/// <summary>Real-time metrics for a single queue.</summary>
public sealed class QueueMetrics
{
    /// <summary>Queue name.</summary>
    public string Queue { get; init; } = string.Empty;

    /// <summary>Number of jobs waiting to be claimed.</summary>
    public int Enqueued { get; init; }

    /// <summary>Number of jobs currently being processed.</summary>
    public int Processing { get; init; }
}
