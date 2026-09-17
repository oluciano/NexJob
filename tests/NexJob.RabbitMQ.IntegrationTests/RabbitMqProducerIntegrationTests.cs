using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NexJob.RabbitMQ;
using RabbitMQ.Client;
using Xunit;

namespace NexJob.Trigger.RabbitMQ.IntegrationTests;

/// <summary>
/// Integration tests verifying real end-to-end message publishing to a RabbitMQ container with publisher confirms.
/// </summary>
[Collection("RabbitMQ")]
public sealed class RabbitMqProducerIntegrationTests : IClassFixture<RabbitMqTriggerFixture>
{
    private readonly RabbitMqTriggerFixture _fixture;

    public RabbitMqProducerIntegrationTests(RabbitMqTriggerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Producer_PublishesMessageToRealRabbitMq_AndConsumerReceivesIt()
    {
        // Arrange
        const string queueName = "producer-integration-queue";
        var factory = new ConnectionFactory
        {
            HostName = _fixture.HostName,
            Port = _fixture.Port,
            UserName = RabbitMqTriggerFixture.UserName,
            Password = RabbitMqTriggerFixture.Password,
        };

        using (var setupConnection = factory.CreateConnection())
        using (var setupChannel = setupConnection.CreateModel())
        {
            setupChannel.QueueDeclare(queue: queueName, durable: false, exclusive: false, autoDelete: false);
            setupChannel.QueuePurge(queueName);
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRabbitMqProducer(options =>
        {
            options.HostName = _fixture.HostName;
            options.Port = _fixture.Port;
            options.UserName = RabbitMqTriggerFixture.UserName;
            options.Password = RabbitMqTriggerFixture.Password;
            options.ConfirmTimeout = TimeSpan.FromSeconds(10);
        });

        using var provider = services.BuildServiceProvider();
        var producerClient = provider.GetRequiredService<IRabbitMqProducerClient>();
        var job = new RabbitMqProducerJob(producerClient, NullLogger<RabbitMqProducerJob>.Instance);

        var payload = new RabbitMqPublishPayload
        {
            Exchange = string.Empty,
            RoutingKey = queueName,
            ValueString = "{\"OrderId\":777,\"Status\":\"Approved\"}",
            CorrelationId = "corr-rmq-777",
            MessageId = "msg-rmq-777",
            Headers = new Dictionary<string, string>
            {
                ["event-source"] = "billing-service",
            },
        };

        // Act - Publish via RabbitMqProducerJob
        await job.ExecuteAsync(payload, CancellationToken.None);

        // Assert - Verify via direct channel BasicGet
        using var verifyConnection = factory.CreateConnection();
        using var verifyChannel = verifyConnection.CreateModel();

        BasicGetResult? result = null;
        for (var i = 0; i < 20; i++)
        {
            result = verifyChannel.BasicGet(queueName, autoAck: true);
            if (result != null)
            {
                break;
            }

            await Task.Delay(100);
        }

        result.Should().NotBeNull();
        var bodyString = Encoding.UTF8.GetString(result!.Body.ToArray());
        bodyString.Should().Be("{\"OrderId\":777,\"Status\":\"Approved\"}");

        result.BasicProperties.CorrelationId.Should().Be("corr-rmq-777");
        result.BasicProperties.MessageId.Should().Be("msg-rmq-777");
        result.BasicProperties.ContentType.Should().Be("application/json");

        result.BasicProperties.Headers.Should().ContainKey("event-source");
        var headerBytes = (byte[])result.BasicProperties.Headers["event-source"];
        Encoding.UTF8.GetString(headerBytes).Should().Be("billing-service");
    }
}
