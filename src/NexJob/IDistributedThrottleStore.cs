namespace NexJob;

/// <summary>
/// Opt-in backend for global throttle enforcement across multiple worker nodes.
/// When not registered, throttling remains per-instance (default behavior).
/// </summary>
public interface IDistributedThrottleStore
{
    /// <summary>
    /// Attempts to acquire a throttle slot for the given resource.
    /// Returns true if the slot was acquired (job may proceed).
    /// Returns false if the limit is already reached globally.
    /// </summary>
    /// <param name="resource">The unique name of the throttled resource.</param>
    /// <param name="maxConcurrent">The maximum number of concurrent executions allowed globally.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>True if acquired; otherwise false.</returns>
    Task<bool> TryAcquireAsync(string resource, int maxConcurrent, CancellationToken ct = default);

    /// <summary>
    /// Releases a previously acquired throttle slot.
    /// Must be called after job completion (success or failure).
    /// </summary>
    /// <param name="resource">The unique name of the throttled resource.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task ReleaseAsync(string resource, CancellationToken ct = default);
}
