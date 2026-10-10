using FluentAssertions;
using NexJob.Postgres;
using NexJob.Storage;
using Testcontainers.PostgreSql;
using Xunit;

namespace NexJob.IntegrationTests;

/// <summary>
/// Runs the full <see cref="StorageProviderTestsBase"/> contract against a real
/// PostgreSQL instance spun up via Testcontainers.
/// Requires Docker to be available on the host.
/// </summary>
public sealed class PostgresStorageProviderTests : StorageProviderTestsBase, IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;
    private string _connectionString = string.Empty;

    public PostgresStorageProviderTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    protected override async Task<(IJobStorage Job, IRecurringStorage Recurring, IDashboardStorage Dashboard, IStorageProvider Full)> CreateStorageAsync()
    {
        var baseConn = _fixture.Container.GetConnectionString();
        var dbName = $"nexjob_{Guid.NewGuid():N}";

        using var conn = new Npgsql.NpgsqlConnection(baseConn);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE {dbName};";
        await cmd.ExecuteNonQueryAsync();

        var builder = new Npgsql.NpgsqlConnectionStringBuilder(baseConn)
        {
            Database = dbName,

            // A provider (and its pool) is created per test and never disposed; close idle connections quickly
            // so the growing contract suite does not exhaust the container's max_connections.
            ConnectionIdleLifetime = 5,
            ConnectionPruningInterval = 1,
            MaxPoolSize = 10,
        };

        _connectionString = builder.ConnectionString;
        var provider = new PostgresStorageProvider(builder.ConnectionString);

        return (provider, provider, provider, provider);
    }

    [Fact]
    public async Task FetchBatchAsync_with_several_queues_reads_the_fetch_index_not_the_whole_backlog()
    {
        var (storage, _, _, _) = await CreateStorageAsync();
        await using var conn = new Npgsql.NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // A backlog large enough that a sequential scan is the cheaper plan for a query that cannot use the index.
        await using (var seed = conn.CreateCommand())
        {
            seed.CommandText =
                """
                INSERT INTO nexjob_jobs (id, job_type, input_type, input_json, queue, priority, status, created_at)
                SELECT gen_random_uuid(), 'T', 'I', '{}'::jsonb, 'q' || (g % 2), 3, 'Enqueued', NOW() + g * interval '1 millisecond'
                FROM generate_series(1, 20000) g;
                ANALYZE nexjob_jobs;
                """;
            await seed.ExecuteNonQueryAsync();
        }

        var before = await ReadFetchIndexScansAsync(conn);

        (await storage.FetchBatchAsync(["q0", "q1"], 5)).Should().HaveCount(5);

        // Statistics reach other sessions after a short delay: wait for the observed value, not a fixed time.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        long after;
        do
        {
            after = await ReadFetchIndexScansAsync(conn);
            if (after > before)
            {
                break;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        after.Should().BeGreaterThan(before, "the fetch must walk idx_nexjob_jobs_fetch instead of sorting every Enqueued row");
    }

    private static async Task<long> ReadFetchIndexScansAsync(Npgsql.NpgsqlConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(idx_scan), 0) FROM pg_stat_user_indexes WHERE indexrelname = 'idx_nexjob_jobs_fetch'";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
