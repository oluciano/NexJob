using Microsoft.Extensions.DependencyInjection;
using NexJob.SqlServer;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Concurrency scenarios on SqlServer.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class SqlServerConcurrencyTests
    : ConcurrencyScenarios,
      IClassFixture<SqlServerReliabilityFixture>
{
    private readonly SqlServerReliabilityFixture _fixture;

    public SqlServerConcurrencyTests(SqlServerReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobSqlServer(_fixture.ConnectionString);
}
