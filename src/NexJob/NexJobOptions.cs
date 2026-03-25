namespace NexJob;

/// <summary>
/// Configuration options for the NexJob background job system.
/// Pass an <see cref="Action{NexJobOptions}"/> to
/// <see cref="NexJobServiceCollectionExtensions.AddNexJob"/> to customise these values.
/// </summary>
public sealed class NexJobOptions
{
    /// <summary>
    /// Maximum number of jobs that can execute concurrently on this host.
    /// Defaults to <c>10</c>.
    /// </summary>
    public int Workers { get; set; } = 10;

    /// <summary>
    /// Maximum number of execution attempts before a job is moved to the dead-letter
    /// (failed) state. Defaults to <c>10</c>.
    /// </summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>
    /// How often the dispatcher polls for new jobs when none are immediately available.
    /// Defaults to <c>15 seconds</c>.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How often active workers refresh their heartbeat timestamp.
    /// Defaults to <c>30 seconds</c>.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum time allowed between heartbeat updates before a job is considered
    /// orphaned and re-enqueued. Defaults to <c>5 minutes</c>.
    /// </summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Ordered list of queue names that workers on this host will poll.
    /// Queues are drained in the order specified. Defaults to <c>["default"]</c>.
    /// </summary>
    public IReadOnlyList<string> Queues { get; set; } = ["default"];

    /// <summary>
    /// Computes the retry delay for a failed job given the attempt number (1-based).
    /// Defaults to exponential backoff: <c>pow(attempt, 4) + 15 + rand(30) × (attempt + 1)</c> seconds.
    /// Override in tests or when you need a different backoff strategy.
    /// </summary>
    public Func<int, TimeSpan> RetryDelayFactory { get; set; } = attempt =>
        TimeSpan.FromSeconds(Math.Pow(attempt, 4) + 15 + (Random.Shared.Next(30) * (attempt + 1)));
}
