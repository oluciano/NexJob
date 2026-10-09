using Microsoft.Extensions.DependencyInjection;
using NexJob.SqlServer;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>The default queue transition from v5 to v6 on SqlServer.</summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class SqlServerQueuePrefixUpgradeTests
    : QueuePrefixUpgradeScenarios,
      IClassFixture<SqlServerReliabilityFixture>
{
    private readonly SqlServerReliabilityFixture _fixture;

    public SqlServerQueuePrefixUpgradeTests(SqlServerReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobSqlServer(_fixture.ConnectionString);
}
