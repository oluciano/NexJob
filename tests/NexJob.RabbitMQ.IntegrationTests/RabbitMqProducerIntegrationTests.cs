using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NexJob.RabbitMQ;
using NexJob.Tests;
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
        await TestWait.UntilAsync(
            () =>
            {
                result = verifyChannel.BasicGet(queueName, autoAck: true);
                return Task.FromResult(result is not null);
            },
            because: "the published message to reach the queue");

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

    /// <summary>N1 (#482): with the default queue configuration, an outbox message is published without listing a queue.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task EnqueueRabbitMqAsync_WithDefaultQueueConfiguration_PublishesTheMessage()
    {
        var queueName = $"outbox-default-queue-{Guid.NewGuid():N}";
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
        }

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(o =>
                {
                    o.PollingInterval = TimeSpan.FromMilliseconds(50);
                    o.MaxAttempts = 1;
                });
                services.AddRabbitMqProducer(options =>
                {
                    options.HostName = _fixture.HostName;
                    options.Port = _fixture.Port;
                    options.UserName = RabbitMqTriggerFixture.UserName;
                    options.Password = RabbitMqTriggerFixture.Password;
                    options.ConfirmTimeout = TimeSpan.FromSeconds(10);
                });
            })
            .Build();
        await host.StartAsync();

        await host.Services.GetRequiredService<IScheduler>().EnqueueRabbitMqAsync(string.Empty, queueName, new { id = 1 });

        using var verifyConnection = factory.CreateConnection();
        using var verifyChannel = verifyConnection.CreateModel();
        BasicGetResult? result = null;
        await TestWait.UntilAsync(
            () =>
            {
                result = verifyChannel.BasicGet(queueName, autoAck: true);
                return Task.FromResult(result is not null);
            },
            because: "the published message to reach the queue");

        Encoding.UTF8.GetString(result!.Body.ToArray()).Should().Be("{\"id\":1}");
        await host.StopAsync();
    }

    // ── #483: Mandatory must fail an unroutable publish; the defaults must apply ──

    /// <summary>N1: a mandatory message the broker cannot route fails the publish, with the broker's reply.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task PublishAsync_MandatoryAndUnroutable_Fails()
    {
        using var client = NewClient(o => o.Mandatory = true);

        var act = () => client.PublishAsync(new RabbitMqPublishPayload { Exchange = string.Empty, RoutingKey = $"nowhere-{Guid.NewGuid():N}", ValueString = "{}" });

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("312").And.Contain("NO_ROUTE");
    }

    /// <summary>N2: a mandatory message that can be routed is published.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task PublishAsync_MandatoryAndRoutable_Publishes()
    {
        var queue = DeclareQueue();
        using var client = NewClient(o => o.Mandatory = true);

        await client.PublishAsync(new RabbitMqPublishPayload { Exchange = string.Empty, RoutingKey = queue, ValueString = "{\"ok\":true}" });

        (await ReadAsync(queue)).Should().Be("{\"ok\":true}");
    }

    /// <summary>N2: without Mandatory, an unroutable message is still dropped by the broker without an error.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task PublishAsync_NotMandatoryAndUnroutable_StillSucceeds()
    {
        using var client = NewClient(_ => { });

        var act = () => client.PublishAsync(new RabbitMqPublishPayload { Exchange = string.Empty, RoutingKey = $"nowhere-{Guid.NewGuid():N}", ValueString = "{}" });

        await act.Should().NotThrowAsync();
    }

    /// <summary>N3: a payload with no routing key uses DefaultRoutingKey.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task PublishAsync_NoRoutingKey_UsesDefaultRoutingKey()
    {
        var queue = DeclareQueue();
        using var client = NewClient(o => o.DefaultRoutingKey = queue);

        await client.PublishAsync(new RabbitMqPublishPayload { Exchange = string.Empty, ValueString = "{\"viaDefaultKey\":true}" });

        (await ReadAsync(queue)).Should().Be("{\"viaDefaultKey\":true}");
    }

    /// <summary>N1: EnqueueRabbitMqAsync with only a routing key publishes through DefaultExchange.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task EnqueueRabbitMqAsync_RoutingKeyOnly_PublishesThroughDefaultExchange()
    {
        var exchange = $"ex-{Guid.NewGuid():N}";
        var queue = DeclareQueue();
        using (var setup = NewFactory().CreateConnection())
        using (var channel = setup.CreateModel())
        {
            channel.ExchangeDeclare(exchange, ExchangeType.Direct);
            channel.QueueBind(queue, exchange, "k1");
        }

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(o =>
                {
                    o.PollingInterval = TimeSpan.FromMilliseconds(50);
                    o.MaxAttempts = 1;
                });
                services.AddRabbitMqProducer(options =>
                {
                    options.HostName = _fixture.HostName;
                    options.Port = _fixture.Port;
                    options.UserName = RabbitMqTriggerFixture.UserName;
                    options.Password = RabbitMqTriggerFixture.Password;
                    options.DefaultExchange = exchange;
                });
            })
            .Build();
        await host.StartAsync();

        await host.Services.GetRequiredService<IScheduler>().EnqueueRabbitMqAsync("k1", new { id = 7 });

        (await ReadAsync(queue)).Should().Be("{\"id\":7}");
        await host.StopAsync();
    }

    private ConnectionFactory NewFactory() => new()
    {
        HostName = _fixture.HostName,
        Port = _fixture.Port,
        UserName = RabbitMqTriggerFixture.UserName,
        Password = RabbitMqTriggerFixture.Password,
    };

    private string DeclareQueue()
    {
        var queue = $"q-{Guid.NewGuid():N}";
        using var connection = NewFactory().CreateConnection();
        using var channel = connection.CreateModel();
        channel.QueueDeclare(queue: queue, durable: false, exclusive: false, autoDelete: false);
        return queue;
    }

    private IRabbitMqProducerClient NewClient(Action<RabbitMqProducerOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRabbitMqProducer(options =>
        {
            options.HostName = _fixture.HostName;
            options.Port = _fixture.Port;
            options.UserName = RabbitMqTriggerFixture.UserName;
            options.Password = RabbitMqTriggerFixture.Password;
            options.ConfirmTimeout = TimeSpan.FromSeconds(10);
            configure(options);
        });
        return services.BuildServiceProvider().GetRequiredService<IRabbitMqProducerClient>();
    }

    private async Task<string> ReadAsync(string queue)
    {
        using var connection = NewFactory().CreateConnection();
        using var channel = connection.CreateModel();
        BasicGetResult? result = null;
        await TestWait.UntilAsync(
            () =>
            {
                result = channel.BasicGet(queue, autoAck: true);
                return Task.FromResult(result is not null);
            },
            because: "the published message to reach the queue");

        return Encoding.UTF8.GetString(result!.Body.ToArray());
    }
}
