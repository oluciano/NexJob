namespace NexJob;

/// <summary>
/// The exception handed to dead-letter handlers and forwarders when a job fails because the node that was running it
/// stopped sending heartbeats and the job had no attempts left. The job itself never reported an error: the process
/// that held it died, so this exception stands in for the failure.
/// </summary>
public sealed class OrphanedJobException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="OrphanedJobException"/> class.</summary>
    /// <param name="jobId">The job that was orphaned.</param>
    /// <param name="attempts">How many attempts the job had used.</param>
    public OrphanedJobException(JobId jobId, int attempts)
        : base($"Job {jobId.Value} was orphaned: the node running it stopped sending heartbeats and no attempts were left ({attempts} used).")
    {
        JobId = jobId;
        Attempts = attempts;
    }

    /// <summary>Gets the job that was orphaned.</summary>
    public JobId JobId { get; }

    /// <summary>Gets how many attempts the job had used when it was orphaned.</summary>
    public int Attempts { get; }
}
