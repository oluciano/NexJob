using Dapper;
using Microsoft.Data.SqlClient;

namespace NexJob.SqlServer;

/// <summary>
/// Applies versioned DDL migrations to the SQL Server schema on startup.
/// Uses <c>sp_getapplock</c> to prevent concurrent migration races when multiple
/// instances start simultaneously.
/// </summary>
internal sealed class SchemaMigrator
{
    /// <summary>All versioned migrations in ascending version order.</summary>
    internal static readonly IReadOnlyList<SchemaMigration> AllMigrations =
    [
        new(1, "initial schema", SqlServerSchemaSql.V1CreateTables),
        new(2, "add recurring job config columns", SqlServerSchemaSql.V2AlterRecurring),
        new(3, "add execution_logs and trace_parent columns", SqlServerSchemaSql.V3AddColumns),
        new(4, "create schema_version and recurring_locks tables", SqlServerSchemaSql.V4CreateVersionTable),
    ];

    /// <summary>
    /// Runs all pending migrations against <paramref name="connectionString"/>.
    /// Acquires an application-level lock via <c>sp_getapplock</c>.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public async Task MigrateAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);

        try
        {
            // Acquire exclusive app lock (scoped to transaction)
            await conn.ExecuteAsync(
                "EXEC sp_getapplock @Resource = 'nexjob_migration', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000",
                transaction: tx);

            // Bootstrap version table
            await conn.ExecuteAsync(
                """
                IF OBJECT_ID('nexjob_schema_version', 'U') IS NULL
                CREATE TABLE nexjob_schema_version (
                    version     INT           PRIMARY KEY,
                    applied_at  DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
                    description NVARCHAR(500) NOT NULL
                )
                """,
                transaction: tx);

            var applied = (await conn.QueryAsync<int>(
                "SELECT version FROM nexjob_schema_version", transaction: tx))
                .ToHashSet();

            foreach (var migration in AllMigrations)
            {
                if (applied.Contains(migration.Version))
                {
                    continue;
                }

                // Each migration SQL may contain multiple statements — split on GO
                var statements = migration.Sql
                    .Split(["\nGO\n", "\r\nGO\r\n"], StringSplitOptions.RemoveEmptyEntries);

                foreach (var statement in statements)
                {
                    var trimmed = statement.Trim();
                    if (trimmed.Length > 0)
                    {
                        await conn.ExecuteAsync(trimmed, transaction: tx);
                    }
                }

                await conn.ExecuteAsync(
                    "INSERT INTO nexjob_schema_version (version, description) VALUES (@v, @d)",
                    new { v = migration.Version, d = migration.Description },
                    transaction: tx);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
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
