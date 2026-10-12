using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob.Postgres;
using NexJob.Storage;
using Npgsql;
using Xunit;

namespace NexJob.ReliabilityTests.Validation;

/// <summary>
/// The replica behind <c>UseDashboardReadReplica</c> is lost. The fixture is this class's own, because the test stops the
/// replica. Run on demand with <c>dotnet test --filter Category=Reliability.Validation</c>.
/// </summary>
[Trait("Category", "Reliability.Validation")]
public sealed class PostgresReplicaDownValidationTests : DistributedReliabilityTestBase, IClassFixture<PostgresReplicaFixture>
{
    private readonly PostgresReplicaFixture _fixture;

    public PostgresReplicaDownValidationTests(PostgresReplicaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ReplicaLost_TheDashboardFails_ButEnqueueAndExecutionKeepWorkingOnThePrimary()
    {
        var counter = new ExecutionCounter();
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJobPostgres(_fixture.PrimaryConnectionString);
                services.AddNexJob(options =>
                {
                    options.Workers = 2;
                    options.Queues = ["replica-down"];
                    options.PollingInterval = TimeSpan.FromMilliseconds(100);
                }).UseDashboardReadReplica(_fixture.ReplicaConnectionString);
                services.AddTransient<SuccessJob>(sp => new SuccessJob(counter.Increment, sp.GetRequiredService<ILogger<SuccessJob>>()));
            })
            .Build();
        await host.StartAsync();
        await _fixture.StopReplicaAsync();

        var id = await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<SuccessJob>(queue: "replica-down");

        (await WaitForJobStatus(host, id, JobStatus.Succeeded, TimeSpan.FromSeconds(40))).Should().NotBeNull("the workers use the primary");
        var dashboard = () => host.Services.GetRequiredService<IDashboardStorage>().GetMetricsAsync();
        await dashboard.Should().ThrowAsync<NpgsqlException>("the dashboard reads from the lost replica");
        await host.StopAsync();
    }
}
