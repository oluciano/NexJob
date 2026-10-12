using System.Text;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NexJob.Kafka;
using Testcontainers.Kafka;
using Xunit;

namespace NexJob.Kafka.IntegrationTests;

/// <summary>
/// Integration tests verifying real end-to-end message publishing to an Apache Kafka container.
/// </summary>
[Collection("Kafka")]
public sealed class KafkaProducerIntegrationTests
{
    private readonly KafkaFixture _fixture;

    public KafkaProducerIntegrationTests(KafkaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Producer_PublishesMessageToRealKafka_AndConsumerReceivesIt()
    {
        // Arrange
        var topic = "integration-test-topic";
        var bootstrapServers = _fixture.BootstrapServers;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKafkaProducer(options =>
        {
            options.BootstrapServers = bootstrapServers;
            options.Acks = Acks.All;
        });

        using var provider = services.BuildServiceProvider();
        var producerClient = provider.GetRequiredService<IKafkaProducerClient>();
        var job = new KafkaProducerJob(producerClient, NullLogger<KafkaProducerJob>.Instance);

        var payload = new KafkaPublishPayload
        {
            Topic = topic,
            Key = "order-999",
            ValueString = "{\"OrderId\":999,\"Amount\":450.00}",
            Headers = new Dictionary<string, string>
            {
                ["correlation-id"] = "corr-integration-999",
            },
        };

        // Act - Publish via KafkaProducerJob
        await job.ExecuteAsync(payload, CancellationToken.None);

        // Assert - Verify via real Confluent.Kafka consumer
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = "integration-verifier-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(consumerConfig).Build();
        consumer.Subscribe(topic);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var consumeResult = consumer.Consume(cts.Token);

        consumeResult.Should().NotBeNull();
        consumeResult.Message.Key.Should().Be("order-999");

        var receivedJson = Encoding.UTF8.GetString(consumeResult.Message.Value);
        receivedJson.Should().Be("{\"OrderId\":999,\"Amount\":450.00}");

        var header = consumeResult.Message.Headers.FirstOrDefault(h => h.Key == "correlation-id");
        header.Should().NotBeNull();
        Encoding.UTF8.GetString(header!.GetValueBytes()).Should().Be("corr-integration-999");
    }

    /// <summary>N1 (#482): with the default queue configuration, an outbox message is published without listing a queue.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task EnqueueKafkaAsync_WithDefaultQueueConfiguration_PublishesTheMessage()
    {
        var topic = $"outbox-default-queue-{Guid.NewGuid():N}";
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(o =>
                {
                    o.PollingInterval = TimeSpan.FromMilliseconds(50);
                    o.MaxAttempts = 1;
                });
                services.AddKafkaProducer(options =>
                {
                    options.BootstrapServers = _fixture.BootstrapServers;
                    options.Acks = Acks.All;
                });
            })
            .Build();
        await host.StartAsync();

        await host.Services.GetRequiredService<IScheduler>().EnqueueKafkaAsync(topic, "key1", new { id = 1 });

        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = _fixture.BootstrapServers,
            GroupId = $"verifier-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(topic);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = consumer.Consume(cts.Token);

        Encoding.UTF8.GetString(result.Message.Value).Should().Be("{\"id\":1}");
        await host.StopAsync();
    }
}
