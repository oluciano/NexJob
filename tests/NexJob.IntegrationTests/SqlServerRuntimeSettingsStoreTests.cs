using NexJob.Configuration;
using NexJob.SqlServer;
using Xunit;

namespace NexJob.IntegrationTests;

/// <summary>
/// Tests <see cref="SqlServerRuntimeSettingsStore"/> contract against a real SQL Server instance.
/// Requires Docker to be available on the host.
/// </summary>
public sealed class SqlServerRuntimeSettingsStoreTests
    : RuntimeSettingsStoreTestsBase, IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _fixture;

    public SqlServerRuntimeSettingsStoreTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    protected override async Task<IRuntimeSettingsStore> CreateStoreAsync()
    {
        var baseConn = _fixture.Container.GetConnectionString();
        var dbName = $"NexJob_RT_{Guid.NewGuid():N}";

        using var conn = new Microsoft.Data.SqlClient.SqlConnection(baseConn);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE [{dbName}];";
        await cmd.ExecuteNonQueryAsync();

        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(baseConn)
        {
            InitialCatalog = dbName,
        };

        var cs = builder.ConnectionString;

        // Create a storage provider to trigger migrations (which will create nexjob_settings table)
        new SqlServerStorageProvider(cs);

        return new SqlServerRuntimeSettingsStore(cs);
    }
}
