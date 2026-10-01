using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Configuration;
using NexJob.Postgres;
using NexJob.Storage;
using Npgsql;
using Xunit;

namespace NexJob.IntegrationTests;

/// <summary>
/// Connection usage of the Postgres registration against a real PostgreSQL (issue #307): NexJob usually shares
/// its database with other applications, so it must use one pool per node and still work with a small pool.
/// </summary>
public sealed class PostgresConnectionUsageTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public PostgresConnectionUsageTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConnectionStringRegistration_UsesOnePoolForProviderAndSettingsStore()
    {
        // N1 (Positive): with Minimum = Maximum Pool Size, one pool keeps exactly that many connections open and
        // two pools would keep twice as many.
        var appName = $"nj307_{Guid.NewGuid():N}";
        var cs = await CreateDatabaseAsync($";Application Name={appName};Minimum Pool Size=3;Maximum Pool Size=3");
        var services = new ServiceCollection();
        services.AddNexJobPostgres(cs);
        await using var provider = services.BuildServiceProvider();

        var storage = provider.GetRequiredService<IDashboardStorage>();
        var store = provider.GetRequiredService<IRuntimeSettingsStore>();
        await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await storage.GetMetricsAsync();
            await store.GetAsync();
        }));
        await Task.Delay(1500);

        var open = await CountConnectionsAsync(appName);
        Assert.True(open <= 3, $"expected at most 3 connections from one pool, found {open}");
    }

    [Fact]
    public async Task Host_PoolFarSmallerThanWorkers_StillCompletesEveryJob()
    {
        // N2 (Negative path): a pool of 3 with 10 workers on a shared database must queue for connections, not hang.
        var cs = await CreateDatabaseAsync(";Maximum Pool Size=3;Timeout=30");
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJobPostgres(cs);
                services.AddNexJob(o =>
                {
                    o.Workers = 10;
                    o.PollingInterval = TimeSpan.FromMilliseconds(100);
                });
                services.AddTransient<QuickJob>();
            })
            .Build();

        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();
        for (var i = 0; i < 100; i++)
        {
            await scheduler.EnqueueAsync<QuickJob>();
        }

        var storage = host.Services.GetRequiredService<IDashboardStorage>();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        long succeeded = 0;
        while (DateTime.UtcNow < deadline && succeeded < 100)
        {
            await Task.Delay(300);
            succeeded = (await storage.GetMetricsAsync()).Succeeded;
        }

        await host.StopAsync();
        Assert.Equal(100, succeeded);
    }

    [Fact]
    public async Task SettingsStore_AfterRegistration_ReadsAndWrites()
    {
        // N3: the shared data source still serves the settings store, and a second resolve returns the same store.
        var cs = await CreateDatabaseAsync(string.Empty);
        var services = new ServiceCollection();
        services.AddNexJobPostgres(cs);
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IRuntimeSettingsStore>();

        await store.SaveAsync(new RuntimeSettings { RecurringJobsPaused = true, });

        Assert.True((await store.GetAsync()).RecurringJobsPaused);
        Assert.Same(store, provider.GetRequiredService<IRuntimeSettingsStore>());
    }

    private async Task<string> CreateDatabaseAsync(string extra)
    {
        var baseConn = _fixture.Container.GetConnectionString();
        var dbName = $"nexjob_use_{Guid.NewGuid():N}";

        await using (var admin = new NpgsqlConnection(baseConn))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE {dbName};";
            await cmd.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(baseConn) { Database = dbName, }.ToString() + extra;
    }

    private async Task<long> CountConnectionsAsync(string applicationName)
    {
        await using var admin = new NpgsqlConnection(_fixture.Container.GetConnectionString() + ";Pooling=false");
        await admin.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE application_name = @app", admin);
        cmd.Parameters.AddWithValue("app", applicationName);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private sealed class QuickJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
