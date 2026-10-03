using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using NexJob.SqlServer;
using Xunit;
using Xunit.Abstractions;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Connections seen by SqlServer while NexJob works.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class SqlServerConnectionTests
    : ConnectionCountScenarios,
      IClassFixture<SqlServerReliabilityFixture>
{
    private readonly SqlServerReliabilityFixture _fixture;

    public SqlServerConnectionTests(SqlServerReliabilityFixture fixture, ITestOutputHelper output)
        : base(output)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobSqlServer(_fixture.ConnectionString);

    /// <inheritdoc/>
    protected override void ResetClientPools() => SqlConnection.ClearAllPools();

    /// <inheritdoc/>
    protected override async Task<int> CountServerConnectionsAsync()
    {
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(_fixture.ConnectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE is_user_process = 1", connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }
}
