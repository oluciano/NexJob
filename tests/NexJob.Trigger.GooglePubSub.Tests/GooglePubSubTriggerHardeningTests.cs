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

    /// <summary>Tests success path with traceparent.</summary>
    /// <returns>A task.</returns>
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

    /// <summary>Tests missing job_type path.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task HandleMessageAsync_MissingJobType_ReturnsNack()
    {
        var sut = CreateSut();
        var msg = CreateMessage(null);

        var method = typeof(GooglePubSubTriggerHandler).GetMethod("HandleMessageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var reply = await (Task<SubscriberClient.Reply>)method!.Invoke(sut, new object[] { msg, CancellationToken.None })!;

        reply.Should().Be(SubscriberClient.Reply.Nack);
    }

    /// <summary>Tests scheduler failure path.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task HandleMessageAsync_SchedulerThrows_ReturnsNack()
    {
        var sut = CreateSut();
        var msg = CreateMessage();
        _schedulerMock.Setup(x => x.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Exception("Fail"));

        var method = typeof(GooglePubSubTriggerHandler).GetMethod("HandleMessageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var reply = await (Task<SubscriberClient.Reply>)method!.Invoke(sut, new object[] { msg, CancellationToken.None })!;

        reply.Should().Be(SubscriberClient.Reply.Nack);
    }

    // ─── Lifecycle Branches ────────────────────────────────────────────────

    /// <summary>Tests that StartAsync surfaces immediate subscriber faults.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task StartAsync_WhenSubscriberFaultsImmediately_SurfacesFault()
    {
        var sut = CreateSut();
        var fault = new Exception("Startup failed");
        _subscriberMock.Setup(x => x.StartAsync(It.IsAny<Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromException(fault));

        var act = () => sut.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<Exception>().WithMessage("Startup failed");
    }

    /// <summary>Tests that StopAsync logs subscriber run task faults.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task StopAsync_WhenRunTaskFaults_LogsError()
    {
        var sut = CreateSut();
        var fault = new Exception("Late failure");

        // StartAsync returns a faulted task
        _subscriberMock.Setup(x => x.StartAsync(It.IsAny<Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromException(fault));

        // StartAsync will throw because it detects the immediate fault
        try
        {
            await sut.StartAsync(CancellationToken.None);
        }
        catch
        {
            // Expected
        }

        // Act
        var act = () => sut.StopAsync(CancellationToken.None);

        // Assert: Should not throw (error is logged and swallowed)
        await act.Should().NotThrowAsync();
        _subscriberMock.Verify(x => x.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
