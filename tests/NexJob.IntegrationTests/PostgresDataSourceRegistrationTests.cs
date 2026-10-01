using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Configuration;
using NexJob.Postgres;
using Npgsql;
using Xunit;

namespace NexJob.IntegrationTests;

/// <summary>
/// Covers <c>AddNexJobPostgres(NpgsqlDataSource)</c> against a real PostgreSQL that requires a password.
/// <see cref="NpgsqlDataSource.ConnectionString"/> drops the password, so the settings store must use the
/// data source itself instead of rebuilding a connection from that string (issue #308).
/// </summary>
public sealed class PostgresDataSourceRegistrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public PostgresDataSourceRegistrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SettingsStore_RegisteredWithDataSource_RoundTripsOnPasswordProtectedDatabase()
    {
        // N1 (Positive): save then read through the store registered with the data source.
        await using var ds = (await CreateDatabaseAsync()).DataSource;
        using var provider = BuildProvider(ds);
        var store = provider.GetRequiredService<IRuntimeSettingsStore>();

        await store.SaveAsync(new RuntimeSettings { PollingInterval = TimeSpan.FromSeconds(7), });
        var loaded = await store.GetAsync();

        Assert.Equal(TimeSpan.FromSeconds(7), loaded.PollingInterval);
    }

    [Fact]
    public async Task Host_RegisteredWithDataSource_StaysRunning()
    {
        // N2 (Negative path): a failing settings store used to make a background service throw and stop the host.
        await using var ds = (await CreateDatabaseAsync()).DataSource;
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJobPostgres(ds);
                services.AddNexJob(o =>
                {
                    o.Workers = 2;
                    o.PollingInterval = TimeSpan.FromMilliseconds(200);
                });
            })
            .Build();

        var stopped = false;
        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => stopped = true);

        await host.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(2.5));
        var stoppedByItself = stopped;
        await host.StopAsync();

        Assert.False(stoppedByItself, "the host must not stop by itself because of the settings store");
    }

    [Fact]
    public async Task SettingsStore_EmptyTable_ReturnsDefaults()
    {
        // N3 (boundary): nothing saved yet means defaults, with no error.
        await using var ds = (await CreateDatabaseAsync()).DataSource;
        using var provider = BuildProvider(ds);

        var loaded = await provider.GetRequiredService<IRuntimeSettingsStore>().GetAsync();

        Assert.Null(loaded.PollingInterval);
        Assert.Empty(loaded.PausedQueues);
    }

    [Fact]
    public async Task SettingsStore_ConnectionStringConstructor_StillWorks()
    {
        // N3: the public string constructor keeps working for existing users.
        var (dataSource, connectionString) = await CreateDatabaseAsync();
        await using var ds = dataSource;
        var store = new PostgresRuntimeSettingsStore(connectionString);

        await store.SaveAsync(new RuntimeSettings { RecurringJobsPaused = true, });

        Assert.True((await store.GetAsync()).RecurringJobsPaused);
    }

    private static ServiceProvider BuildProvider(NpgsqlDataSource ds)
    {
        var services = new ServiceCollection();
        services.AddNexJobPostgres(ds);
        return services.BuildServiceProvider();
    }

    private async Task<(NpgsqlDataSource DataSource, string ConnectionString)> CreateDatabaseAsync()
    {
        var baseConn = _fixture.Container.GetConnectionString();
        var dbName = $"nexjob_ds_{Guid.NewGuid():N}";

        await using (var admin = new NpgsqlConnection(baseConn))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE {dbName};";
            await cmd.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(baseConn) { Database = dbName, }.ToString();

        // The string constructor runs the schema migrations (the data source constructor does not).
        new PostgresStorageProvider(connectionString);

        return (NpgsqlDataSource.Create(connectionString), connectionString);
    }
}
