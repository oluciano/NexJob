namespace NexJob;

/// <summary>
/// Registry for monitoring and observing background event triggers and queue/topic listeners.
/// </summary>
public interface IListenerRegistry
{
    /// <summary>
    /// Registers a listener instance with initial starting status.
    /// </summary>
    /// <param name="registration">The listener metadata registration.</param>
    void Register(ListenerRegistration registration);

    /// <summary>
    /// Updates the operational or connection status of a registered listener.
    /// </summary>
    /// <param name="listenerId">The unique listener ID.</param>
    /// <param name="status">The new status.</param>
    /// <param name="description">Optional status message or exception details.</param>
    void UpdateStatus(string listenerId, ListenerStatus status, string? description = null);

    /// <summary>
    /// Gets a point-in-time snapshot of a registered listener by its ID.
    /// </summary>
    /// <param name="listenerId">The unique listener ID.</param>
    /// <returns>The snapshot if found; otherwise null.</returns>
    ListenerSnapshot? Get(string listenerId);

    /// <summary>
    /// Retrieves snapshots of all registered listeners.
    /// </summary>
    /// <returns>A read-only list of listener snapshots.</returns>
    IReadOnlyList<ListenerSnapshot> GetAll();
}
