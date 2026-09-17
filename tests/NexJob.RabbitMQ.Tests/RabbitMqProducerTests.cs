using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NexJob.RabbitMQ;
using RabbitMQ.Client;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// 3N Unit Test Matrix for NexJob RabbitMQ Outbox Producer.
/// Covers Positive (N1), Negative (N2), and Invalid Input (N3) scenarios.
/// </summary>
public sealed class RabbitMqProducerTests
{
    private sealed record TestOrder(int OrderId, string Item, decimal Price);

    // =========================================================================
    // N1 — POSITIVE TESTS (Happy Path)
    // =========================================================================

    [Fact]
    public async Task EnqueueRabbitMqAsync_WithStronglyTypedObject_SerializesAndEnqueuesJobCorrectly()
    {
        // Arrange
        var mockScheduler = new Mock<IScheduler>();
        RabbitMqPublishPayload? capturedPayload = null;
        string? capturedQueue = null;
        IReadOnlyList<string>? capturedTags = null;

        mockScheduler
            .Setup(s => s.EnqueueAsync<RabbitMqProducerJob, RabbitMqPublishPayload>(
                It.IsAny<RabbitMqPublishPayload>(),
                It.IsAny<string?>(),
                It.IsAny<JobPriority>(),
                It.IsAny<string?>(),
                It.IsAny<DuplicatePolicy>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<RabbitMqPublishPayload, string?, JobPriority, string?, DuplicatePolicy, IReadOnlyList<string>?, TimeSpan?, CancellationToken>(
                (payload, queue, _, _, _, tags, _, _) =>
                {
                    capturedPayload = payload;
                    capturedQueue = queue;
                    capturedTags = tags;
                })
            .ReturnsAsync(JobId.New());

        var order = new TestOrder(101, "Server Blade", 4999.99m);

        // Act
        var jobId = await mockScheduler.Object.EnqueueRabbitMqAsync(
            exchange: "orders.exchange",
            routingKey: "order.created",
            value: order,
            headers: new Dictionary<string, string> { ["source"] = "checkout-api" },
            correlationId: "corr-101",
            messageId: "msg-101");

        // Assert
        jobId.Should().NotBeNull();
        capturedPayload.Should().NotBeNull();
        capturedPayload!.Exchange.Should().Be("orders.exchange");
        capturedPayload.RoutingKey.Should().Be("order.created");
        capturedPayload.CorrelationId.Should().Be("corr-101");
        capturedPayload.MessageId.Should().Be("msg-101");
        capturedPayload.ContentType.Should().Be("application/json");
        capturedPayload.ValueString.Should().NotBeNull();
        capturedPayload.Headers.Should().ContainKey("source").WhoseValue.Should().Be("checkout-api");
        capturedQueue.Should().Be("rabbitmq-producer");
        capturedTags.Should().Contain("producer:rabbitmq");

        var deserialized = JsonSerializer.Deserialize<TestOrder>(capturedPayload.ValueString!);
        deserialized.Should().BeEquivalentTo(order);
    }

    [Fact]
    public async Task EnqueueRabbitMqAsync_WithRoutingKeyOnly_UsesDefaultExchange()
    {
        // Arrange
        var mockScheduler = new Mock<IScheduler>();
        RabbitMqPublishPayload? capturedPayload = null;

        mockScheduler
            .Setup(s => s.EnqueueAsync<RabbitMqProducerJob, RabbitMqPublishPayload>(
                It.IsAny<RabbitMqPublishPayload>(),
                It.IsAny<string?>(),
                It.IsAny<JobPriority>(),
                It.IsAny<string?>(),
                It.IsAny<DuplicatePolicy>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<RabbitMqPublishPayload, string?, JobPriority, string?, DuplicatePolicy, IReadOnlyList<string>?, TimeSpan?, CancellationToken>(
                (payload, _, _, _, _, _, _, _) => capturedPayload = payload)
            .ReturnsAsync(JobId.New());

        // Act
        await mockScheduler.Object.EnqueueRabbitMqAsync(
            routingKey: "simple.key",
            value: new TestOrder(1, "Book", 19.99m));

        // Assert
        capturedPayload.Should().NotBeNull();
        capturedPayload!.Exchange.Should().Be(string.Empty);
        capturedPayload.RoutingKey.Should().Be("simple.key");
    }

    [Fact]
    public async Task EnqueueRabbitMqAsync_WithRawString_EnqueuesPayloadCorrectly()
    {
        // Arrange
        var mockScheduler = new Mock<IScheduler>();
        RabbitMqPublishPayload? capturedPayload = null;

        mockScheduler
            .Setup(s => s.EnqueueAsync<RabbitMqProducerJob, RabbitMqPublishPayload>(
                It.IsAny<RabbitMqPublishPayload>(),
                It.IsAny<string?>(),
                It.IsAny<JobPriority>(),
                It.IsAny<string?>(),
                It.IsAny<DuplicatePolicy>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<RabbitMqPublishPayload, string?, JobPriority, string?, DuplicatePolicy, IReadOnlyList<string>?, TimeSpan?, CancellationToken>(
                (payload, _, _, _, _, _, _, _) => capturedPayload = payload)
            .ReturnsAsync(JobId.New());

        // Act
        await mockScheduler.Object.EnqueueRabbitMqAsync(
            exchange: "raw.ex",
            routingKey: "raw.key",
            value: "{\"status\":\"ok\"}");

        // Assert
        capturedPayload.Should().NotBeNull();
        capturedPayload!.ValueString.Should().Be("{\"status\":\"ok\"}");
        capturedPayload.ContentType.Should().Be("application/json");
        capturedPayload.ValueBytes.Should().BeNull();
    }

    [Fact]
    public async Task EnqueueRabbitMqAsync_WithByteArray_EnqueuesPayloadCorrectly()
    {
        // Arrange
        var mockScheduler = new Mock<IScheduler>();
        RabbitMqPublishPayload? capturedPayload = null;

        mockScheduler
            .Setup(s => s.EnqueueAsync<RabbitMqProducerJob, RabbitMqPublishPayload>(
                It.IsAny<RabbitMqPublishPayload>(),
                It.IsAny<string?>(),
                It.IsAny<JobPriority>(),
                It.IsAny<string?>(),
                It.IsAny<DuplicatePolicy>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<RabbitMqPublishPayload, string?, JobPriority, string?, DuplicatePolicy, IReadOnlyList<string>?, TimeSpan?, CancellationToken>(
                (payload, _, _, _, _, _, _, _) => capturedPayload = payload)
            .ReturnsAsync(JobId.New());

        byte[] rawBytes = [0x01, 0x02, 0x03, 0x04];

        // Act
        await mockScheduler.Object.EnqueueRabbitMqAsync(
            exchange: "bytes.ex",
            routingKey: "bytes.key",
            value: rawBytes);

        // Assert
        capturedPayload.Should().NotBeNull();
        capturedPayload!.ValueBytes.Should().BeSameAs(rawBytes);
        capturedPayload.ContentType.Should().Be("application/octet-stream");
        capturedPayload.ValueString.Should().BeNull();
    }

    [Fact]
    public async Task EnqueueRabbitMqRawAsync_String_EnqueuesPayloadCorrectly()
    {
        // Arrange
        var mockScheduler = new Mock<IScheduler>();
        RabbitMqPublishPayload? capturedPayload = null;

        mockScheduler
            .Setup(s => s.EnqueueAsync<RabbitMqProducerJob, RabbitMqPublishPayload>(
                It.IsAny<RabbitMqPublishPayload>(),
                It.IsAny<string?>(),
                It.IsAny<JobPriority>(),
                It.IsAny<string?>(),
                It.IsAny<DuplicatePolicy>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<RabbitMqPublishPayload, string?, JobPriority, string?, DuplicatePolicy, IReadOnlyList<string>?, TimeSpan?, CancellationToken>(
                (payload, _, _, _, _, _, _, _) => capturedPayload = payload)
            .ReturnsAsync(JobId.New());

        // Act
        await mockScheduler.Object.EnqueueRabbitMqRawAsync(
            exchange: "ex",
            routingKey: "key",
            value: "hello raw string");

        // Assert
        capturedPayload.Should().NotBeNull();
        capturedPayload!.ValueString.Should().Be("hello raw string");
        capturedPayload.ContentType.Should().Be("application/json");
    }

    [Fact]
    public async Task EnqueueRabbitMqRawAsync_ByteArray_EnqueuesPayloadCorrectly()
    {
        // Arrange
        var mockScheduler = new Mock<IScheduler>();
        RabbitMqPublishPayload? capturedPayload = null;

        mockScheduler
            .Setup(s => s.EnqueueAsync<RabbitMqProducerJob, RabbitMqPublishPayload>(
                It.IsAny<RabbitMqPublishPayload>(),
                It.IsAny<string?>(),
                It.IsAny<JobPriority>(),
                It.IsAny<string?>(),
                It.IsAny<DuplicatePolicy>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<RabbitMqPublishPayload, string?, JobPriority, string?, DuplicatePolicy, IReadOnlyList<string>?, TimeSpan?, CancellationToken>(
                (payload, _, _, _, _, _, _, _) => capturedPayload = payload)
            .ReturnsAsync(JobId.New());

        byte[] rawBytes = [0xFF, 0xFE];

        // Act
        await mockScheduler.Object.EnqueueRabbitMqRawAsync(
            exchange: "ex",
            routingKey: "key",
            value: rawBytes);

        // Assert
        capturedPayload.Should().NotBeNull();
        capturedPayload!.ValueBytes.Should().BeSameAs(rawBytes);
        capturedPayload.ContentType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task ProducerJob_ExecuteAsync_CallsClientPublishAsyncCorrectly()
    {
        // Arrange
        var mockClient = new Mock<IRabbitMqProducerClient>();
        var mockLogger = new Mock<ILogger<RabbitMqProducerJob>>();
        var job = new RabbitMqProducerJob(mockClient.Object, mockLogger.Object);

        var payload = new RabbitMqPublishPayload
        {
            Exchange = "events",
            RoutingKey = "event.fired",
            ValueString = "{\"foo\":\"bar\"}",
        };

        // Act
        await job.ExecuteAsync(payload, CancellationToken.None);

        // Assert
        mockClient.Verify(c => c.PublishAsync(payload, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProducerJob_ExecuteAsync_WithNullLogger_ExecutesCleanly()
    {
        // Arrange
        var mockClient = new Mock<IRabbitMqProducerClient>();
        var job = new RabbitMqProducerJob(mockClient.Object, null);

        var payload = new RabbitMqPublishPayload
        {
            Exchange = "events",
            RoutingKey = "key",
            ValueString = "payload",
        };

        // Act
        var act = async () => await job.ExecuteAsync(payload, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        mockClient.Verify(c => c.PublishAsync(payload, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void AddRabbitMqProducer_ServiceCollection_RegistersDependenciesCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddRabbitMqProducer(options =>
        {
            options.HostName = "rabbit.internal";
            options.Port = 5673;
            options.UserName = "admin";
            options.Password = "secret";
            options.DefaultExchange = "main.events";
        });

        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<RabbitMqProducerOptions>>().Value;
        options.HostName.Should().Be("rabbit.internal");
        options.Port.Should().Be(5673);
        options.UserName.Should().Be("admin");
        options.Password.Should().Be("secret");
        options.DefaultExchange.Should().Be("main.events");

        var client = provider.GetService<IRabbitMqProducerClient>();
        client.Should().NotBeNull();

        var job = provider.GetService<RabbitMqProducerJob>();
        job.Should().NotBeNull();
    }

    [Fact]
    public void AddRabbitMqProducer_NexJobBuilder_RegistersProducer()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new NexJobBuilder(services);

        // Act
        builder.AddRabbitMqProducer(options =>
        {
            options.HostName = "my-host";
        });

        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<RabbitMqProducerOptions>>().Value;
        options.HostName.Should().Be("my-host");
    }

    [Fact]
    public void AddRabbitMqTrigger_NexJobBuilder_RegistersTrigger()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new NexJobBuilder(services);

        // Act
        builder.AddRabbitMqTrigger(options =>
        {
            options.HostName = "my-host";
            options.QueueName = "incoming-q";
        });

        // Assert
        services.Any(s => s.ImplementationType == typeof(Trigger.RabbitMQ.RabbitMqTriggerHandler)).Should().BeTrue();
    }

    // =========================================================================
    // N2 — NEGATIVE TESTS (Broker / Execution Failures)
    // =========================================================================

    [Fact]
    public async Task ProducerJob_ExecuteAsync_ClientThrowsException_PropagatesException()
    {
        // Arrange
        var mockClient = new Mock<IRabbitMqProducerClient>();
        mockClient
            .Setup(c => c.PublishAsync(It.IsAny<RabbitMqPublishPayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("RabbitMQ connection lost."));

        var job = new RabbitMqProducerJob(mockClient.Object, NullLogger<RabbitMqProducerJob>.Instance);

        var payload = new RabbitMqPublishPayload
        {
            Exchange = "fail.ex",
            RoutingKey = "fail.key",
            ValueString = "data",
        };

        // Act
        Func<Task> act = async () => await job.ExecuteAsync(payload, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("RabbitMQ connection lost.");
    }

    [Fact]
    public async Task ProducerJob_ExecuteAsync_ClientThrowsTimeoutException_PropagatesException()
    {
        // Arrange
        var mockClient = new Mock<IRabbitMqProducerClient>();
        mockClient
            .Setup(c => c.PublishAsync(It.IsAny<RabbitMqPublishPayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Publisher confirm timed out."));

        var job = new RabbitMqProducerJob(mockClient.Object, NullLogger<RabbitMqProducerJob>.Instance);

        var payload = new RabbitMqPublishPayload
        {
            Exchange = "timeout.ex",
            RoutingKey = "timeout.key",
            ValueString = "data",
        };

        // Act
        Func<Task> act = async () => await job.ExecuteAsync(payload, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<TimeoutException>()
            .WithMessage("Publisher confirm timed out.");
    }

    // =========================================================================
    // N3 — INVALID INPUT TESTS (Boundary & Null Validation)
    // =========================================================================

    [Fact]
    public void ProducerJob_Constructor_NullClient_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => new RabbitMqProducerJob(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ProducerJob_ExecuteAsync_NullInput_ThrowsArgumentNullException()
    {
        // Arrange
        var mockClient = new Mock<IRabbitMqProducerClient>();
        var job = new RabbitMqProducerJob(mockClient.Object, NullLogger<RabbitMqProducerJob>.Instance);

        // Act
        Func<Task> act = async () => await job.ExecuteAsync(null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task EnqueueRabbitMqAsync_NullScheduler_ThrowsArgumentNullException()
    {
        // Act
        Func<Task> act = async () => await ((IScheduler)null!).EnqueueRabbitMqAsync(
            exchange: "ex",
            routingKey: "key",
            value: new TestOrder(1, "Item", 10m));

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task EnqueueRabbitMqAsync_NullValue_ThrowsArgumentNullException()
    {
        // Arrange
        var mockScheduler = new Mock<IScheduler>();

        // Act
        Func<Task> act = async () => await mockScheduler.Object.EnqueueRabbitMqAsync<TestOrder>(
            exchange: "ex",
            routingKey: "key",
            value: null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task EnqueueRabbitMqRawAsync_NullScheduler_ThrowsArgumentNullException()
    {
        // Act
        Func<Task> act = async () => await ((IScheduler)null!).EnqueueRabbitMqRawAsync(
            exchange: "ex",
            routingKey: "key",
            value: "string");

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task EnqueueRabbitMqRawAsync_NullValueString_ThrowsArgumentNullException()
    {
        // Arrange
        var mockScheduler = new Mock<IScheduler>();

        // Act
        Func<Task> act = async () => await mockScheduler.Object.EnqueueRabbitMqRawAsync(
            exchange: "ex",
            routingKey: "key",
            value: (string)null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task EnqueueRabbitMqRawAsync_NullValueBytes_ThrowsArgumentNullException()
    {
        // Arrange
        var mockScheduler = new Mock<IScheduler>();

        // Act
        Func<Task> act = async () => await mockScheduler.Object.EnqueueRabbitMqRawAsync(
            exchange: "ex",
            routingKey: "key",
            value: (byte[])null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void AddRabbitMqProducer_NullBuilder_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => ((NexJobBuilder)null!).AddRabbitMqProducer(_ => { });

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddRabbitMqProducer_NullConfigure_ThrowsArgumentNullException()
    {
        // Arrange
        var builder = new NexJobBuilder(new ServiceCollection());

        // Act
        Action act = () => builder.AddRabbitMqProducer(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddRabbitMqTrigger_NullBuilder_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => ((NexJobBuilder)null!).AddRabbitMqTrigger(_ => { });

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddRabbitMqTrigger_NullConfigure_ThrowsArgumentNullException()
    {
        // Arrange
        var builder = new NexJobBuilder(new ServiceCollection());

        // Act
        Action act = () => builder.AddRabbitMqTrigger(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RabbitMqProducerOptions_MissingHostName_FailsValidation()
    {
        // Arrange
        var options = new RabbitMqProducerOptions
        {
            HostName = string.Empty,
        };

        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(RabbitMqProducerOptions.HostName)));
    }

    [Fact]
    public void RabbitMqProducerOptions_DefaultValues_AreExpected()
    {
        // Act
        var options = new RabbitMqProducerOptions();

        // Assert
        options.HostName.Should().Be("localhost");
        options.Port.Should().Be(5672);
        options.UserName.Should().Be("guest");
        options.Password.Should().Be("guest");
        options.VirtualHost.Should().Be("/");
        options.DefaultExchange.Should().Be(string.Empty);
        options.DefaultRoutingKey.Should().BeNull();
        options.Mandatory.Should().BeFalse();
        options.ConfirmTimeout.Should().Be(TimeSpan.FromSeconds(5));
        options.Queue.Should().Be("rabbitmq-producer");
        options.DefaultPriority.Should().Be(JobPriority.Normal);
    }

    [Fact]
    public void RabbitMqPublishPayload_DefaultValues_AreExpected()
    {
        // Act
        var payload = new RabbitMqPublishPayload();

        // Assert
        payload.Exchange.Should().BeNull();
        payload.RoutingKey.Should().Be(string.Empty);
        payload.ValueBytes.Should().BeNull();
        payload.ValueString.Should().BeNull();
        payload.Headers.Should().BeNull();
        payload.Mandatory.Should().BeNull();
        payload.ContentType.Should().Be("application/json");
        payload.CorrelationId.Should().BeNull();
        payload.MessageId.Should().BeNull();
    }
}
