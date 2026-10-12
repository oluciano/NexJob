using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Tests;
using Xunit;

namespace NexJob.ReliabilityTests.Validation;

/// <summary>
/// The Kafka trigger against a cluster of three brokers. Run on demand with
/// <c>dotnet test --filter Category=Reliability.Validation</c>.
/// </summary>
[Trait("Category", "Reliability.Validation")]
public sealed class KafkaClusterValidationTests : IClassFixture<KafkaClusterFixture>
{
    private readonly KafkaClusterFixture _fixture;

    public KafkaClusterValidationTests(KafkaClusterFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ThreeBrokers_ABrokerIsLost_TheTriggerKeepsEnqueueingEveryRecord()
    {
        var topic = $"cluster-{Guid.NewGuid():N}";
        await CreateReplicatedTopicAsync(topic);
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(options => options.Workers = 0);
                services.AddKafkaTrigger(options =>
                {
                    options.BootstrapServers = _fixture.BootstrapServers;
                    options.Topic = topic;
                    options.GroupId = $"group-{Guid.NewGuid():N}";
                });
            })
            .Build();
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        await PublishAsync(topic, from: 0, count: 5);
        await TestWait.UntilAsync(
            async () => (await scheduler.GetJobsByTagAsync("trigger:kafka")).Count == 5,
            timeout: TimeSpan.FromSeconds(60),
            because: "the five records before the failure to become jobs");

        await _fixture.StopBrokerAsync(1);
        await PublishAsync(topic, from: 5, count: 5);

        await TestWait.UntilAsync(
            async () => (await scheduler.GetJobsByTagAsync("trigger:kafka")).Count == 10,
            timeout: TimeSpan.FromSeconds(120),
            because: "the five records after a broker was lost to become jobs");
        await host.StopAsync();

        (await scheduler.GetJobsByTagAsync("trigger:kafka")).Should().HaveCount(10, "no record is lost or turned into two jobs");
    }

    private async Task CreateReplicatedTopicAsync(string topic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = _fixture.BootstrapServers }).Build();
        await admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 3, ReplicationFactor = 3 }]);
    }

    private async Task PublishAsync(string topic, int from, int count)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _fixture.BootstrapServers,
            Acks = Acks.All,
        }).Build();
        for (var i = from; i < from + count; i++)
        {
            var headers = new Headers { { "nexjob.job_type", Encoding.UTF8.GetBytes(typeof(ClusterJob).AssemblyQualifiedName!) } };
            await producer.ProduceAsync(topic, new Message<string, string> { Key = $"k{i}", Value = $"v{i}", Headers = headers });
        }
    }

    private sealed class ClusterJob : IJob<string>
    {
        public Task ExecuteAsync(string input, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
