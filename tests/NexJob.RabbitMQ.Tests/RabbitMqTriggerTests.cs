using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NexJob.Internal;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace NexJob.Trigger.RabbitMQ.Tests;

/// <summary>
/// Tests for the RabbitMQ trigger.
/// </summary>
public sealed class RabbitMqTriggerTests
{
    private readonly Mock<IConnectionFactory> _connectionFactoryMock = new();
    private readonly Mock<IConnection> _connectionMock = new();
    private readonly Mock<IModel> _channelMock = new();
    private readonly MockScheduler _scheduler = new();
    private readonly Mock<ILogger<RabbitMqTriggerHandler>> _loggerMock = new();
    private readonly NexJobOptions _nexJobOptions = new() { MaxAttempts = 3 };
    private readonly RabbitMqTriggerOptions _triggerOptions = new()
    {
        HostName = "localhost",
        QueueName = "test-queue",
        TargetQueue = "default",
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMqTriggerTests"/> class.
    /// </summary>
    public RabbitMqTriggerTests()
    {
        _connectionFactoryMock.Setup(f => f.CreateConnection()).Returns(_connectionMock.Object);
        _connectionMock.Setup(c => c.CreateModel()).Returns(_channelMock.Object);
        _channelMock.Setup(m => m.IsOpen).Returns(true);

        _hardenedFactorymock.Setup(x => x.CreateConnection()).Returns(_hardenedConnectionmock.Object);
        _hardenedConnectionmock.Setup(x => x.CreateModel()).Returns(_hardenedChannelmock.Object);
    }

    /// <summary>
    /// Verifies that a received message is correctly enqueued as a job and acknowledged.
    /// </summary>
    [Fact]
    public async Task HappyPath_MessageReceived_JobEnqueuedAndAcked()
    {
        // Arrange
        AsyncEventingBasicConsumer? consumer = null;
        _channelMock.Setup(m => m.BasicConsume(It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<IBasicConsumer>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object>, IBasicConsumer>((_, _, _, _, _, _, c) => consumer = (AsyncEventingBasicConsumer)c)
            .Returns("consumer-tag");

        var handler = new RabbitMqTriggerHandler(
            Options.Create(_triggerOptions),
            _connectionFactoryMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);

        await handler.StartAsync(CancellationToken.None);

        var body = Encoding.UTF8.GetBytes("{\"key\":\"value\"}");
        var props = new Mock<IBasicProperties>();
        props.Setup(p => p.CorrelationId).Returns("test-correlation-id");
        props.Setup(p => p.MessageId).Returns("test-message-id");
        props.Setup(p => p.Headers).Returns(new Dictionary<string, object>
        {
            ["nexjob.job_type"] = Encoding.UTF8.GetBytes("TestJobType"),
        });

        // Act
        await consumer!.HandleBasicDeliver(
            "consumer-tag",
            1,
            false,
            "exchange",
            "routing-key",
            props.Object,
            body);

        await _scheduler.WaitForEnqueueAsync(CancellationToken.None);

        // Assert
        _scheduler.EnqueueCalls.Should().HaveCount(1);
        // Behavior changed in v5.6: idempotency key is MessageId only (#266)
        _scheduler.EnqueueCalls[0].IdempotencyKey.Should().Be("test-message-id");
        _channelMock.Verify(m => m.BasicAck(1, false), Times.Once);
    }

    /// <summary>
    /// Verifies that if enqueuing fails, the message is nacked without requeue.
    /// </summary>
    [Fact]
    public async Task EnqueueFailure_NackedWithoutRequeue()
    {
        // Arrange
        AsyncEventingBasicConsumer? consumer = null;
        _channelMock.Setup(m => m.BasicConsume(It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<IBasicConsumer>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object>, IBasicConsumer>((_, _, _, _, _, _, c) => consumer = (AsyncEventingBasicConsumer)c)
            .Returns("consumer-tag");

        _scheduler.ShouldFailEnqueue = true;

        var handler = new RabbitMqTriggerHandler(
            Options.Create(_triggerOptions),
            _connectionFactoryMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);

        await handler.StartAsync(CancellationToken.None);

        var body = Encoding.UTF8.GetBytes("{}");
        var props = new Mock<IBasicProperties>();
        props.Setup(p => p.Headers).Returns(new Dictionary<string, object>
        {
            ["nexjob.job_type"] = Encoding.UTF8.GetBytes("TestJobType"),
        });

        // Act
        await consumer!.HandleBasicDeliver(
            "consumer-tag",
            1,
            false,
            "exchange",
            "routing-key",
            props.Object,
            body);

        await _scheduler.WaitForEnqueueAttemptAsync(CancellationToken.None);

        // Assert
        // Behavior changed in v5.6: transient enqueue errors are requeued instead of nacked without requeue (#265)
        _channelMock.Verify(m => m.BasicNack(1, false, true), Times.Once);
        _channelMock.Verify(m => m.BasicNack(1, false, false), Times.Never);
    }

    /// <summary>
    /// Verifies that if an OperationCanceledException occurs, the message is nacked with requeue.
    /// </summary>
    [Fact]
    public async Task OperationCanceledException_NackedWithRequeue()
    {
        // Arrange
        AsyncEventingBasicConsumer? consumer = null;
        _channelMock.Setup(m => m.BasicConsume(It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<IBasicConsumer>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object>, IBasicConsumer>((_, _, _, _, _, _, c) => consumer = (AsyncEventingBasicConsumer)c)
            .Returns("consumer-tag");

        var mockScheduler = new Mock<IScheduler>();
        mockScheduler.Setup(s => s.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var handler = new RabbitMqTriggerHandler(
            Options.Create(_triggerOptions),
            _connectionFactoryMock.Object,
            mockScheduler.Object,
            _nexJobOptions,
            _loggerMock.Object);

        await handler.StartAsync(CancellationToken.None);

        var body = Encoding.UTF8.GetBytes("{}");
        var props = new Mock<IBasicProperties>();
        props.Setup(p => p.Headers).Returns(new Dictionary<string, object>
        {
            ["nexjob.job_type"] = Encoding.UTF8.GetBytes("TestJobType"),
        });

        // Act
        await consumer!.HandleBasicDeliver(
            "consumer-tag",
            1,
            false,
            "exchange",
            "routing-key",
            props.Object,
            body);

        // Assert
        _channelMock.Verify(m => m.BasicNack(1, false, true), Times.Once);
    }

    /// <summary>
    /// Verifies that stopping the trigger cancels an enqueue that is still waiting on the storage, so shutdown does not
    /// wait for it and the message is never acknowledged (the broker redelivers it).
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task StopAsync_WhileTheEnqueueIsPending_CancelsItAndNeverAcks()
    {
        // Arrange
        AsyncEventingBasicConsumer? consumer = null;
        _channelMock.Setup(m => m.BasicConsume(It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<IBasicConsumer>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object>, IBasicConsumer>((_, _, _, _, _, _, c) => consumer = (AsyncEventingBasicConsumer)c)
            .Returns("consumer-tag");

        var enqueueStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mockScheduler = new Mock<IScheduler>();
        mockScheduler.Setup(s => s.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .Returns<JobRecord, DuplicatePolicy, CancellationToken>(async (_, _, ct) =>
            {
                enqueueStarted.SetResult();
                await Task.Delay(Timeout.Infinite, ct); // a storage that never answers
                return default!;
            });

        var handler = new RabbitMqTriggerHandler(
            Options.Create(_triggerOptions),
            _connectionFactoryMock.Object,
            mockScheduler.Object,
            _nexJobOptions,
            _loggerMock.Object);
        await handler.StartAsync(CancellationToken.None);

        var props = new Mock<IBasicProperties>();
        props.Setup(p => p.Headers).Returns(new Dictionary<string, object>
        {
            ["nexjob.job_type"] = Encoding.UTF8.GetBytes("TestJobType"),
        });
        var delivery = consumer!.HandleBasicDeliver("consumer-tag", 1, false, "exchange", "routing-key", props.Object, Encoding.UTF8.GetBytes("{}"));
        await enqueueStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Act
        await handler.StopAsync(CancellationToken.None);

        // Assert
        await delivery.WaitAsync(TimeSpan.FromSeconds(10));
        _channelMock.Verify(m => m.BasicAck(It.IsAny<ulong>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// Verifies that the traceparent header is correctly propagated to the JobRecord.
    /// </summary>
    [Fact]
    public async Task TracePropagation_TraceparentSetOnJobRecord()
    {
        // Arrange
        AsyncEventingBasicConsumer? consumer = null;
        _channelMock.Setup(m => m.BasicConsume(It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<IBasicConsumer>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object>, IBasicConsumer>((_, _, _, _, _, _, c) => consumer = (AsyncEventingBasicConsumer)c)
            .Returns("consumer-tag");

        var handler = new RabbitMqTriggerHandler(
            Options.Create(_triggerOptions),
            _connectionFactoryMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);

        await handler.StartAsync(CancellationToken.None);

        var traceparent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        var props = new Mock<IBasicProperties>();
        props.Setup(p => p.Headers).Returns(new Dictionary<string, object>
        {
            ["nexjob.job_type"] = Encoding.UTF8.GetBytes("TestJobType"),
            ["traceparent"] = Encoding.UTF8.GetBytes(traceparent),
        });

        // Act
        await consumer!.HandleBasicDeliver(
            "consumer-tag",
            1,
            false,
            "exchange",
            "routing-key",
            props.Object,
            Encoding.UTF8.GetBytes("{}"));

        await _scheduler.WaitForEnqueueAsync(CancellationToken.None);

        // Assert
        _scheduler.EnqueueCalls.Should().HaveCount(1);
        _scheduler.EnqueueCalls[0].TraceParent.Should().Be(traceparent);
    }

    /// <summary>
    /// Verifies that if the job type header is missing, an InvalidOperationException is thrown and the message is nacked.
    /// </summary>
    [Fact]
    public async Task MissingJobType_ThrowsInvalidOperationExceptionAndNacks()
    {
        // Arrange
        AsyncEventingBasicConsumer? consumer = null;
        _channelMock.Setup(m => m.BasicConsume(It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<IBasicConsumer>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object>, IBasicConsumer>((_, _, _, _, _, _, c) => consumer = (AsyncEventingBasicConsumer)c)
            .Returns("consumer-tag");

        var handler = new RabbitMqTriggerHandler(
            Options.Create(_triggerOptions),
            _connectionFactoryMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);

        await handler.StartAsync(CancellationToken.None);

        var props = new Mock<IBasicProperties>();
        props.Setup(p => p.Headers).Returns(new Dictionary<string, object>());

        // Act
        await consumer!.HandleBasicDeliver(
            "consumer-tag",
            1,
            false,
            "exchange",
            "routing-key",
            props.Object,
            Encoding.UTF8.GetBytes("{}"));

        // Assert
        _channelMock.Verify(m => m.BasicNack(1, false, false), Times.Once);
        _scheduler.EnqueueCalls.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a message without nexjob.job_type header is nacked without requeue.
    /// </summary>
    [Fact]
    public async Task MissingJobType_MessageNackedWithoutRequeue()
    {
        // Arrange
        AsyncEventingBasicConsumer? consumer = null;
        _channelMock.Setup(m => m.BasicConsume(
                It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<bool>(), It.IsAny<IDictionary<string, object>>(),
                It.IsAny<IBasicConsumer>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object>, IBasicConsumer>(
                (_, _, _, _, _, _, c) => consumer = (AsyncEventingBasicConsumer)c)
            .Returns("consumer-tag");

        var handler = new RabbitMqTriggerHandler(
            Options.Create(_triggerOptions),
            _connectionFactoryMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);

        await handler.StartAsync(CancellationToken.None);

        var body = Encoding.UTF8.GetBytes("{\"key\":\"value\"}");
        var props = new Mock<IBasicProperties>();
        props.Setup(p => p.CorrelationId).Returns("test-correlation-id");
        props.Setup(p => p.Headers).Returns(new Dictionary<string, object>()); // No nexjob.job_type

        // Act
        await consumer!.HandleBasicDeliver(
            "consumer-tag", 1, false, "exchange", "routing-key", props.Object, body);

        await _scheduler.WaitForEnqueueAttemptAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromMilliseconds(500))
            .ContinueWith(_ => { }); // ignore timeout — no enqueue expected

        // Assert
        _scheduler.EnqueueCalls.Should().BeEmpty("no job_type means no job should be created");
        _channelMock.Verify(m => m.BasicNack(1, false, false), Times.Once);
    }

    /// <summary>
    /// Verifies that when nexjob.job_type header is absent, the configured JobType from options is used.
    /// </summary>
    [Fact]
    public async Task JobTypeInOptions_FallbackUsed_WhenHeaderMissing()
    {
        // Arrange
        AsyncEventingBasicConsumer? consumer = null;
        _channelMock.Setup(m => m.BasicConsume(
                It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<bool>(), It.IsAny<IDictionary<string, object>>(),
                It.IsAny<IBasicConsumer>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object>, IBasicConsumer>(
                (_, _, _, _, _, _, c) => consumer = (AsyncEventingBasicConsumer)c)
            .Returns("consumer-tag");

        var options = new RabbitMqTriggerOptions
        {
            HostName = "localhost",
            QueueName = "test-queue",
            TargetQueue = "default",
            JobType = "ConfiguredRabbitConsumerJob",
        };

        var handler = new RabbitMqTriggerHandler(
            Options.Create(options),
            _connectionFactoryMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);

        await handler.StartAsync(CancellationToken.None);

        var body = Encoding.UTF8.GetBytes("{\"orderId\":123}");
        var props = new Mock<IBasicProperties>();
        props.Setup(p => p.CorrelationId).Returns("corr-123");
        props.Setup(p => p.MessageId).Returns("msg-corr-123");
        props.Setup(p => p.Headers).Returns(new Dictionary<string, object>());

        // Act
        await consumer!.HandleBasicDeliver(
            "consumer-tag", 1, false, "exchange", "routing-key", props.Object, body);

        await _scheduler.WaitForEnqueueAsync(CancellationToken.None);

        // Assert
        _scheduler.EnqueueCalls.Should().HaveCount(1);
        _scheduler.EnqueueCalls[0].JobType.Should().Be("ConfiguredRabbitConsumerJob");
        // Behavior changed in v5.6: idempotency key is MessageId only (#266)
        _scheduler.EnqueueCalls[0].IdempotencyKey.Should().Be("msg-corr-123");
        _channelMock.Verify(m => m.BasicAck(1, false), Times.Once);
    }

    /// <summary>
    /// Verifies that when both header and options JobType are present, the configured JobType takes precedence.
    /// </summary>
    [Fact]
    // Behavior changed in v6.7.0: a configured JobType is a pinning decision by the operator
    // and must not be overridable by an attacker-controlled broker header (security issue #630).
    public async Task OptionsJobType_TakesPrecedence_OverHeaderJobType()
    {
        // Arrange
        AsyncEventingBasicConsumer? consumer = null;
        _channelMock.Setup(m => m.BasicConsume(
                It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<bool>(), It.IsAny<IDictionary<string, object>>(),
                It.IsAny<IBasicConsumer>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object>, IBasicConsumer>(
                (_, _, _, _, _, _, c) => consumer = (AsyncEventingBasicConsumer)c)
            .Returns("consumer-tag");

        var options = new RabbitMqTriggerOptions
        {
            HostName = "localhost",
            QueueName = "test-queue",
            TargetQueue = "default",
            JobType = "FallbackRabbitJob",
        };

        var handler = new RabbitMqTriggerHandler(
            Options.Create(options),
            _connectionFactoryMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);

        await handler.StartAsync(CancellationToken.None);

        var body = Encoding.UTF8.GetBytes("{\"orderId\":456}");
        var props = new Mock<IBasicProperties>();
        props.Setup(p => p.CorrelationId).Returns("corr-456");
        props.Setup(p => p.Headers).Returns(new Dictionary<string, object>
        {
            ["nexjob.job_type"] = Encoding.UTF8.GetBytes("HeaderPriorityRabbitJob"),
        });

        // Act
        await consumer!.HandleBasicDeliver(
            "consumer-tag", 1, false, "exchange", "routing-key", props.Object, body);

        await _scheduler.WaitForEnqueueAsync(CancellationToken.None);

        // Assert
        _scheduler.EnqueueCalls.Should().HaveCount(1);
        _scheduler.EnqueueCalls[0].JobType.Should().Be("FallbackRabbitJob");
        _channelMock.Verify(m => m.BasicAck(1, false), Times.Once);
    }

    /// <summary>
    /// Verifies that when no MessageId is provided the job is enqueued without an idempotency key (no body-hash fallback).
    /// </summary>
    [Fact]
    public async Task IdempotencyFallback_Sha256_WhenCorrelationIdAndMessageIdMissing()
    {
        // Arrange
        AsyncEventingBasicConsumer? consumer = null;
        _channelMock.Setup(m => m.BasicConsume(
                It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<bool>(), It.IsAny<IDictionary<string, object>>(),
                It.IsAny<IBasicConsumer>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object>, IBasicConsumer>(
                (_, _, _, _, _, _, c) => consumer = (AsyncEventingBasicConsumer)c)
            .Returns("consumer-tag");

        var options = new RabbitMqTriggerOptions
        {
            HostName = "localhost",
            QueueName = "test-queue",
            TargetQueue = "default",
            JobType = "DeterministicIdempotencyJob",
        };

        var handler = new RabbitMqTriggerHandler(
            Options.Create(options),
            _connectionFactoryMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);

        await handler.StartAsync(CancellationToken.None);

        var body = Encoding.UTF8.GetBytes("{\"content\":\"unique-payload\"}");

        var props = new Mock<IBasicProperties>();
        props.Setup(p => p.CorrelationId).Returns((string)null!);
        props.Setup(p => p.MessageId).Returns((string)null!);
        props.Setup(p => p.Headers).Returns(new Dictionary<string, object>());

        // Act
        await consumer!.HandleBasicDeliver(
            "consumer-tag", 1, false, "exchange", "routing-key", props.Object, body);

        await _scheduler.WaitForEnqueueAsync(CancellationToken.None);

        // Assert
        _scheduler.EnqueueCalls.Should().HaveCount(1);
        // Behavior changed in v5.6: idempotency key is MessageId only (#266)
        _scheduler.EnqueueCalls[0].IdempotencyKey.Should().BeNull();
        _channelMock.Verify(m => m.BasicAck(1, false), Times.Once);
    }

    /// <summary>
    /// Verifies that an empty body without a MessageId is enqueued without an idempotency key and without throwing.
    /// </summary>
    [Fact]
    public async Task IdempotencyFallback_EmptyBody_ComputesValidSha256()
    {
        // Arrange
        AsyncEventingBasicConsumer? consumer = null;
        _channelMock.Setup(m => m.BasicConsume(
                It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<bool>(), It.IsAny<IDictionary<string, object>>(),
                It.IsAny<IBasicConsumer>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object>, IBasicConsumer>(
                (_, _, _, _, _, _, c) => consumer = (AsyncEventingBasicConsumer)c)
            .Returns("consumer-tag");

        var options = new RabbitMqTriggerOptions
        {
            HostName = "localhost",
            QueueName = "test-queue",
            TargetQueue = "default",
            JobType = "EmptyBodyJob",
        };

        var handler = new RabbitMqTriggerHandler(
            Options.Create(options),
            _connectionFactoryMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);

        await handler.StartAsync(CancellationToken.None);

        var body = Array.Empty<byte>();

        var props = new Mock<IBasicProperties>();
        props.Setup(p => p.CorrelationId).Returns((string)null!);
        props.Setup(p => p.MessageId).Returns((string)null!);
        props.Setup(p => p.Headers).Returns(new Dictionary<string, object>());

        // Act
        await consumer!.HandleBasicDeliver(
            "consumer-tag", 1, false, "exchange", "routing-key", props.Object, body);

        await _scheduler.WaitForEnqueueAsync(CancellationToken.None);

        // Assert
        _scheduler.EnqueueCalls.Should().HaveCount(1);
        // Behavior changed in v5.6: idempotency key is MessageId only (#266)
        _scheduler.EnqueueCalls[0].IdempotencyKey.Should().BeNull();
        _channelMock.Verify(m => m.BasicAck(1, false), Times.Once);
    }

    /// <summary>
    /// Verifies that whitespace-only JobType in options is treated as missing and message is nacked.
    /// </summary>
    [Fact]
    public async Task JobTypeWhitespace_TreatedAsMissing()
    {
        // Arrange
        AsyncEventingBasicConsumer? consumer = null;
        _channelMock.Setup(m => m.BasicConsume(
                It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<bool>(), It.IsAny<IDictionary<string, object>>(),
                It.IsAny<IBasicConsumer>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object>, IBasicConsumer>(
                (_, _, _, _, _, _, c) => consumer = (AsyncEventingBasicConsumer)c)
            .Returns("consumer-tag");

        var options = new RabbitMqTriggerOptions
        {
            HostName = "localhost",
            QueueName = "test-queue",
            TargetQueue = "default",
            JobType = "   ",
        };

        var handler = new RabbitMqTriggerHandler(
            Options.Create(options),
            _connectionFactoryMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);

        await handler.StartAsync(CancellationToken.None);

        var body = Encoding.UTF8.GetBytes("{}");
        var props = new Mock<IBasicProperties>();
        props.Setup(p => p.CorrelationId).Returns("corr-whitespace");
        props.Setup(p => p.Headers).Returns(new Dictionary<string, object>());

        // Act
        await consumer!.HandleBasicDeliver(
            "consumer-tag", 1, false, "exchange", "routing-key", props.Object, body);

        await _scheduler.WaitForEnqueueAttemptAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromMilliseconds(500))
            .ContinueWith(_ => { });

        // Assert
        _scheduler.EnqueueCalls.Should().BeEmpty();
        _channelMock.Verify(m => m.BasicNack(1, false, false), Times.Once);
    }

    /// <summary>
    /// Verifies that AddNexJobRabbitMqTrigger generic overload correctly registers the job and configures JobType.
    /// </summary>
    [Fact]
    public void AddNexJobRabbitMqTrigger_Generic_RegistersJobAndConfiguresJobType()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddNexJobRabbitMqTrigger<TestConsumerRabbitJob>(opt =>
        {
            opt.HostName = "localhost";
            opt.QueueName = "test-queue";
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RabbitMqTriggerOptions>>().Value;

        // Assert
        options.JobType.Should().Be(typeof(TestConsumerRabbitJob).AssemblyQualifiedName);
        services.Any(sd => sd.ServiceType == typeof(TestConsumerRabbitJob)).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that RabbitMqTriggerHandler registers with IListenerRegistry and updates status.
    /// </summary>
    [Fact]
    public async Task ListenerRegistry_Lifecycle_TracksStatusProperly()
    {
        // Arrange
        var registry = new DefaultListenerRegistry();
        _channelMock.Setup(m => m.BasicConsume(It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<IBasicConsumer>()))
            .Returns("consumer-tag");

        var handler = new RabbitMqTriggerHandler(
            Options.Create(_triggerOptions),
            _connectionFactoryMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object,
            registry);

        var initial = registry.Get($"rabbitmq:{_triggerOptions.QueueName}");
        initial.Should().NotBeNull();
        initial!.Status.Should().Be(ListenerStatus.Starting);

        // Act & Assert 1: Start
        await handler.StartAsync(CancellationToken.None);
        var started = registry.Get($"rabbitmq:{_triggerOptions.QueueName}");
        started!.Status.Should().Be(ListenerStatus.Listening);

        // Act & Assert 2: Stop
        await handler.StopAsync(CancellationToken.None);
        var stopped = registry.Get($"rabbitmq:{_triggerOptions.QueueName}");
        stopped!.Status.Should().Be(ListenerStatus.Stopped);
    }

    // ── Idempotency key is MessageId only (issue #266) ────────────────────────

    private async Task<IReadOnlyList<JobRecord>> DeliverAsync(int expectedEnqueues, params (string? MessageId, string? CorrelationId)[] messages)
    {
        AsyncEventingBasicConsumer? consumer = null;
        _channelMock.Setup(m => m.BasicConsume(
                It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<bool>(), It.IsAny<IDictionary<string, object>>(),
                It.IsAny<IBasicConsumer>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object>, IBasicConsumer>(
                (_, _, _, _, _, _, c) => consumer = (AsyncEventingBasicConsumer)c)
            .Returns("consumer-tag");

        var options = new RabbitMqTriggerOptions
        {
            HostName = "localhost",
            QueueName = "test-queue",
            TargetQueue = "default",
            JobType = "IdempotencyKeyJob",
        };

        var handler = new RabbitMqTriggerHandler(
            Options.Create(options),
            _connectionFactoryMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);
        await handler.StartAsync(CancellationToken.None);

        ulong tag = 1;
        foreach (var (messageId, correlationId) in messages)
        {
            var props = new Mock<IBasicProperties>();
            props.Setup(p => p.MessageId).Returns(messageId!);
            props.Setup(p => p.CorrelationId).Returns(correlationId!);
            props.Setup(p => p.Headers).Returns(new Dictionary<string, object>());
            await consumer!.HandleBasicDeliver(
                "consumer-tag", tag++, false, "exchange", "routing-key", props.Object, Encoding.UTF8.GetBytes("{}"));
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (_scheduler.EnqueueCalls.Count < expectedEnqueues && !cts.IsCancellationRequested)
        {
            await Task.Delay(10, CancellationToken.None);
        }

        return _scheduler.EnqueueCalls;
    }

    /// <summary>N1: messages sharing a CorrelationId but with different MessageIds produce two jobs.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task SameCorrelationId_DifferentMessageIds_EnqueueTwoJobs()
    {
        var calls = await DeliverAsync(2, ("m-1", "order-42"), ("m-2", "order-42"));

        calls.Should().HaveCount(2);
        calls.Select(c => c.IdempotencyKey).Should().Equal("m-1", "m-2");
    }

    /// <summary>N2: the same MessageId delivered again carries the same key, so storage can deduplicate it.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task SameMessageIdRedelivered_UsesSameKey()
    {
        var calls = await DeliverAsync(2, ("m-9", "corr-a"), ("m-9", "corr-b"));

        calls.Should().HaveCount(2);
        calls.Select(c => c.IdempotencyKey).Should().OnlyContain(k => k == "m-9");
    }

    /// <summary>N3: a missing or whitespace MessageId enqueues without an idempotency key.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task NoMessageId_EnqueuesWithoutIdempotencyKey()
    {
        var calls = await DeliverAsync(2, (null, "corr-x"), ("   ", "corr-x"));

        calls.Should().HaveCount(2);
        calls.Select(c => c.IdempotencyKey).Should().OnlyContain(k => k == null);
    }

    // ── Transient enqueue failures are requeued, permanent ones are not (issue #265) ──

    private async Task<(AsyncEventingBasicConsumer Consumer, RabbitMqTriggerHandler Handler)> StartHandlerAsync(RabbitMqTriggerOptions options, TimeSpan? nackDelay)
    {
        AsyncEventingBasicConsumer? consumer = null;
        _channelMock.Setup(m => m.BasicConsume(
                It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<bool>(), It.IsAny<IDictionary<string, object>>(),
                It.IsAny<IBasicConsumer>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object>, IBasicConsumer>(
                (_, _, _, _, _, _, c) => consumer = (AsyncEventingBasicConsumer)c)
            .Returns("consumer-tag");

        var handler = new RabbitMqTriggerHandler(
            Options.Create(options),
            _connectionFactoryMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object)
        {
            TransientNackDelay = nackDelay ?? TimeSpan.FromSeconds(1),
        };
        await handler.StartAsync(CancellationToken.None);
        return (consumer!, handler);
    }

    private static Mock<IBasicProperties> PropsWithJobType(bool withJobType = true)
    {
        var props = new Mock<IBasicProperties>();
        props.Setup(p => p.MessageId).Returns("m-1");
        props.Setup(p => p.Headers).Returns(withJobType
            ? new Dictionary<string, object> { ["nexjob.job_type"] = Encoding.UTF8.GetBytes("TestJobType") }
            : new Dictionary<string, object>());
        return props;
    }

    /// <summary>N1: storage fails once — the first delivery is requeued, the redelivery is enqueued and acked.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task TransientEnqueueFailure_IsRequeued_ThenRedeliveredAndAcked()
    {
        var (consumer, _) = await StartHandlerAsync(_triggerOptions, TimeSpan.FromMilliseconds(1));
        _scheduler.FailFirstEnqueues = 1;
        var props = PropsWithJobType();

        await consumer.HandleBasicDeliver("consumer-tag", 1, false, "exchange", "routing-key", props.Object, Encoding.UTF8.GetBytes("{}"));
        await consumer.HandleBasicDeliver("consumer-tag", 2, true, "exchange", "routing-key", props.Object, Encoding.UTF8.GetBytes("{}"));

        _channelMock.Verify(m => m.BasicNack(1, false, true), Times.Once);
        _channelMock.Verify(m => m.BasicAck(2, false), Times.Once);
        _channelMock.Verify(m => m.BasicNack(It.IsAny<ulong>(), It.IsAny<bool>(), false), Times.Never);
        _scheduler.SucceededEnqueues.Should().Be(1);
    }

    /// <summary>N2: a message without job type is permanent — nacked without requeue and never handed to the scheduler.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task MissingJobType_IsNackedWithoutRequeue_NotEnqueued()
    {
        var (consumer, _) = await StartHandlerAsync(_triggerOptions, TimeSpan.FromMilliseconds(1));

        await consumer.HandleBasicDeliver("consumer-tag", 1, false, "exchange", "routing-key", PropsWithJobType(withJobType: false).Object, Encoding.UTF8.GetBytes("{}"));

        _channelMock.Verify(m => m.BasicNack(1, false, false), Times.Once);
        _channelMock.Verify(m => m.BasicNack(1, false, true), Times.Never);
        _scheduler.EnqueueCalls.Should().BeEmpty();
    }

    /// <summary>N3: storage keeps failing — the requeue is delayed (no hot redelivery loop) and the message is never acked.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task StorageKeepsFailing_RequeueIsDelayed_NeverAcked()
    {
        var (consumer, _) = await StartHandlerAsync(_triggerOptions, nackDelay: null);
        _scheduler.ShouldFailEnqueue = true;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        await consumer.HandleBasicDeliver("consumer-tag", 1, false, "exchange", "routing-key", PropsWithJobType().Object, Encoding.UTF8.GetBytes("{}"));
        sw.Stop();

        sw.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(900), "the default pause before requeue is one second");
        _channelMock.Verify(m => m.BasicNack(1, false, true), Times.Once);
        _channelMock.Verify(m => m.BasicAck(It.IsAny<ulong>(), It.IsAny<bool>()), Times.Never);
    }

    private readonly Mock<IConnectionFactory> _hardenedFactorymock = new();
    private readonly Mock<IConnection> _hardenedConnectionmock = new();
    private readonly Mock<IModel> _hardenedChannelmock = new();
    private readonly Mock<IScheduler> _hardenedSchedulermock = new();
    private readonly RabbitMqTriggerOptions _hardenedOptions = new()
    {
        QueueName = "test-q",
        ReconnectDelay = TimeSpan.FromMilliseconds(10),
    };
    private readonly NexJobOptions _hardenedNexjoboptions = new();

    private RabbitMqTriggerHandler HardenedCreateSut()
    {
        return new RabbitMqTriggerHandler(
            Options.Create(_hardenedOptions),
            _hardenedFactorymock.Object,
            _hardenedSchedulermock.Object,
            _hardenedNexjoboptions,
            NullLogger<RabbitMqTriggerHandler>.Instance);
    }

    // ─── Metadata Extraction Branches ──────────────────────────────────────

    /// <summary>Tests traceparent extraction with various header states.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task OnMessageReceived_TraceparentBranches()
    {
        var sut = HardenedCreateSut();
        await sut.StartAsync(CancellationToken.None);

        var props = new Mock<IBasicProperties>();
        IDictionary<string, object>? nullHeaders = null;
        props.Setup(p => p.Headers).Returns(nullHeaders!);

        var ea = new BasicDeliverEventArgs("t1", 1, false, "ex", "rk", props.Object, new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("{}")));

        var method = typeof(RabbitMqTriggerHandler).GetMethod("OnMessageReceivedAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(sut, new object[] { new object(), ea })!;

        _hardenedChannelmock.Verify(x => x.BasicNack(1, false, false), Times.AtLeastOnce);
    }

    // ─── Idempotency Key Fallback ──────────────────────────────────────────

    /// <summary>Tests fallback from CorrelationId to MessageId for idempotency key.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task OnMessageReceived_UsesMessageId_WhenCorrelationIdIsMissing()
    {
        var sut = HardenedCreateSut();
        await sut.StartAsync(CancellationToken.None);

        var props = new Mock<IBasicProperties>();
        props.Setup(p => p.Headers).Returns(new Dictionary<string, object> { ["nexjob.job_type"] = Encoding.UTF8.GetBytes("Job") });
        string? nullCorrelation = null;
        props.Setup(p => p.CorrelationId).Returns(nullCorrelation!);
        props.Setup(p => p.MessageId).Returns("msg-123");

        var ea = new BasicDeliverEventArgs("tag", 1, false, "ex", "rk", props.Object, new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("{}")));

        var method = typeof(RabbitMqTriggerHandler).GetMethod("OnMessageReceivedAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(sut, new object[] { new object(), ea })!;

        _hardenedSchedulermock.Verify(x => x.EnqueueAsync(It.Is<JobRecord>(j => j.IdempotencyKey == "msg-123"), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Teardown Branches ────────────────────────────────────────────────

    /// <summary>Tests that teardown handles closed channels and null connections gracefully.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task StopAsync_HandlesClosedResources()
    {
        var sut = HardenedCreateSut();
        await sut.StartAsync(CancellationToken.None);

        _hardenedChannelmock.Setup(x => x.IsOpen).Returns(false);

        await sut.StopAsync(CancellationToken.None);

        _hardenedChannelmock.Verify(x => x.BasicCancel(It.IsAny<string>()), Times.Never);
        _hardenedChannelmock.Verify(x => x.Dispose(), Times.Once);
    }

    /// <summary>Tests that teardown swallows exceptions.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Teardown_SwallowsExceptions()
    {
        var sut = HardenedCreateSut();
        await sut.StartAsync(CancellationToken.None);

        _hardenedChannelmock.Setup(x => x.Dispose()).Throws(new Exception("Disposal error"));

        await sut.StopAsync(CancellationToken.None);

        _hardenedChannelmock.Verify(x => x.Dispose(), Times.Once);
    }

    // ─── Reconnection Lifecycle Branches ───────────────────────────────────

    /// <summary>Tests the reconnection loop logic upon connection failure.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task StartAsync_WhenConnectionFails_RetriesUntilSuccess()
    {
        _hardenedFactorymock.SetupSequence(x => x.CreateConnection())
            .Throws(new Exception("Rabbit down"))
            .Returns(_hardenedConnectionmock.Object);

        var sut = HardenedCreateSut();
        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(50);

        _hardenedFactorymock.Verify(x => x.CreateConnection(), Times.AtLeast(2));
    }

    // ─── ConfigureConnection (#484) ─────────────────────────────────────────

    /// <summary>N1: the delegate configures the factory the trigger connects with.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task StartAsync_ConfigureConnection_IsAppliedToTheFactory()
    {
        var factory = new ConnectionFactory();
        var options = new RabbitMqTriggerOptions { Port = 1, QueueName = "q", ConfigureConnection = f => f.ClientProvidedName = "my-trigger" };
        var handler = new RabbitMqTriggerHandler(Options.Create(options), factory, _scheduler, _nexJobOptions, NullLogger<RabbitMqTriggerHandler>.Instance);

        await handler.StartAsync(CancellationToken.None);
        await handler.StopAsync(CancellationToken.None);

        factory.ClientProvidedName.Should().Be("my-trigger");
    }

    /// <summary>N2: the five connection settings win over what the delegate sets.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task StartAsync_ConfigureConnection_CannotOverrideTheFiveSettings()
    {
        var factory = new ConnectionFactory();
        var options = new RabbitMqTriggerOptions
        {
            HostName = "host-from-options",
            Port = 1,
            QueueName = "q",
            ConfigureConnection = f => f.HostName = "host-from-delegate",
        };
        var handler = new RabbitMqTriggerHandler(Options.Create(options), factory, _scheduler, _nexJobOptions, NullLogger<RabbitMqTriggerHandler>.Instance);

        await handler.StartAsync(CancellationToken.None);
        await handler.StopAsync(CancellationToken.None);

        factory.HostName.Should().Be("host-from-options");
    }

    /// <summary>N3: no delegate is the default and changes nothing.</summary>
    [Fact]
    public void ConfigureConnection_DefaultsToNull() =>
        new RabbitMqTriggerOptions().ConfigureConnection.Should().BeNull();

    /// <summary>N2: a delegate that throws is logged as a failed connection and does not crash the host.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task StartAsync_ConfigureConnectionThrows_DoesNotThrow()
    {
        var options = new RabbitMqTriggerOptions { Port = 1, QueueName = "q", ConfigureConnection = _ => throw new FormatException("bad tls") };
        var handler = new RabbitMqTriggerHandler(Options.Create(options), new ConnectionFactory(), _scheduler, _nexJobOptions, NullLogger<RabbitMqTriggerHandler>.Instance);

        await handler.Invoking(h => h.StartAsync(CancellationToken.None)).Should().NotThrowAsync();
        await handler.StopAsync(CancellationToken.None);
    }
}

/// <summary>
/// Sample consumer job for RabbitMQ registration tests.
/// </summary>
public sealed class TestConsumerRabbitJob : IJob<string>
{
    public Task ExecuteAsync(string input, CancellationToken cancellationToken) => Task.CompletedTask;
}
