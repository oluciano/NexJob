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
public sealed class PostgresStorageProviderTests : StorageProviderTestsBase, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("nexjob_test")
        .WithUsername("nexjob")
        .WithPassword("nexjob_pw")
        .Build();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    protected override Task<IStorageProvider> CreateStorageAsync() =>
        Task.FromResult<IStorageProvider>(
            new PostgresStorageProvider(_container.GetConnectionString()));
}
