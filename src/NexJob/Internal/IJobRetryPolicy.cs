namespace NexJob.Internal;

/// <summary>
/// Determines whether a failed job should be retried and when.
/// </summary>
internal interface IJobRetryPolicy
{
    /// <summary>
    /// Computes the retry timestamp for a failed job.
    /// Returns the UTC time at which the job should be retried,
    /// or <see langword="null"/> if the job has exhausted all attempts
    /// and should be moved to dead-letter.
    /// </summary>
    /// <param name="job">The failed job record. <see cref="JobRecord.Attempts"/> reflects the current attempt count.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <returns>UTC retry timestamp, or null for dead-letter.</returns>
    DateTimeOffset? ComputeRetryAt(JobRecord job, Exception exception);
}
