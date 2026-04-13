using System.Text;
using FluentAssertions;
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
}
