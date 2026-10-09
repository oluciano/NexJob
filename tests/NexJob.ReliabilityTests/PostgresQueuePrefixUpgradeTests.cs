using Microsoft.Extensions.DependencyInjection;
using NexJob.Postgres;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>The default queue transition from v5 to v6 on Postgres.</summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class PostgresQueuePrefixUpgradeTests
    : QueuePrefixUpgradeScenarios,
      IClassFixture<PostgresReliabilityFixture>
{
    private readonly PostgresReliabilityFixture _fixture;

    public PostgresQueuePrefixUpgradeTests(PostgresReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobPostgres(_fixture.ConnectionString);
}
