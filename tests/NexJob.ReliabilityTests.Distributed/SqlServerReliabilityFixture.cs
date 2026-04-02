using Testcontainers.MsSql;
using Xunit;

namespace NexJob.ReliabilityTests.Distributed;

/// <summary>
/// Fixture for SQL Server container with connection string access.
/// </summary>
public sealed class SqlServerReliabilityFixture : IAsyncLifetime
{
    public MsSqlContainer Container { get; } = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
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
