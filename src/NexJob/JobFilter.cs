namespace NexJob;

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

    /// <summary>Filter by recurring job identifier. <see langword="null"/> returns jobs from all recurring definitions.</summary>
    public string? RecurringJobId { get; init; }
}
