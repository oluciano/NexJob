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
    private readonly string? _connectionString;
    private readonly NpgsqlDataSource? _dataSource;

    /// <summary>Initializes a new <see cref="PostgresRuntimeSettingsStore"/>.</summary>
    public PostgresRuntimeSettingsStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Initializes a new <see cref="PostgresRuntimeSettingsStore"/> that opens its connections through the caller's
    /// data source, so the password, the pool and any data-source configuration are shared with the provider.
    /// <see cref="NpgsqlDataSource.ConnectionString"/> has the password removed, so it cannot be used instead.
    /// </summary>
    /// <param name="dataSource">The data source used by the Postgres provider.</param>
    internal PostgresRuntimeSettingsStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    /// <inheritdoc/>
    public async Task<RuntimeSettings> GetAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);

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

        await using var conn = await OpenAsync(ct).ConfigureAwait(false);

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

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        if (_dataSource is not null)
        {
            return await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        }

        var conn = new NpgsqlConnection(_connectionString);
        try
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
