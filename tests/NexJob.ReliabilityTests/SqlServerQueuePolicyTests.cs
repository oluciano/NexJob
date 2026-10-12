using Microsoft.Extensions.DependencyInjection;
using NexJob.SqlServer;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Queue policies on a SQL Server database.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class SqlServerQueuePolicyTests
    : QueuePolicyScenarios,
      IClassFixture<SqlServerReliabilityFixture>
{
    private readonly SqlServerReliabilityFixture _fixture;

    public SqlServerQueuePolicyTests(SqlServerReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobSqlServer(_fixture.ConnectionString);
}
