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
/// What NexJob.Redis does against a primary with one replica (not in cluster mode). Run on demand with
/// <c>dotnet test --filter Category=Reliability.Validation</c>; this is not part of the pull request pipeline.
/// </summary>
[Trait("Category", "Reliability.Validation")]
public sealed class RedisReplicaValidationTests : DistributedReliabilityTestBase, IClassFixture<RedisReplicaFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly RedisReplicaFixture _fixture;

    public RedisReplicaValidationTests(RedisReplicaFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PrimaryAndReplicaEndpoints_RunTheJobOnce_WhicheverIsListedFirst(bool primaryFirst)
    {
        var endpoints = primaryFirst
            ? $"{_fixture.PrimaryEndpoint},{_fixture.ReplicaEndpoint}"
            : $"{_fixture.ReplicaEndpoint},{_fixture.PrimaryEndpoint}";
        var counter = new ExecutionCounter();
        using var host = BuildHost(s => s.AddNexJobRedis(endpoints), Jobs(counter));
        await host.StartAsync();

        var id = await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<SuccessJob>();

        (await WaitForAllSucceeded(host, [id], Timeout)).Should().BeTrue("writes go to the primary even when the replica is listed first");
        counter.Count.Should().Be(1);
        await host.StopAsync();
    }

    [Fact]
    public async Task ReplicaEndpointOnly_StartsButEveryWriteFailsWithReadonly()
    {
        var counter = new ExecutionCounter();
        using var host = BuildHost(s => s.AddNexJobRedis(_fixture.ReplicaEndpoint), Jobs(counter));
        await host.StartAsync();

        var enqueue = () => host.Services.GetRequiredService<IScheduler>().EnqueueAsync<SuccessJob>();

        (await enqueue.Should().ThrowAsync<RedisServerException>()).Which.Message.Should().Contain("READONLY");
        counter.Count.Should().Be(0);
        await host.StopAsync();
    }

    private static Action<IServiceCollection> Jobs(ExecutionCounter counter) =>
        s => s.AddTransient<SuccessJob>(sp => new SuccessJob(counter.Increment, sp.GetRequiredService<ILogger<SuccessJob>>()));
}
