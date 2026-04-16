using NexJob.SqlServer;
using NexJob.Storage;
using Testcontainers.MsSql;
using Xunit;

namespace NexJob.IntegrationTests;

/// <summary>
/// Runs the full <see cref="StorageProviderTestsBase"/> contract against a real
/// SQL Server instance spun up via Testcontainers.
/// Requires Docker to be available on the host.
/// </summary>
public sealed class SqlServerStorageProviderTests : StorageProviderTestsBase, IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _fixture;

    public SqlServerStorageProviderTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    // SOBRESCREVA o método que cria o storage para usar um BANCO ÚNICO por teste
    protected override async Task<(IJobStorage Job, IRecurringStorage Recurring, IDashboardStorage Dashboard, IStorageProvider Full)> CreateStorageAsync()
    {
        var baseConn = _fixture.Container.GetConnectionString();
        // Criamos um nome de banco totalmente aleatório para CADA método de teste [Fact]
        var dbName = $"NexJob_{Guid.NewGuid():N}";

        using var conn = new Microsoft.Data.SqlClient.SqlConnection(baseConn);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE [{dbName}];";
        await cmd.ExecuteNonQueryAsync();

        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(baseConn)
        {
            InitialCatalog = dbName,
        };

        var provider = new SqlServerStorageProvider(builder.ConnectionString);
        // O próprio provider deve criar as tabelas no banco novo
        return (provider, provider, provider, provider);
    }
}
