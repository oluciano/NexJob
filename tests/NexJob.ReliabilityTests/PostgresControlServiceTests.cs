using Microsoft.Extensions.DependencyInjection;
using NexJob.Postgres;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Job control service scenarios on Postgres.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class PostgresControlServiceTests
    : ControlServiceScenarios,
      IClassFixture<PostgresReliabilityFixture>
{
    private readonly PostgresReliabilityFixture _fixture;

    public PostgresControlServiceTests(PostgresReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobPostgres(_fixture.ConnectionString);
}
