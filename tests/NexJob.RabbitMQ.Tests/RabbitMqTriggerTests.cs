using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        _scheduler.EnqueueCalls[0].IdempotencyKey.Should().Be("test-correlation-id");
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
        _channelMock.Verify(m => m.BasicNack(1, false, false), Times.Once);
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
        props.Setup(p => p.Headers).Returns(new Dictionary<string, object>());

        // Act
        await consumer!.HandleBasicDeliver(
            "consumer-tag", 1, false, "exchange", "routing-key", props.Object, body);

        await _scheduler.WaitForEnqueueAsync(CancellationToken.None);

        // Assert
        _scheduler.EnqueueCalls.Should().HaveCount(1);
        _scheduler.EnqueueCalls[0].JobType.Should().Be("ConfiguredRabbitConsumerJob");
        _scheduler.EnqueueCalls[0].IdempotencyKey.Should().Be("corr-123");
        _channelMock.Verify(m => m.BasicAck(1, false), Times.Once);
    }

    /// <summary>
    /// Verifies that when both header and options JobType are present, the header takes precedence.
    /// </summary>
    [Fact]
    public async Task HeaderJobType_TakesPrecedence_OverOptionsJobType()
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
        _scheduler.EnqueueCalls[0].JobType.Should().Be("HeaderPriorityRabbitJob");
        _channelMock.Verify(m => m.BasicAck(1, false), Times.Once);
    }

    /// <summary>
    /// Verifies that when neither CorrelationId nor MessageId is provided, idempotencyKey falls back to deterministic SHA256 of body.
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
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(body));

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
        _scheduler.EnqueueCalls[0].IdempotencyKey.Should().Be(expectedSha256);
        _channelMock.Verify(m => m.BasicAck(1, false), Times.Once);
    }

    /// <summary>
    /// Verifies that an empty body without CorrelationId/MessageId produces a valid non-empty SHA256 hash without throwing.
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
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(body));

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
        _scheduler.EnqueueCalls[0].IdempotencyKey.Should().Be(expectedSha256);
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
}

/// <summary>
/// Sample consumer job for RabbitMQ registration tests.
/// </summary>
public sealed class TestConsumerRabbitJob : IJob<string>
{
    public Task ExecuteAsync(string input, CancellationToken cancellationToken) => Task.CompletedTask;
}
