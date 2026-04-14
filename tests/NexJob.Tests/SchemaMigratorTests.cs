using FluentAssertions;
using NexJob.Postgres;
using NexJob.SqlServer;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Unit tests for the pending-migration selection logic shared by
/// <see cref="NexJob.Postgres.SchemaMigrator"/> and <see cref="NexJob.SqlServer.SchemaMigrator"/>.
/// These tests exercise the pure filtering logic without a real database connection.
/// </summary>
public sealed class SchemaMigratorTests
{
    // ─── Postgres.SchemaMigrator.GetPendingMigrations ─────────────────────────

    [Fact]
    public void Postgres_GetPending_AllApplied_ReturnsEmpty()
    {
        var applied = new HashSet<int> { 1, 2, 3, 4, 5, 6, 7, 8 };

        var pending = NexJob.Postgres.SchemaMigrator
            .GetPendingMigrations(NexJob.Postgres.SchemaMigrator.AllMigrations, applied);

        pending.Should().BeEmpty("all migrations are already applied");
    }

    [Fact]
    public void Postgres_GetPending_NoneApplied_ReturnsAll()
    {
        var pending = NexJob.Postgres.SchemaMigrator
            .GetPendingMigrations(NexJob.Postgres.SchemaMigrator.AllMigrations, new HashSet<int>())
            .ToList();

        pending.Should().HaveCount(NexJob.Postgres.SchemaMigrator.AllMigrations.Count,
            "no migrations applied means all are pending");
    }

    [Fact]
    public void Postgres_GetPending_SomeApplied_ReturnsOnlyPending()
    {
        var applied = new HashSet<int> { 1, 2 };

        var pending = NexJob.Postgres.SchemaMigrator
            .GetPendingMigrations(NexJob.Postgres.SchemaMigrator.AllMigrations, applied)
            .ToList();

        pending.Should().HaveCount(6);
        pending.Select(m => m.Version).Should().Equal(3, 4, 5, 6, 7, 8);
    }

    [Fact]
    public void Postgres_GetPending_OutOfOrderApplied_ReturnsCorrect()
    {
        // Versions 1 and 3 applied; 2, 4, 5, 6, 7, 8 missing
        var applied = new HashSet<int> { 1, 3 };

        var pending = NexJob.Postgres.SchemaMigrator
            .GetPendingMigrations(NexJob.Postgres.SchemaMigrator.AllMigrations, applied)
            .ToList();

        pending.Select(m => m.Version).Should().Equal(2, 4, 5, 6, 7, 8);
    }

    [Fact]
    public void Postgres_GetPending_ResultIsAlwaysOrderedByVersion()
    {
        // Even if AllMigrations were out of order, result must be sorted
        var shuffled = NexJob.Postgres.SchemaMigrator.AllMigrations
            .OrderByDescending(m => m.Version)
            .ToList();

        var pending = NexJob.Postgres.SchemaMigrator
            .GetPendingMigrations(shuffled, new HashSet<int>())
            .ToList();

        pending.Select(m => m.Version).Should().BeInAscendingOrder(
            "migrations must always run in version order");
    }

    // ─── SqlServer.SchemaMigrator.GetPendingMigrations ────────────────────────

    [Fact]
    public void SqlServer_GetPending_AllApplied_ReturnsEmpty()
    {
        var applied = new HashSet<int> { 1, 2, 3, 4, 5, 6, 7, 8 };

        var pending = NexJob.SqlServer.SchemaMigrator
            .GetPendingMigrations(NexJob.SqlServer.SchemaMigrator.AllMigrations, applied);

        pending.Should().BeEmpty("all migrations are already applied");
    }

    [Fact]
    public void SqlServer_GetPending_NoneApplied_ReturnsAll()
    {
        var pending = NexJob.SqlServer.SchemaMigrator
            .GetPendingMigrations(NexJob.SqlServer.SchemaMigrator.AllMigrations, new HashSet<int>())
            .ToList();

        pending.Should().HaveCount(NexJob.SqlServer.SchemaMigrator.AllMigrations.Count,
            "no migrations applied means all are pending");
    }

    [Fact]
    public void SqlServer_GetPending_SomeApplied_ReturnsOnlyPending()
    {
        var applied = new HashSet<int> { 1, 2 };

        var pending = NexJob.SqlServer.SchemaMigrator
            .GetPendingMigrations(NexJob.SqlServer.SchemaMigrator.AllMigrations, applied)
            .ToList();

        pending.Should().HaveCount(6);
        pending.Select(m => m.Version).Should().Equal(3, 4, 5, 6, 7, 8);
    }

    [Fact]
    public void SqlServer_GetPending_OutOfOrderApplied_ReturnsCorrect()
    {
        var applied = new HashSet<int> { 1, 3 };

        var pending = NexJob.SqlServer.SchemaMigrator
            .GetPendingMigrations(NexJob.SqlServer.SchemaMigrator.AllMigrations, applied)
            .ToList();

        pending.Select(m => m.Version).Should().Equal(2, 4, 5, 6, 7, 8);
    }

    [Fact]
    public void SqlServer_GetPending_ResultIsAlwaysOrderedByVersion()
    {
        var shuffled = NexJob.SqlServer.SchemaMigrator.AllMigrations
            .OrderByDescending(m => m.Version)
            .ToList();

        var pending = NexJob.SqlServer.SchemaMigrator
            .GetPendingMigrations(shuffled, new HashSet<int>())
            .ToList();

        pending.Select(m => m.Version).Should().BeInAscendingOrder(
            "migrations must always run in version order");
    }
}
