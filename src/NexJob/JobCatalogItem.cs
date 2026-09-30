namespace NexJob;

/// <summary>
/// Aggregated catalog item representing distinct job definitions with execution performance statistics.
/// </summary>
/// <param name="JobType">The fully-qualified or short type name of the job definition.</param>
/// <param name="Queue">The queue designated for this job.</param>
/// <param name="TotalRuns">Total number of recorded executions.</param>
/// <param name="SucceededRuns">Number of executions that completed successfully.</param>
/// <param name="FailedRuns">Number of executions that exhausted retries or failed.</param>
/// <param name="LastExecutedAt">Timestamp of the most recent execution, if any.</param>
/// <param name="AvgDurationSeconds">Average execution duration in seconds, if available.</param>
public sealed record JobCatalogItem(
    string JobType,
    string Queue,
    long TotalRuns,
    long SucceededRuns,
    long FailedRuns,
    DateTimeOffset? LastExecutedAt,
    double? AvgDurationSeconds = null);
