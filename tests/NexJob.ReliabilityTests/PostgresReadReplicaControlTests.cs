using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexJob.Postgres;
using Npgsql;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Operator actions through <see cref="IJobControlService"/> on a host that uses <c>UseDashboardReadReplica</c>. The replica is
/// simulated with a read-only role on the same database, which rejects every write the way a replica does.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class PostgresReadReplicaControlTests : IClassFixture<PostgresReliabilityFixture>
{
    private readonly PostgresReliabilityFixture _fixture;

    public PostgresReadReplicaControlTests(PostgresReliabilityFixture fixture)
        => _fixture = fixture;

    // N1 (Positive): delete is a write, so it must run on the primary even though the dashboard reads from the replica.
    [Fact]
    public async Task DeleteJobAsync_WithAReadOnlyReplica_DeletesOnThePrimary()
    {
        await using var host = await BuildAsync();
        var id = await host.GetRequiredService<IScheduler>().EnqueueAsync<NoopJob>();

        await host.GetRequiredService<IJobControlService>().DeleteJobAsync(id);

        (await CountAsync(id)).Should().Be(0);
    }

    // N2 (Negative): requeue also writes; a succeeded job must stay untouched and the call must not throw 42501.
    [Fact]
    public async Task RequeueJobAsync_WithAReadOnlyReplica_DoesNotFailOnPermission()
    {
        await using var host = await BuildAsync();
        var id = await host.GetRequiredService<IScheduler>().EnqueueAsync<NoopJob>();

        var act = () => host.GetRequiredService<IJobControlService>().RequeueJobAsync(id);

        await act.Should().NotThrowAsync();
    }

    // N3 (Invalid input): an id that exists nowhere is ignored.
    [Fact]
    public async Task DeleteJobAsync_WithAReadOnlyReplica_IgnoresAnUnknownId()
    {
        await using var host = await BuildAsync();

        var act = () => host.GetRequiredService<IJobControlService>().DeleteJobAsync(new JobId(Guid.NewGuid()));

        await act.Should().NotThrowAsync();
    }

    private async Task<ServiceProvider> BuildAsync()
    {
        var role = $"ro_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await admin.OpenAsync();
            var database = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString).Database;
            await using var grant = new NpgsqlCommand(
                $"CREATE ROLE {role} LOGIN PASSWORD 'ro'; GRANT CONNECT ON DATABASE \"{database}\" TO {role}; GRANT USAGE ON SCHEMA public TO {role};",
                admin);
            await grant.ExecuteNonQueryAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexJobPostgres(_fixture.ConnectionString);
        services.AddNexJob(o => o.Workers = 0).UseDashboardReadReplica(
            new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Username = role, Password = "ro" }.ConnectionString);
        var provider = services.BuildServiceProvider();

        // The schema is created by the primary's startup migration; the read-only role needs SELECT after it exists.
        _ = provider.GetRequiredService<PostgresStorageProvider>(); // resolving the primary runs the migration
        await using (var admin = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await admin.OpenAsync();
            await using var select = new NpgsqlCommand($"GRANT SELECT ON ALL TABLES IN SCHEMA public TO {role};", admin);
            await select.ExecuteNonQueryAsync();
        }

        return provider;
    }

    private async Task<long> CountAsync(JobId id)
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM nexjob_jobs WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id.Value);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private sealed class NoopJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
