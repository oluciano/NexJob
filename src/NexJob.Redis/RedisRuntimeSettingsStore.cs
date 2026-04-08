using System.Text.Json;
using NexJob.Configuration;
using StackExchange.Redis;

namespace NexJob.Redis;

/// <summary>
/// Redis-backed implementation of <see cref="IRuntimeSettingsStore"/>.
/// Persists runtime configuration as a JSON string at <c>nexjob:settings:runtime</c>
/// so that dashboard overrides survive application restarts.
/// </summary>
public sealed class RedisRuntimeSettingsStore : IRuntimeSettingsStore
{
    private const string SettingsKey = "nexjob:settings:runtime";
    private readonly IDatabase _db;

    /// <summary>Initializes a new <see cref="RedisRuntimeSettingsStore"/>.</summary>
    public RedisRuntimeSettingsStore(IDatabase db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public async Task<RuntimeSettings> GetAsync(CancellationToken ct = default)
    {
        var json = await _db.StringGetAsync(SettingsKey).ConfigureAwait(false);

        return json.IsNullOrEmpty
            ? new RuntimeSettings()
            : JsonSerializer.Deserialize<RuntimeSettings>(json.ToString()) ?? new RuntimeSettings();
    }

    /// <inheritdoc/>
    public async Task SaveAsync(RuntimeSettings settings, CancellationToken ct = default)
    {
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(settings);
        await _db.StringSetAsync(SettingsKey, json).ConfigureAwait(false);
    }
}
