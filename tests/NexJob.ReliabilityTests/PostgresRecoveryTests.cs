using Microsoft.Extensions.DependencyInjection;
using NexJob.Postgres;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Restart scenarios on Postgres.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class PostgresRecoveryTests
    : RecoveryScenarios,
      IClassFixture<PostgresReliabilityFixture>
{
    private readonly PostgresReliabilityFixture _fixture;

    public PostgresRecoveryTests(PostgresReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobPostgres(_fixture.ConnectionString);
}
