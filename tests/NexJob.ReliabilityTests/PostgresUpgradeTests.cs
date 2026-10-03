using FluentAssertions;
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

    [Fact]
    public async Task JobInsertedWithTheColumnListOfVersion57_IsReadAndRunByTheNewVersion()
    {
        // Mixed version: a 5.7 node knows nothing about expires_at, so its INSERT leaves it out.
        var queue = $"upgrade-old-insert-{Guid.NewGuid():N}";
        var log = new ExecutionLog();
        using var host = BuildHost(Storage(), s => s.AddSingleton(log).AddTransient<StepJob>(), workers: 1, queues: [queue]);
        _ = host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>(); // applies the migrations, V11 included
        var id = Guid.NewGuid();

        await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO nexjob_jobs
                    (id, job_type, input_type, input_json, schema_version, queue, priority, status,
                     idempotency_key, attempts, max_attempts, created_at, scheduled_at, parent_job_id, recurring_job_id, tags)
                VALUES
                    (@id, @jobType, @inputType, '{}'::jsonb, 1, @queue, 3, 'Enqueued', NULL, 0, 3, NOW(), NULL, NULL, NULL, '{}')
                """,
                connection);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("jobType", typeof(StepJob).AssemblyQualifiedName!);
            command.Parameters.AddWithValue("inputType", typeof(NexJob.Internal.NoInput).AssemblyQualifiedName!);
            command.Parameters.AddWithValue("queue", queue);
            await command.ExecuteNonQueryAsync();
        }

        await host.StartAsync();

        (await WaitForJobStatus(host, new JobId(id), JobStatus.Succeeded, TimeSpan.FromSeconds(30))).Should().NotBeNull("the new version runs what the old one wrote");
        log.Count($"done:{id}").Should().Be(1);
        await host.StopAsync();
    }

    private static async Task<long> Scalar(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
