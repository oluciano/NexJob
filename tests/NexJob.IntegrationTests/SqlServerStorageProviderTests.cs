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
public sealed class SqlServerStorageProviderTests : StorageProviderTestsBase, IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    protected override Task<IStorageProvider> CreateStorageAsync() =>
        Task.FromResult<IStorageProvider>(
            new SqlServerStorageProvider(_container.GetConnectionString()));
}
