using Microsoft.Extensions.DependencyInjection;
using NexJob.Postgres;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Connections seen by Postgres while NexJob works.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class PostgresConnectionTests
    : ConnectionCountScenarios,
      IClassFixture<PostgresReliabilityFixture>
{
    private readonly PostgresReliabilityFixture _fixture;

    public PostgresConnectionTests(PostgresReliabilityFixture fixture, ITestOutputHelper output)
        : base(output)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobPostgres(_fixture.ConnectionString);

    /// <inheritdoc/>
    protected override void ResetClientPools() => NpgsqlConnection.ClearAllPools();

    /// <inheritdoc/>
    protected override async Task<int> CountServerConnectionsAsync()
    {
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*)::int FROM pg_stat_activity WHERE datname = current_database() AND backend_type = 'client backend'", connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }
}
