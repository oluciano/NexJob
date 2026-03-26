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

/// <summary>
/// Mutable runtime configuration that overrides <see cref="NexJobOptions"/> values
/// without requiring an application restart.
/// </summary>
public sealed class RuntimeSettings
{
    /// <summary>Override global worker count. <see langword="null"/> = use appsettings/code value.</summary>
    public int? Workers { get; set; }

    /// <summary>Queues that are administratively paused. Workers skip these queues entirely.</summary>
    public HashSet<string> PausedQueues { get; set; } = [];

    /// <summary>When <see langword="true"/>, the recurring job scheduler does not enqueue any jobs.</summary>
    public bool RecurringJobsPaused { get; set; }

    /// <summary>Override polling interval. <see langword="null"/> = use appsettings/code value.</summary>
    public TimeSpan? PollingInterval { get; set; }

    /// <summary>Timestamp of the last save, set automatically by the store.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
