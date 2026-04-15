using System.Text;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NexJob.Internal;
using Xunit;

namespace NexJob.Trigger.Kafka.Tests;

/// <summary>
/// Tests for the Kafka trigger.
/// </summary>
public sealed class KafkaTriggerTests
{
    private readonly Mock<IKafkaConsumer> _consumerMock = new();
    private readonly MockScheduler _scheduler = new();
    private readonly Mock<ILogger<KafkaTriggerHandler>> _loggerMock = new();
    private readonly NexJobOptions _nexJobOptions = new() { MaxAttempts = 3 };
    private readonly KafkaTriggerOptions _triggerOptions = new()
    {
        BootstrapServers = "localhost:9092",
        Topic = "test-topic",
        GroupId = "test-group",
        TargetQueue = "default",
    };

    /// <summary>
    /// Verifies that a received message is correctly enqueued as a job and committed.
    /// </summary>
    [Fact]
    public async Task HappyPath_MessageReceived_JobEnqueuedAndCommitted()
    {
        // Arrange
        var message = new Message<string, string>
        {
            Key = "test-key",
            Value = "{\"key\":\"value\"}",
            Headers = new Headers
            {
                { "nexjob.job_type", Encoding.UTF8.GetBytes("TestJobType") },
            },
        };

        var consumeResult = new ConsumeResult<string, string>
        {
            Message = message,
            Topic = "test-topic",
            Partition = 0,
            Offset = 1,
        };

        _consumerMock.SetupSequence(m => m.Consume(It.IsAny<TimeSpan>()))
            .Returns(consumeResult)
            .Returns((ConsumeResult<string, string>?)null); // Stop loop after one message

        var handler = new KafkaTriggerHandler(
            Options.Create(_triggerOptions),
            _consumerMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await handler.StartAsync(cts.Token);
        await _scheduler.WaitForEnqueueAsync(cts.Token);
        await handler.StopAsync(cts.Token);

        // Assert
        _scheduler.EnqueueCalls.Should().HaveCount(1);
        _scheduler.EnqueueCalls[0].IdempotencyKey.Should().Be("test-key");
        _consumerMock.Verify(m => m.Commit(consumeResult), Times.Once);
        _consumerMock.Verify(m => m.Close(), Times.Once);
    }

    /// <summary>
    /// Verifies that if enqueuing fails and no DLT is configured, the offset is NOT committed.
    /// </summary>
    [Fact]
    public async Task EnqueueFailure_NoDLT_NotCommitted()
    {
        // Arrange
        var message = new Message<string, string>
        {
            Key = "test-key-fail",
            Value = "{}",
            Headers = new Headers
            {
                { "nexjob.job_type", Encoding.UTF8.GetBytes("TestJobType") },
            },
        };

        var consumeResult = new ConsumeResult<string, string>
        {
            Message = message,
            Topic = "test-topic",
            Partition = 0,
            Offset = 2,
        };

        _consumerMock.SetupSequence(m => m.Consume(It.IsAny<TimeSpan>()))
            .Returns(consumeResult)
            .Returns((ConsumeResult<string, string>?)null);

        _scheduler.ShouldFailEnqueue = true;

        var handler = new KafkaTriggerHandler(
            Options.Create(_triggerOptions),
            _consumerMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await handler.StartAsync(cts.Token);
        await _scheduler.WaitForEnqueueAttemptAsync(cts.Token);
        await handler.StopAsync(cts.Token);

        // Assert
        _consumerMock.Verify(m => m.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Never);
    }

    /// <summary>
    /// Verifies that if enqueuing fails and DLT is configured, the message is produced to DLT and committed.
    /// </summary>
    [Fact]
    public async Task EnqueueFailure_WithDLT_ProducedToDLTAndCommitted()
    {
        // Arrange
        var options = new KafkaTriggerOptions
        {
            BootstrapServers = "localhost:9092",
            Topic = "test-topic",
            GroupId = "test-group",
            DeadLetterTopic = "test-dlt",
        };

        var message = new Message<string, string>
        {
            Key = "test-key-dlt",
            Value = "{}",
            Headers = new Headers
            {
                { "nexjob.job_type", Encoding.UTF8.GetBytes("TestJobType") },
            },
        };

        var consumeResult = new ConsumeResult<string, string>
        {
            Message = message,
            Topic = "test-topic",
            Partition = 0,
            Offset = 3,
        };

        _consumerMock.SetupSequence(m => m.Consume(It.IsAny<TimeSpan>()))
            .Returns(consumeResult)
            .Returns((ConsumeResult<string, string>?)null);

        _scheduler.ShouldFailEnqueue = true;

        var handler = new KafkaTriggerHandler(
            Options.Create(options),
            _consumerMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await handler.StartAsync(cts.Token);
        await _scheduler.WaitForEnqueueAttemptAsync(cts.Token);
        await handler.StopAsync(cts.Token);

        // Assert
        _consumerMock.Verify(m => m.ProduceToDeadLetterAsync("test-dlt", consumeResult, It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Once);
        _consumerMock.Verify(m => m.Commit(consumeResult), Times.Once);
    }

    /// <summary>
    /// Verifies that an OperationCanceledException stops the loop and does NOT commit.
    /// </summary>
    [Fact]
    public async Task OperationCanceledException_StopsLoopAndNotCommitted()
    {
        // Arrange
        var message = new Message<string, string>
        {
            Key = "test-key-cancel",
            Value = "{}",
            Headers = new Headers
            {
                { "nexjob.job_type", Encoding.UTF8.GetBytes("TestJobType") },
            },
        };

        var consumeResult = new ConsumeResult<string, string>
        {
            Message = message,
            Topic = "test-topic",
            Partition = 0,
            Offset = 4,
        };

        _consumerMock.Setup(m => m.Consume(It.IsAny<TimeSpan>())).Returns(consumeResult);

        var mockScheduler = new Mock<IScheduler>();
        mockScheduler.Setup(s => s.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var handler = new KafkaTriggerHandler(
            Options.Create(_triggerOptions),
            _consumerMock.Object,
            mockScheduler.Object,
            _nexJobOptions,
            _loggerMock.Object);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await handler.StartAsync(cts.Token);

        // Wait a bit then stop
        await Task.Delay(500, cts.Token);
        await handler.StopAsync(cts.Token);

        // Assert
        _consumerMock.Verify(m => m.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Never);
    }

    /// <summary>
    /// Verifies that the traceparent header is correctly propagated to the JobRecord.
    /// </summary>
    [Fact]
    public async Task TracePropagation_TraceparentSetOnJobRecord()
    {
        // Arrange
        var traceparent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        var message = new Message<string, string>
        {
            Key = "test-key-trace",
            Value = "{}",
            Headers = new Headers
            {
                { "nexjob.job_type", Encoding.UTF8.GetBytes("TestJobType") },
                { "traceparent", Encoding.UTF8.GetBytes(traceparent) },
            },
        };

        var consumeResult = new ConsumeResult<string, string>
        {
            Message = message,
            Topic = "test-topic",
            Partition = 0,
            Offset = 5,
        };

        _consumerMock.SetupSequence(m => m.Consume(It.IsAny<TimeSpan>()))
            .Returns(consumeResult)
            .Returns((ConsumeResult<string, string>?)null);

        var handler = new KafkaTriggerHandler(
            Options.Create(_triggerOptions),
            _consumerMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await handler.StartAsync(cts.Token);
        await _scheduler.WaitForEnqueueAsync(cts.Token);
        await handler.StopAsync(cts.Token);

        // Assert
        _scheduler.EnqueueCalls.Should().HaveCount(1);
        _scheduler.EnqueueCalls[0].TraceParent.Should().Be(traceparent);
    }

    /// <summary>
    /// Verifies that if the job type header is missing, an InvalidOperationException is thrown and the offset is NOT committed.
    /// </summary>
    [Fact]
    public async Task MissingJobType_ThrowsInvalidOperationExceptionAndNotCommitted()
    {
        // Arrange
        var message = new Message<string, string>
        {
            Key = "test-key-no-type",
            Value = "{}",
            Headers = new Headers(),
        };

        var consumeResult = new ConsumeResult<string, string>
        {
            Message = message,
            Topic = "test-topic",
            Partition = 0,
            Offset = 6,
        };

        _consumerMock.SetupSequence(m => m.Consume(It.IsAny<TimeSpan>()))
            .Returns(consumeResult)
            .Returns((ConsumeResult<string, string>?)null);

        var handler = new KafkaTriggerHandler(
            Options.Create(_triggerOptions),
            _consumerMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await handler.StartAsync(cts.Token);

        // Since it throws, we wait a bit then stop
        await Task.Delay(500, cts.Token);
        await handler.StopAsync(cts.Token);

        // Assert
        _consumerMock.Verify(m => m.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Never);
        _scheduler.EnqueueCalls.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a message without nexjob.job_type header is not enqueued
    /// and offset is not committed.
    /// </summary>
    [Fact]
    public async Task MissingJobType_NotEnqueuedAndNotCommitted()
    {
        // Arrange
        var message = new Message<string, string>
        {
            Key = "test-key",
            Value = "{\"key\":\"value\"}",
            Headers = new Headers(), // No nexjob.job_type header
        };

        var consumeResult = new ConsumeResult<string, string>
        {
            Message = message,
            Topic = "test-topic",
            Partition = 0,
            Offset = 1,
        };

        _consumerMock.SetupSequence(m => m.Consume(It.IsAny<TimeSpan>()))
            .Returns(consumeResult)
            .Returns((ConsumeResult<string, string>?)null);

        var handler = new KafkaTriggerHandler(
            Options.Create(_triggerOptions),
            _consumerMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await handler.StartAsync(cts.Token);
        await Task.Delay(300, cts.Token).ContinueWith(_ => { });
        await handler.StopAsync(CancellationToken.None);

        // Assert
        _scheduler.EnqueueCalls.Should().BeEmpty("no job_type means no job should be created");
        _consumerMock.Verify(m => m.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Never,
            "offset must not be committed when job_type is missing");
    }
}
