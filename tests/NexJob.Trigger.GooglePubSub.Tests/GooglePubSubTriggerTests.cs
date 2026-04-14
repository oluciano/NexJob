using FluentAssertions;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NexJob.Internal;
using Xunit;

namespace NexJob.Trigger.GooglePubSub.Tests;

/// <summary>
/// Tests for the Google Pub/Sub trigger.
/// </summary>
public sealed class GooglePubSubTriggerTests
{
    private readonly Mock<IPubSubSubscriber> _subscriberMock = new();
    private readonly MockScheduler _scheduler = new();
    private readonly Mock<ILogger<GooglePubSubTriggerHandler>> _loggerMock = new();
    private readonly NexJobOptions _nexJobOptions = new() { MaxAttempts = 3 };
    private readonly GooglePubSubTriggerOptions _triggerOptions = new()
    {
        ProjectId = "test-project",
        SubscriptionId = "test-sub",
        TargetQueue = "default",
    };

    /// <summary>
    /// Verifies that a received message is correctly enqueued as a job and acknowledged.
    /// </summary>
    [Fact]
    public async Task HappyPath_MessageReceived_JobEnqueuedAndAcked()
    {
        // Arrange
        Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>? capturedHandler = null;

        _subscriberMock
            .Setup(s => s.StartAsync(It.IsAny<Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>>(), It.IsAny<CancellationToken>()))
            .Callback<Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>, CancellationToken>(
                (handler, _) => capturedHandler = handler)
            .Returns(Task.CompletedTask);

        var handler = new GooglePubSubTriggerHandler(
            Options.Create(_triggerOptions),
            _subscriberMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);

        await handler.StartAsync(CancellationToken.None);

        var message = new PubsubMessage
        {
            MessageId = "msg-001",
            Data = ByteString.CopyFromUtf8("{\"key\":\"value\"}"),
            Attributes = { ["nexjob.job_type"] = "TestJobType", },
        };

        // Act
        var reply = await capturedHandler!(message, CancellationToken.None);

        // Assert
        reply.Should().Be(SubscriberClient.Reply.Ack);
        _scheduler.EnqueueCalls.Should().HaveCount(1);
        _scheduler.EnqueueCalls[0].IdempotencyKey.Should().Be("msg-001");
    }

    /// <summary>
    /// Verifies that if enqueuing fails, the message is nacked.
    /// </summary>
    [Fact]
    public async Task EnqueueFailure_Nacked()
    {
        // Arrange
        Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>? capturedHandler = null;

        _subscriberMock
            .Setup(s => s.StartAsync(It.IsAny<Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>>(), It.IsAny<CancellationToken>()))
            .Callback<Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>, CancellationToken>(
                (handler, _) => capturedHandler = handler)
            .Returns(Task.CompletedTask);

        _scheduler.ShouldFailEnqueue = true;

        var handler = new GooglePubSubTriggerHandler(
            Options.Create(_triggerOptions),
            _subscriberMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);

        await handler.StartAsync(CancellationToken.None);

        var message = new PubsubMessage
        {
            MessageId = "msg-fail",
            Data = ByteString.CopyFromUtf8("{}"),
            Attributes = { ["nexjob.job_type"] = "TestJobType", },
        };

        // Act
        var reply = await capturedHandler!(message, CancellationToken.None);

        // Assert
        reply.Should().Be(SubscriberClient.Reply.Nack);
    }

    /// <summary>
    /// Verifies that an OperationCanceledException results in a nack.
    /// </summary>
    [Fact]
    public async Task OperationCanceledException_Nacked()
    {
        // Arrange
        Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>? capturedHandler = null;

        _subscriberMock
            .Setup(s => s.StartAsync(It.IsAny<Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>>(), It.IsAny<CancellationToken>()))
            .Callback<Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>, CancellationToken>(
                (handler, _) => capturedHandler = handler)
            .Returns(Task.CompletedTask);

        var mockScheduler = new Mock<IScheduler>();
        mockScheduler.Setup(s => s.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var handler = new GooglePubSubTriggerHandler(
            Options.Create(_triggerOptions),
            _subscriberMock.Object,
            mockScheduler.Object,
            _nexJobOptions,
            _loggerMock.Object);

        await handler.StartAsync(CancellationToken.None);

        var message = new PubsubMessage
        {
            MessageId = "msg-cancel",
            Data = ByteString.CopyFromUtf8("{}"),
            Attributes = { ["nexjob.job_type"] = "TestJobType", },
        };

        // Act
        var reply = await capturedHandler!(message, CancellationToken.None);

        // Assert
        reply.Should().Be(SubscriberClient.Reply.Nack);
    }

    /// <summary>
    /// Verifies that the traceparent attribute is correctly propagated to the JobRecord.
    /// </summary>
    [Fact]
    public async Task TracePropagation_TraceparentSetOnJobRecord()
    {
        // Arrange
        Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>? capturedHandler = null;

        _subscriberMock
            .Setup(s => s.StartAsync(It.IsAny<Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>>(), It.IsAny<CancellationToken>()))
            .Callback<Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>, CancellationToken>(
                (handler, _) => capturedHandler = handler)
            .Returns(Task.CompletedTask);

        var handler = new GooglePubSubTriggerHandler(
            Options.Create(_triggerOptions),
            _subscriberMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);

        await handler.StartAsync(CancellationToken.None);

        var traceparent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        var message = new PubsubMessage
        {
            MessageId = "msg-trace",
            Data = ByteString.CopyFromUtf8("{}"),
            Attributes =
            {
                ["nexjob.job_type"] = "TestJobType",
                ["traceparent"] = traceparent,
            },
        };

        // Act
        await capturedHandler!(message, CancellationToken.None);

        // Assert
        _scheduler.EnqueueCalls.Should().HaveCount(1);
        _scheduler.EnqueueCalls[0].TraceParent.Should().Be(traceparent);
    }

    /// <summary>
    /// Verifies that if the job type attribute is missing, an InvalidOperationException is thrown and the message is nacked.
    /// </summary>
    [Fact]
    public async Task MissingJobType_ThrowsInvalidOperationExceptionAndNacks()
    {
        // Arrange
        Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>? capturedHandler = null;

        _subscriberMock
            .Setup(s => s.StartAsync(It.IsAny<Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>>(), It.IsAny<CancellationToken>()))
            .Callback<Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>, CancellationToken>(
                (handler, _) => capturedHandler = handler)
            .Returns(Task.CompletedTask);

        var handler = new GooglePubSubTriggerHandler(
            Options.Create(_triggerOptions),
            _subscriberMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);

        await handler.StartAsync(CancellationToken.None);

        var message = new PubsubMessage
        {
            MessageId = "msg-no-type",
            Data = ByteString.CopyFromUtf8("{}"),
            Attributes = { }, // Missing nexjob.job_type
        };

        // Act
        var reply = await capturedHandler!(message, CancellationToken.None);

        // Assert
        reply.Should().Be(SubscriberClient.Reply.Nack);
        _scheduler.EnqueueCalls.Should().BeEmpty();
    }
}
