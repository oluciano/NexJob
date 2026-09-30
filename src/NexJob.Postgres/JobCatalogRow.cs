namespace NexJob.Postgres;

/// <summary>Dapper row mapping for GetJobCatalogAsync query.</summary>
internal sealed class JobCatalogRow
{
    public string JobType { get; set; } = string.Empty;
    public string Queue { get; set; } = "default";
    public long TotalRuns { get; set; }
    public long SucceededRuns { get; set; }
    public long FailedRuns { get; set; }
    public DateTimeOffset? LastExecutedAt { get; set; }
    public double? AvgDurationSeconds { get; set; }
}
