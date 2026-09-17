using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NexJob.Kafka;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// 3N Unit Test Matrix for NexJob Kafka Outbox Producer.
/// Covers Positive (N1), Negative (N2), and Invalid Input (N3) scenarios.
/// </summary>
public sealed class KafkaProducerTests
{
    // =========================================================================
    // N1 — POSITIVE TESTS (Happy Path)
    // =========================================================================

    [Fact]
    public async Task EnqueueKafkaAsync_WithStronglyTypedObject_SerializesAndEnqueuesJobCorrectly()
    {
        // Arrange
        var mockScheduler = new Mock<IScheduler>();
        KafkaPublishPayload? capturedPayload = null;
        string? capturedQueue = null;
        IReadOnlyList<string>? capturedTags = null;

        mockScheduler
            .Setup(s => s.EnqueueAsync<KafkaProducerJob, KafkaPublishPayload>(
                It.IsAny<KafkaPublishPayload>(),
                It.IsAny<string?>(),
                It.IsAny<JobPriority>(),
                It.IsAny<string?>(),
                It.IsAny<DuplicatePolicy>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<KafkaPublishPayload, string?, JobPriority, string?, DuplicatePolicy, IReadOnlyList<string>?, TimeSpan?, CancellationToken>(
                (payload, queue, _, _, _, tags, _, _) =>
                {
                    capturedPayload = payload;
                    capturedQueue = queue;
                    capturedTags = tags;
                })
            .ReturnsAsync(JobId.New());

        var order = new TestOrder(42, "Laptop", 1299.99m);

        // Act
        var jobId = await mockScheduler.Object.EnqueueKafkaAsync(
            topic: "orders-topic",
            key: "order-42",
            value: order,
            headers: new Dictionary<string, string> { ["source"] = "checkout-service" });

        // Assert
        jobId.Should().NotBeNull();
        capturedPayload.Should().NotBeNull();
        capturedPayload!.Topic.Should().Be("orders-topic");
        capturedPayload.Key.Should().Be("order-42");
        capturedPayload.ValueString.Should().NotBeNull();
        capturedPayload.Headers.Should().ContainKey("source").WhoseValue.Should().Be("checkout-service");
        capturedQueue.Should().Be("kafka-producer");
        capturedTags.Should().Contain("producer:kafka");

        var deserialized = JsonSerializer.Deserialize<TestOrder>(capturedPayload.ValueString!);
        deserialized.Should().BeEquivalentTo(order);
    }

    [Fact]
    public async Task EnqueueKafkaAsync_WithRawString_EnqueuesPayloadCorrectly()
    {
        // Arrange
        var mockScheduler = new Mock<IScheduler>();
        KafkaPublishPayload? capturedPayload = null;

        mockScheduler
            .Setup(s => s.EnqueueAsync<KafkaProducerJob, KafkaPublishPayload>(
                It.IsAny<KafkaPublishPayload>(),
                It.IsAny<string?>(),
                It.IsAny<JobPriority>(),
                It.IsAny<string?>(),
                It.IsAny<DuplicatePolicy>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<KafkaPublishPayload, string?, JobPriority, string?, DuplicatePolicy, IReadOnlyList<string>?, TimeSpan?, CancellationToken>(
                (payload, _, _, _, _, _, _, _) => capturedPayload = payload)
            .ReturnsAsync(JobId.New());

        // Act
        await mockScheduler.Object.EnqueueKafkaAsync(
            topic: "raw-topic",
            key: "key-1",
            value: "plain text message");

        // Assert
        capturedPayload.Should().NotBeNull();
        capturedPayload!.Topic.Should().Be("raw-topic");
        capturedPayload.Key.Should().Be("key-1");
        capturedPayload.ValueString.Should().Be("plain text message");
        capturedPayload.ValueBytes.Should().BeNull();
    }

    [Fact]
    public async Task EnqueueKafkaAsync_WithRawByteArray_EnqueuesPayloadCorrectly()
    {
        // Arrange
        var mockScheduler = new Mock<IScheduler>();
        KafkaPublishPayload? capturedPayload = null;
        var bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        mockScheduler
            .Setup(s => s.EnqueueAsync<KafkaProducerJob, KafkaPublishPayload>(
                It.IsAny<KafkaPublishPayload>(),
                It.IsAny<string?>(),
                It.IsAny<JobPriority>(),
                It.IsAny<string?>(),
                It.IsAny<DuplicatePolicy>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<KafkaPublishPayload, string?, JobPriority, string?, DuplicatePolicy, IReadOnlyList<string>?, TimeSpan?, CancellationToken>(
                (payload, _, _, _, _, _, _, _) => capturedPayload = payload)
            .ReturnsAsync(JobId.New());

        // Act
        await mockScheduler.Object.EnqueueKafkaAsync(
            topic: "binary-topic",
            key: "key-bin",
            value: bytes);

        // Assert
        capturedPayload.Should().NotBeNull();
        capturedPayload!.Topic.Should().Be("binary-topic");
        capturedPayload.Key.Should().Be("key-bin");
        capturedPayload.ValueBytes.Should().Equal(bytes);
        capturedPayload.ValueString.Should().BeNull();
    }

    [Fact]
    public async Task KafkaProducerJob_ExecuteAsync_ProducesMessageToClientSuccessfully()
    {
        // Arrange
        var mockProducer = new Mock<IKafkaProducerClient>();
        string? capturedTopic = null;
        Message<string, byte[]>? capturedMessage = null;

        mockProducer
            .Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, byte[]>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, byte[]>, CancellationToken>((topic, msg, _) =>
            {
                capturedTopic = topic;
                capturedMessage = msg;
            })
            .ReturnsAsync(new DeliveryResult<string, byte[]>());

        var job = new KafkaProducerJob(mockProducer.Object, NullLogger<KafkaProducerJob>.Instance);

        var payload = new KafkaPublishPayload
        {
            Topic = "events",
            Key = "k1",
            ValueString = "hello kafka",
            Headers = new Dictionary<string, string> { ["correlation-id"] = "cid-123" },
        };

        // Act
        await job.ExecuteAsync(payload, CancellationToken.None);

        // Assert
        capturedTopic.Should().Be("events");
        capturedMessage.Should().NotBeNull();
        capturedMessage!.Key.Should().Be("k1");
        Encoding.UTF8.GetString(capturedMessage.Value).Should().Be("hello kafka");
        capturedMessage.Headers.Should().Contain(h => h.Key == "correlation-id");
    }

    [Fact]
    public async Task KafkaProducerJob_ExecuteAsync_WithTraceParent_PropagatesActivityTrace()
    {
        // Arrange
        var mockProducer = new Mock<IKafkaProducerClient>();
        Message<string, byte[]>? capturedMessage = null;

        mockProducer
            .Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, byte[]>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, byte[]>, CancellationToken>((_, msg, _) => capturedMessage = msg)
            .ReturnsAsync(new DeliveryResult<string, byte[]>());

        var job = new KafkaProducerJob(mockProducer.Object, NullLogger<KafkaProducerJob>.Instance);
        var payload = new KafkaPublishPayload { Topic = "traced-topic", ValueString = "data" };

        var activitySource = new ActivitySource("NexJob.Tests.KafkaProducer");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = activitySource.StartActivity("ProduceKafkaTestActivity");
        activity.Should().NotBeNull();

        // Act
        await job.ExecuteAsync(payload, CancellationToken.None);

        // Assert
        capturedMessage.Should().NotBeNull();
        var traceHeader = capturedMessage!.Headers.FirstOrDefault(h => h.Key == "traceparent");
        traceHeader.Should().NotBeNull();
        Encoding.UTF8.GetString(traceHeader!.GetValueBytes()).Should().Be(activity!.Id);
    }

    [Fact]
    public void AddKafkaProducer_RegistersServicesInDependencyInjection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddKafkaProducer(options =>
        {
            options.BootstrapServers = "localhost:9092";
            options.Acks = Acks.Leader;
            options.EnableIdempotence = false;
        });

        using var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<KafkaProducerOptions>>().Value;
        options.BootstrapServers.Should().Be("localhost:9092");
        options.Acks.Should().Be(Acks.Leader);
        options.EnableIdempotence.Should().BeFalse();

        var producerClient = provider.GetService<IKafkaProducerClient>();
        producerClient.Should().NotBeNull();

        var job = provider.GetService<KafkaProducerJob>();
        job.Should().NotBeNull();
    }

    [Fact]
    public void AddKafkaProducer_OnNexJobBuilder_ChainsSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new NexJobBuilder(services);

        // Act
        var returnedBuilder = builder.AddKafkaProducer(options =>
        {
            options.BootstrapServers = "localhost:9092";
        });

        // Assert
        returnedBuilder.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddKafkaTrigger_OnNexJobBuilder_ChainsSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new NexJobBuilder(services);

        // Act
        var returnedBuilder = builder.AddKafkaTrigger(options =>
        {
            options.BootstrapServers = "localhost:9092";
            options.Topic = "my-topic";
            options.GroupId = "my-group";
        });

        // Assert
        returnedBuilder.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddKafkaTrigger_OnServiceCollection_RegistersSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddKafkaTrigger(options =>
        {
            options.BootstrapServers = "localhost:9092";
            options.Topic = "svc-topic";
            options.GroupId = "svc-group";
        });

        // Assert
        services.Should().Contain(sd => sd.ServiceType == typeof(Trigger.Kafka.IKafkaConsumer));
    }

    [Fact]
    public async Task EnqueueKafkaRawAsync_WithString_EnqueuesSuccessfully()
    {
        // Arrange
        var mockScheduler = new Mock<IScheduler>();
        KafkaPublishPayload? capturedPayload = null;

        mockScheduler
            .Setup(s => s.EnqueueAsync<KafkaProducerJob, KafkaPublishPayload>(
                It.IsAny<KafkaPublishPayload>(),
                It.IsAny<string?>(),
                It.IsAny<JobPriority>(),
                It.IsAny<string?>(),
                It.IsAny<DuplicatePolicy>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<KafkaPublishPayload, string?, JobPriority, string?, DuplicatePolicy, IReadOnlyList<string>?, TimeSpan?, CancellationToken>(
                (payload, _, _, _, _, _, _, _) => capturedPayload = payload)
            .ReturnsAsync(JobId.New());

        // Act
        await mockScheduler.Object.EnqueueKafkaRawAsync(
            topic: "raw-str-topic",
            key: "raw-key",
            value: "hello raw text");

        // Assert
        capturedPayload.Should().NotBeNull();
        capturedPayload!.ValueString.Should().Be("hello raw text");
        capturedPayload.ValueBytes.Should().BeNull();
    }

    [Fact]
    public async Task EnqueueKafkaRawAsync_WithByteArray_EnqueuesSuccessfully()
    {
        // Arrange
        var mockScheduler = new Mock<IScheduler>();
        KafkaPublishPayload? capturedPayload = null;
        var bytes = new byte[] { 1, 2, 3 };

        mockScheduler
            .Setup(s => s.EnqueueAsync<KafkaProducerJob, KafkaPublishPayload>(
                It.IsAny<KafkaPublishPayload>(),
                It.IsAny<string?>(),
                It.IsAny<JobPriority>(),
                It.IsAny<string?>(),
                It.IsAny<DuplicatePolicy>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<KafkaPublishPayload, string?, JobPriority, string?, DuplicatePolicy, IReadOnlyList<string>?, TimeSpan?, CancellationToken>(
                (payload, _, _, _, _, _, _, _) => capturedPayload = payload)
            .ReturnsAsync(JobId.New());

        // Act
        await mockScheduler.Object.EnqueueKafkaRawAsync(
            topic: "raw-bin-topic",
            key: "raw-bin-key",
            value: bytes);

        // Assert
        capturedPayload.Should().NotBeNull();
        capturedPayload!.ValueBytes.Should().Equal(bytes);
        capturedPayload.ValueString.Should().BeNull();
    }

    [Fact]
    public void AddKafkaProducer_WithNullArguments_ThrowsArgumentNullException()
    {
        NexJobBuilder nullBuilder = null!;
        IServiceCollection nullServices = null!;
        var validBuilder = new NexJobBuilder(new ServiceCollection());
        var validServices = new ServiceCollection();

        var act1 = () => nullBuilder.AddKafkaProducer(_ => { });
        var act2 = () => validBuilder.AddKafkaProducer(null!);
        var act3 = () => nullServices.AddKafkaProducer(_ => { });
        var act4 = () => validServices.AddKafkaProducer(null!);

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
        act3.Should().Throw<ArgumentNullException>();
        act4.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddKafkaTrigger_WithNullArguments_ThrowsArgumentNullException()
    {
        NexJobBuilder nullBuilder = null!;
        IServiceCollection nullServices = null!;
        var validBuilder = new NexJobBuilder(new ServiceCollection());
        var validServices = new ServiceCollection();

        var act1 = () => nullBuilder.AddKafkaTrigger(_ => { });
        var act2 = () => validBuilder.AddKafkaTrigger(null!);
        var act3 = () => nullServices.AddKafkaTrigger(_ => { });
        var act4 = () => validServices.AddKafkaTrigger(null!);

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
        act3.Should().Throw<ArgumentNullException>();
        act4.Should().Throw<ArgumentNullException>();
    }

    // =========================================================================
    // N2 — NEGATIVE TESTS (Broker Failures & Exceptions)
    // =========================================================================

    [Fact]
    public async Task KafkaProducerJob_ExecuteAsync_WhenProduceThrowsProduceException_RethrowsException()
    {
        // Arrange
        var mockProducer = new Mock<IKafkaProducerClient>();
        mockProducer
            .Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, byte[]>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProduceException<string, byte[]>(
                new Error(ErrorCode.BrokerNotAvailable, "Broker unavailable"),
                new DeliveryResult<string, byte[]> { Topic = "err-topic" }));

        var job = new KafkaProducerJob(mockProducer.Object, NullLogger<KafkaProducerJob>.Instance);
        var payload = new KafkaPublishPayload { Topic = "err-topic", ValueString = "fail" };

        // Act & Assert
        var act = async () => await job.ExecuteAsync(payload, CancellationToken.None);
        await act.Should().ThrowAsync<ProduceException<string, byte[]>>()
            .WithMessage("*Broker unavailable*");
    }

    [Fact]
    public async Task KafkaProducerJob_ExecuteAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var mockProducer = new Mock<IKafkaProducerClient>();
        mockProducer
            .Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, byte[]>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var job = new KafkaProducerJob(mockProducer.Object, NullLogger<KafkaProducerJob>.Instance);
        var payload = new KafkaPublishPayload { Topic = "cancel-topic", ValueString = "cancel" };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        var act = async () => await job.ExecuteAsync(payload, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // =========================================================================
    // N3 — INVALID INPUT / BOUNDARY TESTS
    // =========================================================================

    [Fact]
    public async Task EnqueueKafkaAsync_WithNullScheduler_ThrowsArgumentNullException()
    {
        IScheduler scheduler = null!;

        var act = async () => await scheduler.EnqueueKafkaAsync(
            topic: "t",
            key: "k",
            value: new TestOrder(1, "A", 10));

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EnqueueKafkaAsync_WithNullOrWhitespaceTopic_ThrowsArgumentException(string? invalidTopic)
    {
        var mockScheduler = new Mock<IScheduler>();

        var act = async () => await mockScheduler.Object.EnqueueKafkaAsync(
            topic: invalidTopic!,
            key: "k",
            value: new TestOrder(1, "A", 10));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task EnqueueKafkaAsync_WithNullStringValue_ThrowsArgumentNullException()
    {
        var mockScheduler = new Mock<IScheduler>();

        var act = async () => await mockScheduler.Object.EnqueueKafkaAsync(
            topic: "t",
            key: "k",
            value: (string)null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task EnqueueKafkaAsync_WithNullByteArrayValue_ThrowsArgumentNullException()
    {
        var mockScheduler = new Mock<IScheduler>();

        var act = async () => await mockScheduler.Object.EnqueueKafkaAsync(
            topic: "t",
            key: "k",
            value: (byte[])null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task KafkaProducerJob_ExecuteAsync_WithNullPayload_ThrowsArgumentNullException()
    {
        var mockProducer = new Mock<IKafkaProducerClient>();
        var job = new KafkaProducerJob(mockProducer.Object, NullLogger<KafkaProducerJob>.Instance);

        var act = async () => await job.ExecuteAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task KafkaProducerJob_ExecuteAsync_WithInvalidTopic_ThrowsArgumentException(string? invalidTopic)
    {
        var mockProducer = new Mock<IKafkaProducerClient>();
        var job = new KafkaProducerJob(mockProducer.Object, NullLogger<KafkaProducerJob>.Instance);
        var payload = new KafkaPublishPayload { Topic = invalidTopic!, ValueString = "data" };

        var act = async () => await job.ExecuteAsync(payload, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void KafkaProducerOptions_ValidationFails_WhenBootstrapServersMissing()
    {
        var services = new ServiceCollection();
        services.AddKafkaProducer(options =>
        {
            options.BootstrapServers = string.Empty;
        });

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<KafkaProducerOptions>>().Value;
        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*BootstrapServers*");
    }

    private sealed record TestOrder(int Id, string Item, decimal Price);
}
