namespace NexJob.Dashboard;

/// <summary>Builds the jobs the dashboard itself enqueues (scenarios, Trigger now).</summary>
internal static class DashboardJobFactory
{
    /// <summary>Builds an enqueued job record for a job type picked in the dashboard.</summary>
    /// <param name="jobType">The resolved job type.</param>
    /// <param name="rawJobType">The type name as received, used when the type has no assembly-qualified name.</param>
    /// <param name="inputTypeName">The assembly-qualified input type name.</param>
    /// <param name="inputJson">The serialized input.</param>
    /// <param name="queue">The queue typed by the operator, or <see langword="null"/> or blank for the default queue.</param>
    /// <param name="options">The options of the host the dashboard runs in.</param>
    /// <param name="tags">Optional tags.</param>
    /// <returns>The record to enqueue.</returns>
    internal static JobRecord CreateJobRecord(
        Type jobType,
        string rawJobType,
        string inputTypeName,
        string inputJson,
        string? queue,
        NexJobOptions options,
        string[]? tags = null)
    {
        return new JobRecord
        {
            Id = JobId.New(),
            JobType = jobType.AssemblyQualifiedName ?? rawJobType,
            InputType = inputTypeName,
            InputJson = inputJson,
            Queue = string.IsNullOrWhiteSpace(queue) ? "default" : queue,
            Priority = JobPriority.Normal,
            Status = JobStatus.Enqueued,
            CreatedAt = DateTimeOffset.UtcNow,
            MaxAttempts = options.MaxAttempts,
            Tags = tags ?? [],
        };
    }
}
