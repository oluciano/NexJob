using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using NexJob.SqlServer;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Operator actions through <see cref="IJobControlService"/> on a host that uses <c>UseDashboardReadReplica</c>. The replica is
/// simulated with a <c>db_datareader</c> login on the same database, which rejects every write the way a replica does.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class SqlServerReadReplicaControlTests : IClassFixture<SqlServerReliabilityFixture>
{
    private readonly SqlServerReliabilityFixture _fixture;

    public SqlServerReadReplicaControlTests(SqlServerReliabilityFixture fixture)
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

    // N2 (Negative): requeue also writes; it must not fail on permission.
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
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexJobSqlServer(_fixture.ConnectionString);
        var login = $"ro_{Guid.NewGuid():N}";
        var replica = new SqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            UserID = login,
            Password = "Ro!Passw0rd-" + Guid.NewGuid().ToString("N")[..8],
            IntegratedSecurity = false,
        };
        services.AddNexJob(o => o.Workers = 0).UseDashboardReadReplica(replica.ConnectionString);
        var provider = services.BuildServiceProvider();

        // The schema is created by the primary's startup migration; the read-only login is created after it exists.
        _ = provider.GetRequiredService<SqlServerStorageProvider>();
        await using var admin = new SqlConnection(_fixture.ConnectionString);
        await admin.OpenAsync();
        await using var create = new SqlCommand(
            $"CREATE LOGIN [{login}] WITH PASSWORD = '{replica.Password}', CHECK_POLICY = OFF; " +
            $"CREATE USER [{login}] FOR LOGIN [{login}]; ALTER ROLE db_datareader ADD MEMBER [{login}];",
            admin);
        await create.ExecuteNonQueryAsync();
        return provider;
    }

    private async Task<int> CountAsync(JobId id)
    {
        await using var conn = new SqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT COUNT(*) FROM nexjob_jobs WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id.Value);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private sealed class NoopJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
