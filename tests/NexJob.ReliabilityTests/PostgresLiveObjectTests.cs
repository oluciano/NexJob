using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Postgres;
using Npgsql;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// <c>AddNexJobPostgres(NpgsqlDataSource)</c> against a real, password-protected PostgreSQL.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class PostgresLiveObjectTests
    : LiveObjectOverloadScenarios,
      IClassFixture<PostgresReliabilityFixture>
{
    private readonly PostgresReliabilityFixture _fixture;

    public PostgresLiveObjectTests(PostgresReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override (Action<IServiceCollection> Register, IAsyncDisposable Owned) CreateOwnedObject()
    {
        var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        return (s => s.AddNexJobPostgres(dataSource), dataSource);
    }

    /// <inheritdoc/>
    protected override async Task AssertStillUsableAsync(IAsyncDisposable owned)
    {
        var dataSource = (NpgsqlDataSource)owned;
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        Assert.Equal(1, (int)(await command.ExecuteScalarAsync())!);
    }

    // N2 (Negative): the constructor stays free of DDL, because the read replica of the dashboard is built with it and a replica
    // must never be asked to create tables. Only the registration of the primary database migrates.
    [Fact]
    public async Task DataSourceConstructor_DoesNotCreateTables_SoAReadReplicaNeverRunsDdl()
    {
        var database = $"replica_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = database };
        await using var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);

        using var provider = new PostgresStorageProvider(dataSource);

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("SELECT to_regclass('public.nexjob_jobs') IS NULL", connection);
        ((bool)(await command.ExecuteScalarAsync())!).Should().BeTrue("building the provider from a data source must not run migrations");
    }

    // N3: the schema has to exist before any other hosted service starts, whatever the registration order. Here NexJob's own
    // services are registered before the storage.
    [Fact]
    public async Task DataSourceOverload_CreatesTheSchemaBeforeTheDispatcher_EvenWhenNexJobIsRegisteredFirst()
    {
        var database = $"order_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = database };
        await using var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        var queue = $"order-{Guid.NewGuid():N}";
        var counter = new ExecutionCounter();
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(options =>
                {
                    options.Workers = 1;
                    options.Queues = [queue];
                    options.PollingInterval = TimeSpan.FromMilliseconds(100);
                });
                services.AddNexJobPostgres(dataSource); // after AddNexJob on purpose
                services.AddTransient<SuccessJob>(sp => new SuccessJob(counter.Increment, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SuccessJob>>()));
            })
            .Build();

        await host.StartAsync();
        var jobId = await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<SuccessJob>(queue: queue);

        (await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(30))).Should().NotBeNull();
        await host.StopAsync();
    }
}
