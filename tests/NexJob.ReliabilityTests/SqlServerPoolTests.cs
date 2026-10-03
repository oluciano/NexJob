using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using NexJob.SqlServer;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// A SqlServer connection pool smaller than the number of workers.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class SqlServerPoolTests
    : PoolScenarios,
      IClassFixture<SqlServerReliabilityFixture>
{
    private readonly SqlServerReliabilityFixture _fixture;

    public SqlServerPoolTests(SqlServerReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> StorageWithPoolOf(int maxPoolSize) =>
        s => s.AddNexJobSqlServer(new SqlConnectionStringBuilder(_fixture.ConnectionString) { MaxPoolSize = maxPoolSize }.ConnectionString);
}
