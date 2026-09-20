using FluentAssertions;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>
    /// Verifies that when nexjob.job_type attribute is absent, configured JobType from options is used.
    /// </summary>
    [Fact]
    public async Task JobTypeInOptions_FallbackUsed_WhenAttributeMissing()
    {
        // Arrange
        Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>? capturedHandler = null;

        _subscriberMock
            .Setup(s => s.StartAsync(It.IsAny<Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>>(), It.IsAny<CancellationToken>()))
            .Callback<Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>, CancellationToken>(
                (handler, _) => capturedHandler = handler)
            .Returns(Task.CompletedTask);

        var options = new GooglePubSubTriggerOptions
        {
            ProjectId = "test-project",
            SubscriptionId = "test-sub",
            TargetQueue = "default",
            JobType = "ConfiguredPubSubConsumerJob",
        };

        var handler = new GooglePubSubTriggerHandler(
            Options.Create(options),
            _subscriberMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);

        await handler.StartAsync(CancellationToken.None);

        var message = new PubsubMessage
        {
            MessageId = "msg-consumer-driven",
            Data = ByteString.CopyFromUtf8("{\"payload\":\"test\"}"),
            Attributes = { }, // No nexjob.job_type
        };

        // Act
        var reply = await capturedHandler!(message, CancellationToken.None);

        // Assert
        reply.Should().Be(SubscriberClient.Reply.Ack);
        _scheduler.EnqueueCalls.Should().HaveCount(1);
        _scheduler.EnqueueCalls[0].JobType.Should().Be("ConfiguredPubSubConsumerJob");
        _scheduler.EnqueueCalls[0].IdempotencyKey.Should().Be("msg-consumer-driven");
    }

    /// <summary>
    /// Verifies that when both attribute and options JobType are present, the attribute takes precedence.
    /// </summary>
    [Fact]
    public async Task AttributeJobType_TakesPrecedence_OverOptionsJobType()
    {
        // Arrange
        Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>? capturedHandler = null;

        _subscriberMock
            .Setup(s => s.StartAsync(It.IsAny<Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>>(), It.IsAny<CancellationToken>()))
            .Callback<Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>, CancellationToken>(
                (handler, _) => capturedHandler = handler)
            .Returns(Task.CompletedTask);

        var options = new GooglePubSubTriggerOptions
        {
            ProjectId = "test-project",
            SubscriptionId = "test-sub",
            TargetQueue = "default",
            JobType = "FallbackOptionsPubSubJob",
        };

        var handler = new GooglePubSubTriggerHandler(
            Options.Create(options),
            _subscriberMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);

        await handler.StartAsync(CancellationToken.None);

        var message = new PubsubMessage
        {
            MessageId = "msg-precedence",
            Data = ByteString.CopyFromUtf8("{\"payload\":\"test\"}"),
            Attributes = { ["nexjob.job_type"] = "AttributePriorityPubSubJob" },
        };

        // Act
        var reply = await capturedHandler!(message, CancellationToken.None);

        // Assert
        reply.Should().Be(SubscriberClient.Reply.Ack);
        _scheduler.EnqueueCalls.Should().HaveCount(1);
        _scheduler.EnqueueCalls[0].JobType.Should().Be("AttributePriorityPubSubJob");
    }

    /// <summary>
    /// Verifies that whitespace-only JobType in options is treated as missing and message is nacked.
    /// </summary>
    [Fact]
    public async Task JobTypeWhitespace_TreatedAsMissing()
    {
        // Arrange
        Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>? capturedHandler = null;

        _subscriberMock
            .Setup(s => s.StartAsync(It.IsAny<Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>>(), It.IsAny<CancellationToken>()))
            .Callback<Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>, CancellationToken>(
                (handler, _) => capturedHandler = handler)
            .Returns(Task.CompletedTask);

        var options = new GooglePubSubTriggerOptions
        {
            ProjectId = "test-project",
            SubscriptionId = "test-sub",
            TargetQueue = "default",
            JobType = "   ",
        };

        var handler = new GooglePubSubTriggerHandler(
            Options.Create(options),
            _subscriberMock.Object,
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);

        await handler.StartAsync(CancellationToken.None);

        var message = new PubsubMessage
        {
            MessageId = "msg-whitespace",
            Data = ByteString.CopyFromUtf8("{}"),
            Attributes = { },
        };

        // Act
        var reply = await capturedHandler!(message, CancellationToken.None);

        // Assert
        reply.Should().Be(SubscriberClient.Reply.Nack);
        _scheduler.EnqueueCalls.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that AddNexJobGooglePubSubTrigger generic overload correctly registers the job and configures JobType.
    /// </summary>
    [Fact]
    public void AddNexJobGooglePubSubTrigger_Generic_RegistersJobAndConfiguresJobType()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddNexJobGooglePubSubTrigger<TestConsumerPubSubJob>(opt =>
        {
            opt.ProjectId = "test-project";
            opt.SubscriptionId = "test-sub";
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<GooglePubSubTriggerOptions>>().Value;

        // Assert
        options.JobType.Should().Be(typeof(TestConsumerPubSubJob).AssemblyQualifiedName);
        services.Any(sd => sd.ServiceType == typeof(TestConsumerPubSubJob)).Should().BeTrue();
    }
}

/// <summary>
/// Sample consumer job for Google Pub/Sub registration tests.
/// </summary>
public sealed class TestConsumerPubSubJob : IJob<string>
{
    /// <inheritdoc />
    public Task ExecuteAsync(string input, CancellationToken cancellationToken) => Task.CompletedTask;
}
