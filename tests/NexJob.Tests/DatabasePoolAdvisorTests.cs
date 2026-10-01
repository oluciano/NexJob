using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob.Postgres;
using NexJob.SqlServer;
using Xunit;

namespace NexJob.Tests;

public sealed class DatabasePoolAdvisorTests
{
    private const string PostgresBase = "Host=db;Database=nexjob;Username=u;Password=p";
    private const string SqlServerBase = "Server=db;Database=nexjob;User Id=u;Password=p;TrustServerCertificate=True";

    // Npgsql calls the option "Maximum Pool Size", SqlClient calls it "Max Pool Size".
    public static TheoryData<string, string, string> Providers => new()
    {
        { "PostgreSQL", PostgresBase, "Maximum Pool Size" },
        { "SQL Server", SqlServerBase, "Max Pool Size" },
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task DefaultPool_LogsInformationOnly(string provider, string connectionString, string poolKey)
    {
        // N1 (Positive): the default pool (100) covers 10 workers, so the line is informational.
        _ = poolKey;
        var sink = new LevelLogSink();

        await StartAsync(provider, workers: 10, connectionString, sink);

        sink.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Information
            && e.Message.Contains("Maximum Pool Size = 100", StringComparison.Ordinal)
            && e.Message.Contains("Workers = 10", StringComparison.Ordinal));
        sink.Entries.Should().NotContain(e => e.Level == LogLevel.Warning);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task PoolSmallerThanWorkers_LogsWarning(string provider, string connectionString, string poolKey)
    {
        // N2 (Negative path made visible): fewer connections than workers means workers will wait.
        var sink = new LevelLogSink();

        await StartAsync(provider, workers: 30, connectionString + $";{poolKey}=5", sink);

        sink.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning
            && e.Message.Contains("smaller than Workers", StringComparison.Ordinal)
            && e.Message.Contains("(5)", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task NoWorkers_LogsNothing(string provider, string connectionString, string poolKey)
    {
        // N3 (boundary): an ops-only host (Workers = 0) uses no worker connections, so it stays quiet.
        var sink = new LevelLogSink();

        await StartAsync(provider, workers: 0, connectionString + $";{poolKey}=1", sink);

        sink.Entries.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task UnparseableConnectionString_LogsNothingAndDoesNotThrow(string provider, string connectionString, string poolKey)
    {
        // N3 (Invalid Input): the advisor never blocks startup, the provider reports a bad string itself.
        _ = (connectionString, poolKey);
        var sink = new LevelLogSink();

        var act = () => StartAsync(provider, workers: 10, "this is not a connection string", sink);

        await act.Should().NotThrowAsync();
        sink.Entries.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task PoolEqualToWorkers_IsNotAWarning(string provider, string connectionString, string poolKey)
    {
        // N3 (boundary): exactly Workers connections is not smaller than Workers.
        var sink = new LevelLogSink();

        await StartAsync(provider, workers: 20, connectionString + $";{poolKey}=20", sink);

        sink.Entries.Should().NotContain(e => e.Level == LogLevel.Warning);
    }

    private static async Task StartAsync(string provider, int workers, string connectionString, ILoggerProvider sink)
    {
        using var factory = LoggerFactory.Create(b => b.AddProvider(sink));
        var options = new NexJobOptions { Workers = workers, };
        IHostedService advisor = provider == "PostgreSQL"
            ? new PostgresPoolAdvisor(options, factory.CreateLogger<PostgresPoolAdvisor>(), connectionString)
            : new SqlServerPoolAdvisor(options, factory.CreateLogger<SqlServerPoolAdvisor>(), connectionString);

        await advisor.StartAsync(CancellationToken.None);
        await advisor.StopAsync(CancellationToken.None);
    }
}
