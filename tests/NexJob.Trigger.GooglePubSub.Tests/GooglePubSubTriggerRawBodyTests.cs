using System.Text.Json;
using FluentAssertions;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace NexJob.Trigger.GooglePubSub.Tests;

/// <summary>
/// Regression tests for #287: a trigger-bound <c>IJob&lt;string&gt;</c> receives the message body verbatim as text,
/// so the body must be stored as valid JSON (a JSON string) and round-trip byte for byte.
/// </summary>
public sealed class GooglePubSubTriggerRawBodyTests
{
    /// <summary>N1/N3: any body, JSON or not, is stored as a JSON string that deserializes back to the original text.</summary>
    /// <param name="body">Raw message body.</param>
    [Theory]
    [InlineData("{\"a\":1}")]
    [InlineData("hello")]
    [InlineData("<x a=\"1\"/>")]
    [InlineData("\"quoted\"")]
    [InlineData("olá 🚀 \"x\"\n\tend")]
    [InlineData("")]
    public async Task Body_IsStoredAsJsonString_AndRoundTripsVerbatim(string body)
    {
        var (job, _) = await EnqueueAsync(body);

        var act = () => JsonDocument.Parse(job.InputJson);
        act.Should().NotThrow("InputJson must be valid JSON so a jsonb column accepts it");
        JsonSerializer.Deserialize<string>(job.InputJson).Should().Be(body);
        job.InputType.Should().Be(typeof(string).AssemblyQualifiedName);
    }

    /// <summary>N2: a plain-text body is acked like any other message, never nacked for redelivery.</summary>
    [Fact]
    public async Task PlainTextBody_IsAcked_NotNacked()
    {
        var (_, reply) = await EnqueueAsync("not json at all");

        reply.Should().Be(SubscriberClient.Reply.Ack);
    }

    private static async Task<(JobRecord Job, SubscriberClient.Reply Reply)> EnqueueAsync(string body)
    {
        var scheduler = new MockScheduler();
        var subscriber = new Mock<IPubSubSubscriber>();
        Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>? captured = null;
        subscriber
            .Setup(s => s.StartAsync(It.IsAny<Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>>(), It.IsAny<CancellationToken>()))
            .Callback<Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>>, CancellationToken>((h, _) => captured = h)
            .Returns(Task.CompletedTask);

        var handler = new GooglePubSubTriggerHandler(
            Options.Create(new GooglePubSubTriggerOptions { ProjectId = "test-project", SubscriptionId = "test-sub", TargetQueue = "default" }),
            subscriber.Object,
            scheduler,
            new NexJobOptions { MaxAttempts = 3 },
            new Mock<ILogger<GooglePubSubTriggerHandler>>().Object);
        await handler.StartAsync(CancellationToken.None);

        var reply = await captured!(
            new PubsubMessage
            {
                MessageId = "msg-001",
                Data = ByteString.CopyFromUtf8(body),
                Attributes = { ["nexjob.job_type"] = "TestJobType" },
            },
            CancellationToken.None);

        return (scheduler.EnqueueCalls[0], reply);
    }
}
