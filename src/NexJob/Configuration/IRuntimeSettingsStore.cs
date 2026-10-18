namespace NexJob.Configuration;

/// <summary>
/// Allows reading and writing NexJob runtime configuration without restarting the application.
/// The dashboard uses this to pause queues, adjust worker counts, and change poll intervals live.
/// </summary>
public interface IRuntimeSettingsStore
{
    /// <summary>Returns the current runtime settings snapshot.</summary>
    Task<RuntimeSettings> GetAsync(CancellationToken ct = default);

    /// <summary>Persists a new runtime settings snapshot.</summary>
    Task SaveAsync(RuntimeSettings settings, CancellationToken ct = default);
}
