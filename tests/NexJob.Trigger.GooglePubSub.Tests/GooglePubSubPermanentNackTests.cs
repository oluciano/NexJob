using FluentAssertions;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace NexJob.Trigger.GooglePubSub.Tests;

/// <summary>
/// A message that can never become a job is nacked after a pause, so it is not redelivered in a tight loop (issue #142).
/// </summary>
public sealed class GooglePubSubPermanentNackTests
{
    private static readonly PubsubMessage NoJobType = new() { MessageId = "no-type", Data = ByteString.CopyFromUtf8("{}") };

    /// <summary>The nack of a message with no job type is held back for the pause.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task MissingJobType_IsNackedOnlyAfterThePause()
    {
        // N2 (Negative)
        var handle = await StartAsync(TimeSpan.FromMilliseconds(400), new MockScheduler());

        var pending = handle(NoJobType, CancellationToken.None);
        await Task.Delay(100);
        pending.IsCompleted.Should().BeFalse("the nack is held back so the message does not come straight back");

        (await pending).Should().Be(SubscriberClient.Reply.Nack);
    }

    /// <summary>Shutdown does not wait for the pause.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task MissingJobType_WhenTheHostIsStopping_NacksAtOnce()
    {
        // N3 (Boundary): shutdown must not wait for the pause.
        var handle = await StartAsync(TimeSpan.FromMinutes(5), new MockScheduler());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var reply = await handle(NoJobType, cts.Token).WaitAsync(TimeSpan.FromSeconds(5));

        reply.Should().Be(SubscriberClient.Reply.Nack);
    }

    /// <summary>A message that can be handled is acknowledged without any pause.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task MessageWithAJobType_IsNotHeldBack()
    {
        // N1 (Positive): only messages that can never be handled wait.
        var scheduler = new MockScheduler();
        var handle = await StartAsync(TimeSpan.FromMinutes(5), scheduler);
        var message = new PubsubMessage { MessageId = "ok", Data = ByteString.CopyFromUtf8("{}") };
        message.Attributes["nexjob.job_type"] = "TestJobType";

        var reply = await handle(message, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        reply.Should().Be(SubscriberClient.Reply.Ack);
        scheduler.EnqueueCalls.Should().ContainSingle();
    }

    private static async Task<Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>> StartAsync(TimeSpan pause, MockScheduler scheduler)
    {
        Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>? captured = null;
        var subscriber = new Mock<IPubSubSubscriber>();
        subscriber
            .Setup(s => s.StartAsync(It.IsAny<Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>>(), It.IsAny<CancellationToken>()))
            .Callback<Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>, CancellationToken>((h, _) => captured = h)
            .Returns(Task.CompletedTask);

        var handler = new GooglePubSubTriggerHandler(
            Options.Create(new GooglePubSubTriggerOptions { ProjectId = "p", SubscriptionId = "s" }),
            subscriber.Object,
            scheduler,
            new NexJobOptions { MaxAttempts = 3 },
            NullLogger<GooglePubSubTriggerHandler>.Instance)
        {
            PermanentNackDelay = pause,
        };
        await handler.StartAsync(CancellationToken.None);
        return captured!;
    }
}
