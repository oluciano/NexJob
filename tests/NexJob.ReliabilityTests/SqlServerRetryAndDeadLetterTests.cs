using Microsoft.Extensions.DependencyInjection;
using NexJob.SqlServer;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Retry and dead-letter scenarios on SqlServer.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class SqlServerRetryAndDeadLetterTests
    : RetryAndDeadLetterScenarios,
      IClassFixture<SqlServerReliabilityFixture>
{
    private readonly SqlServerReliabilityFixture _fixture;

    public SqlServerRetryAndDeadLetterTests(SqlServerReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobSqlServer(_fixture.ConnectionString);
}
