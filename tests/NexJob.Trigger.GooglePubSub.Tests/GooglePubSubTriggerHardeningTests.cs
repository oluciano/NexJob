using FluentAssertions;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NexJob.Internal;
using Xunit;

namespace NexJob.Trigger.GooglePubSub.Tests;

/// <summary>
/// Hardening unit tests for <see cref="GooglePubSubTriggerHandler"/>.
/// Targets 100% branch coverage for Google Pub/Sub message processing.
/// </summary>
public sealed class GooglePubSubTriggerHardeningTests
{
    private readonly Mock<IPubSubSubscriber> _subscriberMock = new();
    private readonly Mock<IScheduler> _schedulerMock = new();
    private readonly GooglePubSubTriggerOptions _options = new()
    {
        ProjectId = "p1",
        SubscriptionId = "s1",
        TargetQueue = "default",
    };
    private readonly NexJobOptions _nexJobOptions = new();

    private GooglePubSubTriggerHandler CreateSut()
    {
        return new GooglePubSubTriggerHandler(
            Options.Create(_options),
            _subscriberMock.Object,
            _schedulerMock.Object,
            _nexJobOptions,
            NullLogger<GooglePubSubTriggerHandler>.Instance);
    }

    private PubsubMessage CreateMessage(string? jobType = "MyJob")
    {
        var msg = new PubsubMessage
        {
            MessageId = "msg1",
            Data = ByteString.CopyFromUtf8("{}"),
        };

        if (jobType != null)
        {
            msg.Attributes.Add("nexjob.job_type", jobType);
        }

        return msg;
    }

    // ─── Message Handling Branches ─────────────────────────────────────────

    /// <summary>Tests that successful enqueue returns Ack.</summary>
    [Fact]
    public async Task HandleMessageAsync_Success_ReturnsAck()
    {
        var sut = CreateSut();
        var msg = CreateMessage();
        msg.Attributes.Add("traceparent", "00-trace");

        var method = typeof(GooglePubSubTriggerHandler).GetMethod("HandleMessageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var reply = await (Task<SubscriberClient.Reply>)method!.Invoke(sut, new object[] { msg, CancellationToken.None })!;

        reply.Should().Be(SubscriberClient.Reply.Ack);
        _schedulerMock.Verify(x => x.EnqueueAsync(It.Is<JobRecord>(j => j.TraceParent == "00-trace"), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Tests that missing job_type attribute causes Nack.</summary>
    [Fact]
    public async Task HandleMessageAsync_MissingJobType_ReturnsNack()
    {
        var sut = CreateSut();
        var msg = CreateMessage(null);

        var method = typeof(GooglePubSubTriggerHandler).GetMethod("HandleMessageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var reply = await (Task<SubscriberClient.Reply>)method!.Invoke(sut, new object[] { msg, CancellationToken.None })!;

        reply.Should().Be(SubscriberClient.Reply.Nack);
    }

    /// <summary>Tests that scheduler failure causes Nack.</summary>
    [Fact]
    public async Task HandleMessageAsync_SchedulerThrows_ReturnsNack()
    {
        var sut = CreateSut();
        var msg = CreateMessage();
        _schedulerMock.Setup(x => x.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Exception("DB Down"));

        var method = typeof(GooglePubSubTriggerHandler).GetMethod("HandleMessageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var reply = await (Task<SubscriberClient.Reply>)method!.Invoke(sut, new object[] { msg, CancellationToken.None })!;

        reply.Should().Be(SubscriberClient.Reply.Nack);
    }

    /// <summary>Tests that cancellation returns Nack.</summary>
    [Fact]
    public async Task HandleMessageAsync_Cancellation_ReturnsNack()
    {
        var sut = CreateSut();
        var msg = CreateMessage();
        _schedulerMock.Setup(x => x.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.OperationCanceledException());

        var method = typeof(GooglePubSubTriggerHandler).GetMethod("HandleMessageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var reply = await (Task<SubscriberClient.Reply>)method!.Invoke(sut, new object[] { msg, CancellationToken.None })!;

        reply.Should().Be(SubscriberClient.Reply.Nack);
    }
}
