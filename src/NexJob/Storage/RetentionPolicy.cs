namespace NexJob.Storage;

/// <summary>
/// Defines how long completed jobs are retained before being automatically deleted.
/// Passed to <see cref="IJobStorage.PurgeJobsAsync"/> by the retention service.
/// </summary>
public sealed class RetentionPolicy
{
    /// <summary>
    /// How long to retain jobs in <see cref="JobStatus.Succeeded"/> state.
    /// Jobs older than this threshold are deleted. <see cref="TimeSpan.Zero"/> disables purging for this status.
    /// </summary>
    public TimeSpan RetainSucceeded { get; init; } = TimeSpan.FromDays(7);

    /// <summary>
    /// How long to retain jobs in <see cref="JobStatus.Failed"/> state.
    /// Jobs older than this threshold are deleted. <see cref="TimeSpan.Zero"/> disables purging for this status.
    /// </summary>
    public TimeSpan RetainFailed { get; init; } = TimeSpan.FromDays(30);

    /// <summary>
    /// How long to retain jobs in <see cref="JobStatus.Expired"/> state.
    /// Jobs older than this threshold are deleted. <see cref="TimeSpan.Zero"/> disables purging for this status.
    /// </summary>
    public TimeSpan RetainExpired { get; init; } = TimeSpan.FromDays(7);

    /// <summary>
    /// How long to retain dead-letter jobs before automatic deletion.
    /// Jobs older than this threshold are deleted. <see cref="TimeSpan.Zero"/> disables purging for this status.
    /// Defaults to 60 days.
    /// </summary>
    public TimeSpan RetainDeadLetter { get; init; } = TimeSpan.FromDays(60);

    /// <summary>
    /// Maximum number of rows to delete per batch/chunk during retention purge.
    /// Helps avoid database lock escalation and transaction log bloat.
    /// Defaults to 1000.
    /// </summary>
    public int BatchSize { get; init; } = 1000;
}
