using Microsoft.Extensions.DependencyInjection;
using NexJob.Postgres;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// A failing result commit on a PostgreSQL database.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class PostgresCommitFailureTests
    : CommitFailureScenarios,
      IClassFixture<PostgresReliabilityFixture>
{
    private readonly PostgresReliabilityFixture _fixture;

    public PostgresCommitFailureTests(PostgresReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobPostgres(_fixture.ConnectionString);
}
