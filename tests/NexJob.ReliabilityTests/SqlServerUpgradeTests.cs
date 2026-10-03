using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using NexJob.SqlServer;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Upgrade scenarios on SQL Server: migration V11 adds <c>expires_at</c> to a populated <c>nexjob_jobs</c>.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class SqlServerUpgradeTests
    : UpgradeScenarios,
      IClassFixture<SqlServerReliabilityFixture>
{
    private readonly SqlServerReliabilityFixture _fixture;

    public SqlServerUpgradeTests(SqlServerReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobSqlServer(_fixture.ConnectionString);

    /// <inheritdoc/>
    protected override async Task MoveBackToPreviousVersionAsync()
    {
        await using var connection = new SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "IF COL_LENGTH('nexjob_jobs', 'expires_at') IS NOT NULL ALTER TABLE nexjob_jobs DROP COLUMN expires_at; DELETE FROM nexjob_schema_version WHERE version = 11;",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <inheritdoc/>
    protected override async Task AssertUpgradedAsync()
    {
        await using var connection = new SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        var column = await Scalar(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('nexjob_jobs') AND name = 'expires_at'");
        var version = await Scalar(connection, "SELECT COUNT(*) FROM nexjob_schema_version WHERE version = 11");

        Assert.Equal(1, column);
        Assert.Equal(1, version);
    }

    private static async Task<int> Scalar(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }
}
