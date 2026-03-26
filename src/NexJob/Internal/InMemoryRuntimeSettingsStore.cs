using NexJob.Configuration;

namespace NexJob.Internal;

/// <summary>
/// In-process, volatile implementation of <see cref="IRuntimeSettingsStore"/>.
/// State is lost on application restart; suitable for single-instance deployments
/// or when a persistent store (e.g., Redis/DB-backed) is not yet configured.
/// </summary>
internal sealed class InMemoryRuntimeSettingsStore : IRuntimeSettingsStore
{
    private volatile RuntimeSettings _current = new();

    /// <inheritdoc/>
    public Task<RuntimeSettings> GetAsync(CancellationToken ct = default)
        => Task.FromResult(_current);

    /// <inheritdoc/>
    public Task SaveAsync(RuntimeSettings settings, CancellationToken ct = default)
    {
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        _current = settings;
        return Task.CompletedTask;
    }
}
