using System.Collections.Concurrent;
using System.Text;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NexJob.Storage;
using NexJob.Tests;
using Testcontainers.Kafka;
using Xunit;

namespace NexJob.ReliabilityTests.Validation;

/// <summary>
/// What the Kafka trigger does when storage stays down for longer than the consumer's <c>max.poll.interval.ms</c>.
/// Run on demand with <c>dotnet test --filter Category=Reliability.Validation</c>.
/// </summary>
[Trait("Category", "Reliability.Validation")]
public sealed class KafkaEvictionValidationTests : IAsyncLifetime
{
    private readonly KafkaContainer _kafka = new KafkaBuilder().WithImage("confluentinc/cp-kafka:7.6.0").Build();
    public Task InitializeAsync() => _kafka.StartAsync();

    public Task DisposeAsync() => _kafka.DisposeAsync().AsTask();

    [Fact]
    public async Task StorageSlowerThanMaxPollInterval_TheRecordReachesStorageTwiceWithTheSameIdempotencyKey()
    {
        var topic = $"eviction-{Guid.NewGuid():N}";
        var calls = new ConcurrentQueue<(DateTimeOffset At, string? Key)>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new Mock<IStorageProvider>();
        storage.Setup(s => s.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .Returns(async (JobRecord job, DuplicatePolicy _, CancellationToken _) =>
            {
                calls.Enqueue((DateTimeOffset.UtcNow, job.IdempotencyKey));
                if (calls.Count == 1)
                {
                    await release.Task;
                }

                return new EnqueueResult(job.Id, false);
            });
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IJobStorage>(storage.Object);
                services.AddSingleton(storage.Object);
                services.AddNexJob(options => options.Workers = 0);
                services.AddKafkaTrigger(options =>
                {
                    options.BootstrapServers = _kafka.GetBootstrapAddress();
                    options.Topic = topic;
                    options.GroupId = $"group-{Guid.NewGuid():N}";
                    options.ConfigureConsumer = c =>
                    {
                        c.MaxPollIntervalMs = 6000;
                        c.SessionTimeoutMs = 6000;
                    };
                });
            })
            .Build();
        using (var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = _kafka.GetBootstrapAddress() }).Build())
        {
            var headers = new Headers { { "nexjob.job_type", Encoding.UTF8.GetBytes(typeof(EvictionJob).AssemblyQualifiedName!) } };
            await producer.ProduceAsync(topic, new Message<string, string> { Key = "k", Value = "v", Headers = headers });
        }

        await host.StartAsync();
        await TestWait.UntilAsync(() => Task.FromResult(!calls.IsEmpty), timeout: TimeSpan.FromSeconds(60), because: "the first enqueue to start");
        await Task.Delay(TimeSpan.FromSeconds(25));
        release.SetResult();
        await Task.Delay(TimeSpan.FromSeconds(25));
        await host.StopAsync();

        calls.Should().HaveCountGreaterOrEqualTo(2, "the consumer was evicted, its commit failed and the record came back");
        calls.Select(c => c.Key).Distinct().Should().ContainSingle("both deliveries carry kafka:{topic}:{partition}:{offset}, which is what keeps them one job");
    }

    private sealed class EvictionJob : IJob<string>
    {
        public Task ExecuteAsync(string input, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
