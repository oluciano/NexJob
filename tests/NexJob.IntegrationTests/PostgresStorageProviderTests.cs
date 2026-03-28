using NexJob.Postgres;
using NexJob.Storage;
using Testcontainers.PostgreSql;
using Xunit;

namespace NexJob.IntegrationTests;

/// <summary>
/// Runs the full <see cref="StorageProviderTestsBase"/> contract against a real
/// PostgreSQL instance spun up via Testcontainers.
/// Requires Docker to be available on the host.
/// </summary>
public sealed class PostgresStorageProviderTests : StorageProviderTestsBase, IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public PostgresStorageProviderTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    protected override async Task<IStorageProvider> CreateStorageAsync()
    {
        var baseConn = _fixture.Container.GetConnectionString();
        var dbName = $"nexjob_{Guid.NewGuid():N}";

        using var conn = new Npgsql.NpgsqlConnection(baseConn);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE {dbName};";
        await cmd.ExecuteNonQueryAsync();

        var builder = new Npgsql.NpgsqlConnectionStringBuilder(baseConn)
        {
            Database = dbName,
        };

        var provider = new PostgresStorageProvider(builder.ConnectionString);

        return provider;
    }
}
