using NexJob.Storage;

namespace NexJob.Internal;

/// <summary>
/// Scoped implementation of <see cref="IJobContext"/> populated by
/// <see cref="JobDispatcherService"/> before invoking each job.
/// </summary>
internal sealed class JobContext : IJobContext
{
    private readonly IJobStorage _storage;

    /// <summary>
    /// Initializes a new <see cref="JobContext"/> from the fetched <paramref name="job"/> record.
    /// </summary>
    public JobContext(JobRecord job, IJobStorage storage)
    {
        JobId = job.Id;
        Attempt = job.Attempts;
        MaxAttempts = job.MaxAttempts;
        Queue = job.Queue;
        RecurringJobId = job.RecurringJobId;
        Tags = job.Tags ?? [];
        _storage = storage;
    }

    /// <inheritdoc/>
    public JobId JobId { get; }

    /// <inheritdoc/>
    public int Attempt { get; }

    /// <inheritdoc/>
    public int MaxAttempts { get; }

    /// <inheritdoc/>
    public string Queue { get; }

    /// <inheritdoc/>
    public string? RecurringJobId { get; }

    /// <inheritdoc/>
    public IReadOnlyList<string> Tags { get; }

    /// <inheritdoc/>
    public Task ReportProgressAsync(int percent, string? message = null, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(percent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);
        return _storage.ReportProgressAsync(JobId, percent, message, ct);
    }
}
