using Microsoft.Extensions.DependencyInjection;
using NexJob.SqlServer;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Restart scenarios on SqlServer.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class SqlServerRecoveryTests
    : RecoveryScenarios,
      IClassFixture<SqlServerReliabilityFixture>
{
    private readonly SqlServerReliabilityFixture _fixture;

    public SqlServerRecoveryTests(SqlServerReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobSqlServer(_fixture.ConnectionString);
}
