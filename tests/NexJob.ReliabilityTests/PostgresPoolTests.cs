using Microsoft.Extensions.DependencyInjection;
using NexJob.Postgres;
using Npgsql;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// A Postgres connection pool smaller than the number of workers.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class PostgresPoolTests
    : PoolScenarios,
      IClassFixture<PostgresReliabilityFixture>
{
    private readonly PostgresReliabilityFixture _fixture;

    public PostgresPoolTests(PostgresReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> StorageWithPoolOf(int maxPoolSize) =>
        s => s.AddNexJobPostgres(new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { MaxPoolSize = maxPoolSize }.ConnectionString);
}
