using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob.Redis;
using NexJob.Storage;
using StackExchange.Redis;
using Xunit;

namespace NexJob.ReliabilityTests.Validation;

/// <summary>
/// A primary with a replica loses its primary and an operator promotes the replica. The fixture is this class's own,
/// because the test destroys the topology the other validation tests need. Run on demand with
/// <c>dotnet test --filter Category=Reliability.Validation</c>.
/// </summary>
[Trait("Category", "Reliability.Validation")]
public sealed class RedisReplicaFailoverValidationTests : DistributedReliabilityTestBase, IClassFixture<RedisReplicaFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly RedisReplicaFixture _fixture;

    public RedisReplicaFailoverValidationTests(RedisReplicaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PrimaryLost_ReplicaPromotedByAnOperator_NexJobKeepsItsJobsAndRunsNewOnes()
    {
        var counter = new ExecutionCounter();
        using var host = BuildHost(s => s.AddNexJobRedis($"{_fixture.PrimaryEndpoint},{_fixture.ReplicaEndpoint}"), Jobs(counter));
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var first = await scheduler.EnqueueAsync<SuccessJob>();
        (await WaitForAllSucceeded(host, [first], Timeout)).Should().BeTrue();

        await _fixture.StopPrimaryAsync();
        using var admin = await ConnectionMultiplexer.ConnectAsync($"{_fixture.ReplicaEndpoint},allowAdmin=true");
        await admin.GetServer(_fixture.ReplicaEndpoint).ExecuteAsync("REPLICAOF", "NO", "ONE");

        var second = await scheduler.EnqueueAsync<SuccessJob>();

        (await WaitForAllSucceeded(host, [second], TimeSpan.FromSeconds(45))).Should().BeTrue("the client finds the promoted replica");
        var kept = await host.Services.GetRequiredService<IStorageProvider>().GetJobByIdAsync(first);
        kept!.Status.Should().Be(JobStatus.Succeeded, "the first job was replicated before the primary was lost");
        counter.Count.Should().Be(2);
        await host.StopAsync();
    }

    private static Action<IServiceCollection> Jobs(ExecutionCounter counter) =>
        s => s.AddTransient<SuccessJob>(sp => new SuccessJob(counter.Increment, sp.GetRequiredService<ILogger<SuccessJob>>()));
}
