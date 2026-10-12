using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob.Postgres;
using NexJob.Storage;
using NexJob.Tests;
using Npgsql;
using Xunit;

namespace NexJob.ReliabilityTests.Validation;

/// <summary>
/// <c>UseDashboardReadReplica</c> against a real PostgreSQL streaming replica. Run on demand with
/// <c>dotnet test --filter Category=Reliability.Validation</c>.
/// </summary>
[Trait("Category", "Reliability.Validation")]
public sealed class PostgresReplicaValidationTests : IClassFixture<PostgresReplicaFixture>
{
    private readonly PostgresReplicaFixture _fixture;

    public PostgresReplicaValidationTests(PostgresReplicaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TheReplicaRejectsWrites_ButTheDashboardSeesJobsRunOnThePrimary()
    {
        var counter = new ExecutionCounter();
        using var host = BuildHost(counter);
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var dashboard = host.Services.GetRequiredService<IDashboardStorage>();

        for (var i = 0; i < 5; i++)
        {
            await scheduler.EnqueueAsync<SuccessJob>(queue: "replica-validation");
        }

        await TestWait.UntilAsync(
            async () => (await dashboard.GetMetricsAsync()).Succeeded == 5,
            timeout: TimeSpan.FromSeconds(60),
            because: "the five succeeded jobs to reach the replica the dashboard reads from");
        await host.StopAsync();

        counter.Count.Should().Be(5);
        await using var replica = new NpgsqlConnection(_fixture.ReplicaConnectionString);
        await replica.OpenAsync();
        await using var write = new NpgsqlCommand("CREATE TABLE should_fail (id int)", replica);
        var attempt = () => write.ExecuteNonQueryAsync();
        (await attempt.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("25006", "the standby is read-only");
    }

    private IHost BuildHost(ExecutionCounter counter) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJobPostgres(_fixture.PrimaryConnectionString);
                services.AddNexJob(options =>
                {
                    options.Workers = 2;
                    options.Queues = ["replica-validation"];
                    options.PollingInterval = TimeSpan.FromMilliseconds(100);
                }).UseDashboardReadReplica(_fixture.ReplicaConnectionString);
                services.AddTransient<SuccessJob>(sp => new SuccessJob(counter.Increment, sp.GetRequiredService<ILogger<SuccessJob>>()));
            })
            .Build();
}
