using NexJob.Configuration;
using NexJob.Postgres;
using Xunit;

namespace NexJob.IntegrationTests;

/// <summary>
/// Tests <see cref="PostgresRuntimeSettingsStore"/> contract against a real PostgreSQL instance.
/// Requires Docker to be available on the host.
/// </summary>
public sealed class PostgresRuntimeSettingsStoreTests
    : RuntimeSettingsStoreTestsBase, IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public PostgresRuntimeSettingsStoreTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    protected override async Task<IRuntimeSettingsStore> CreateStoreAsync()
    {
        var baseConn = _fixture.Container.GetConnectionString();
        var dbName = $"nexjob_rt_{Guid.NewGuid():N}";

        using var conn = new Npgsql.NpgsqlConnection(baseConn);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE {dbName};";
        await cmd.ExecuteNonQueryAsync();

        var cs = new Npgsql.NpgsqlConnectionStringBuilder(baseConn) { Database = dbName, }.ToString();

        // Create a storage provider to trigger migrations (which will create nexjob_settings table)
        new PostgresStorageProvider(cs);

        return new PostgresRuntimeSettingsStore(cs);
    }
}
