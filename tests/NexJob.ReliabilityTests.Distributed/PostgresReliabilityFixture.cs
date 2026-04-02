using Testcontainers.PostgreSql;
using Xunit;

namespace NexJob.ReliabilityTests.Distributed;

/// <summary>
/// Fixture for Postgres container with connection string access.
/// </summary>
public sealed class PostgresReliabilityFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => Container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await Container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await Container.DisposeAsync();
    }
}
