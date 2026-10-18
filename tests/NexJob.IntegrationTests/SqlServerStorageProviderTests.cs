using FluentAssertions;
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

    // Issue #587: the constructor migrates the schema synchronously. On a host with a SynchronizationContext every
    // await in the migration must leave it (ConfigureAwait(false)), or the blocked constructor waits for itself.
    [Fact]
    public async Task Constructor_OnHostWithSynchronizationContext_MigratesWithoutDeadlock()
    {
        var baseConn = _fixture.Container.GetConnectionString();
        var dbName = $"NexJob_{Guid.NewGuid():N}";
        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(baseConn))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE [{dbName}];";
            await cmd.ExecuteNonQueryAsync();
        }

        var connectionString = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(baseConn) { InitialCatalog = dbName }.ConnectionString;

        var first = SingleThreadedSynchronizationContext.Run(() => new SqlServerStorageProvider(connectionString), TimeSpan.FromSeconds(30));
        (await first.GetJobCatalogAsync(default)).Should().BeEmpty("the schema was created and is queryable");

        // N3: the second construction finds an up-to-date schema and migrates nothing.
        var second = SingleThreadedSynchronizationContext.Run(() => new SqlServerStorageProvider(connectionString), TimeSpan.FromSeconds(30));
        (await second.GetJobCatalogAsync(default)).Should().BeEmpty();
    }

    /// <inheritdoc/>
    protected override bool QueueNamesAreCaseSensitive => false;
}
