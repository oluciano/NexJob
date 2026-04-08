#pragma warning disable MA0004
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using NexJob.Configuration;

namespace NexJob.SqlServer;

/// <summary>
/// SQL Server-backed implementation of <see cref="IRuntimeSettingsStore"/>.
/// Persists runtime configuration in the <c>nexjob_settings</c> table so that
/// dashboard overrides survive application restarts.
/// </summary>
public sealed class SqlServerRuntimeSettingsStore : IRuntimeSettingsStore
{
    private const string SettingsKey = "runtime_settings";
    private readonly string _connectionString;

    /// <summary>Initializes a new <see cref="SqlServerRuntimeSettingsStore"/>.</summary>
    public SqlServerRuntimeSettingsStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <inheritdoc/>
    public async Task<RuntimeSettings> GetAsync(CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        var json = await conn.ExecuteScalarAsync<string?>(
            "SELECT value FROM nexjob_settings WHERE [key] = @key",
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

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await conn.ExecuteAsync(
            """
            MERGE nexjob_settings WITH (HOLDLOCK) AS target
            USING (SELECT @key AS [key], @value AS [value]) AS source
                ON target.[key] = source.[key]
            WHEN MATCHED THEN
                UPDATE SET [value] = source.[value], updated_at = SYSDATETIMEOFFSET()
            WHEN NOT MATCHED THEN
                INSERT ([key], [value], updated_at)
                VALUES (source.[key], source.[value], SYSDATETIMEOFFSET());
            """,
            new { key = SettingsKey, value = json, }).ConfigureAwait(false);
    }
}
