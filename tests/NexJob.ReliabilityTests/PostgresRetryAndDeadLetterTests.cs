using Microsoft.Extensions.DependencyInjection;
using NexJob.Postgres;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Retry and dead-letter scenarios on Postgres.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class PostgresRetryAndDeadLetterTests
    : RetryAndDeadLetterScenarios,
      IClassFixture<PostgresReliabilityFixture>
{
    private readonly PostgresReliabilityFixture _fixture;

    public PostgresRetryAndDeadLetterTests(PostgresReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobPostgres(_fixture.ConnectionString);
}
