using Dapper;
using Npgsql;

namespace NexJob.Postgres;

/// <summary>
/// Applies versioned DDL migrations to the PostgreSQL schema on startup.
/// Uses <c>pg_advisory_lock</c> to prevent concurrent migration races when multiple
/// instances start simultaneously.
/// </summary>
internal sealed class SchemaMigrator
{
    /// <summary>All versioned migrations in ascending version order.</summary>
    internal static readonly IReadOnlyList<SchemaMigration> AllMigrations =
    [
        new(1, "initial schema", SchemaSql.V1CreateTables),
        new(2, "add recurring job config columns", SchemaSql.V2AlterRecurring),
        new(3, "add execution_logs and trace_parent columns", SchemaSql.V3AddColumns),
        new(4, "create schema_version and recurring_locks tables", SchemaSql.V4CreateVersionTable),
        new(5, "add progress_percent, progress_message, tags columns", SchemaSql.V5AddProgressAndTags),
    ];

    // Arbitrary but stable numeric key for pg_advisory_lock: hash of 'nexjob'
    private const long AdvisoryLockKey = 7_242_374_305L;

    /// <summary>
    /// Runs all pending migrations against <paramref name="connectionString"/>.
    /// Acquires a PostgreSQL advisory lock so only one instance migrates at a time.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public async Task MigrateAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        // Acquire advisory lock — blocks until acquired
        await conn.ExecuteAsync($"SELECT pg_advisory_lock({AdvisoryLockKey})");

        try
        {
            // Ensure version table exists (bootstrap: first ever run)
            await conn.ExecuteAsync(
                """
                CREATE TABLE IF NOT EXISTS nexjob_schema_version (
                    version     INT         PRIMARY KEY,
                    applied_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    description TEXT        NOT NULL
                )
                """);

            var applied = (await conn.QueryAsync<int>(
                "SELECT version FROM nexjob_schema_version"))
                .ToHashSet();

            foreach (var migration in AllMigrations)
            {
                if (applied.Contains(migration.Version))
                {
                    continue;
                }

                await using var tx = await conn.BeginTransactionAsync(ct);
                try
                {
                    await conn.ExecuteAsync(migration.Sql, transaction: tx);
                    await conn.ExecuteAsync(
                        "INSERT INTO nexjob_schema_version (version, description) VALUES (@v, @d)",
                        new { v = migration.Version, d = migration.Description },
                        transaction: tx);
                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }
        }
        finally
        {
            await conn.ExecuteAsync($"SELECT pg_advisory_unlock({AdvisoryLockKey})");
        }
    }

    /// <summary>
    /// Returns the subset of <paramref name="all"/> whose version is not in <paramref name="applied"/>,
    /// ordered by version ascending. Exposed for unit testing without a real database connection.
    /// </summary>
    /// <param name="all">Full ordered list of known migrations.</param>
    /// <param name="applied">Set of already-applied version numbers.</param>
    internal static IEnumerable<SchemaMigration> GetPendingMigrations(
        IReadOnlyList<SchemaMigration> all,
        IReadOnlySet<int> applied)
        => all.Where(m => !applied.Contains(m.Version))
              .OrderBy(m => m.Version);
}
