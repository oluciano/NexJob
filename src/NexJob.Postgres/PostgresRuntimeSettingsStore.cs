#pragma warning disable MA0004
using System.Text.Json;
using Dapper;
using NexJob.Configuration;
using Npgsql;

namespace NexJob.Postgres;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="IRuntimeSettingsStore"/>.
/// Persists runtime configuration in the <c>nexjob_settings</c> table so that
/// dashboard overrides survive application restarts.
/// </summary>
public sealed class PostgresRuntimeSettingsStore : IRuntimeSettingsStore
{
    private const string SettingsKey = "runtime_settings";
    private readonly string _connectionString;

    /// <summary>Initializes a new <see cref="PostgresRuntimeSettingsStore"/>.</summary>
    public PostgresRuntimeSettingsStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <inheritdoc/>
    public async Task<RuntimeSettings> GetAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        var json = await conn.ExecuteScalarAsync<string?>(
            "SELECT value FROM nexjob_settings WHERE key = @key",
            new { key = SettingsKey, }).ConfigureAwait(false);

        return json is null
            ? new RuntimeSettings()
            : JsonSerializer.Deserialize<RuntimeSettings>(json) ?? new RuntimeSettings();
    }

    /// <inheritdoc/>
    public async Task SaveAsync(RuntimeSettings settings, CancellationToken ct = default)
    {
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(settings);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await conn.ExecuteAsync(
            """
            INSERT INTO nexjob_settings (key, value, updated_at)
            VALUES (@key, @value, NOW())
            ON CONFLICT (key) DO UPDATE
                SET value = EXCLUDED.value,
                    updated_at = EXCLUDED.updated_at
            """,
            new { key = SettingsKey, value = json, }).ConfigureAwait(false);
    }
}
