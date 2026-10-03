using Microsoft.Extensions.DependencyInjection;
using NexJob.Postgres;
using Npgsql;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Upgrade scenarios on PostgreSQL: migration V11 adds <c>expires_at</c> to a populated <c>nexjob_jobs</c>.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class PostgresUpgradeTests
    : UpgradeScenarios,
      IClassFixture<PostgresReliabilityFixture>
{
    private readonly PostgresReliabilityFixture _fixture;

    public PostgresUpgradeTests(PostgresReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobPostgres(_fixture.ConnectionString);

    /// <inheritdoc/>
    protected override async Task MoveBackToPreviousVersionAsync()
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "ALTER TABLE nexjob_jobs DROP COLUMN IF EXISTS expires_at; DELETE FROM nexjob_schema_version WHERE version = 11;",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <inheritdoc/>
    protected override async Task AssertUpgradedAsync()
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        var column = await Scalar(connection, "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'nexjob_jobs' AND column_name = 'expires_at'");
        var version = await Scalar(connection, "SELECT COUNT(*) FROM nexjob_schema_version WHERE version = 11");

        Assert.Equal(1L, column);
        Assert.Equal(1L, version);
    }

    private static async Task<long> Scalar(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
