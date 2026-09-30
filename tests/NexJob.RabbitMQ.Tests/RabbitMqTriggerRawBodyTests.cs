using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace NexJob.Trigger.RabbitMQ.Tests;

/// <summary>
/// Regression tests for #287: a trigger-bound <c>IJob&lt;string&gt;</c> receives the message body verbatim as text,
/// so the body must be stored as valid JSON (a JSON string) and round-trip byte for byte.
/// </summary>
public sealed class RabbitMqTriggerRawBodyTests
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

    /// <summary>N2: a plain-text body is acked like any other message, never rejected as permanently bad.</summary>
    [Fact]
    public async Task PlainTextBody_IsAcked_NotRejected()
    {
        var (_, channel) = await EnqueueAsync("not json at all");

        channel.Verify(m => m.BasicAck(1, false), Times.Once);
        channel.Verify(m => m.BasicNack(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    private static async Task<(JobRecord Job, Mock<IModel> Channel)> EnqueueAsync(string body)
    {
        var scheduler = new MockScheduler();
        var factory = new Mock<IConnectionFactory>();
        var connection = new Mock<IConnection>();
        var channel = new Mock<IModel>();
        factory.Setup(f => f.CreateConnection()).Returns(connection.Object);
        connection.Setup(c => c.CreateModel()).Returns(channel.Object);
        channel.Setup(m => m.IsOpen).Returns(true);

        AsyncEventingBasicConsumer? consumer = null;
        channel.Setup(m => m.BasicConsume(It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<IBasicConsumer>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object>, IBasicConsumer>((_, _, _, _, _, _, c) => consumer = (AsyncEventingBasicConsumer)c)
            .Returns("consumer-tag");

        var handler = new RabbitMqTriggerHandler(
            Options.Create(new RabbitMqTriggerOptions { HostName = "localhost", QueueName = "test-queue", TargetQueue = "default" }),
            factory.Object,
            scheduler,
            new NexJobOptions { MaxAttempts = 3 },
            new Mock<ILogger<RabbitMqTriggerHandler>>().Object);
        await handler.StartAsync(CancellationToken.None);

        var props = new Mock<IBasicProperties>();
        props.Setup(p => p.MessageId).Returns("test-message-id");
        props.Setup(p => p.Headers).Returns(new Dictionary<string, object>
        {
            ["nexjob.job_type"] = Encoding.UTF8.GetBytes("TestJobType"),
        });

        await consumer!.HandleBasicDeliver("consumer-tag", 1, false, "exchange", "routing-key", props.Object, Encoding.UTF8.GetBytes(body));
        await scheduler.WaitForEnqueueAsync(CancellationToken.None);

        return (scheduler.EnqueueCalls[0], channel);
    }
}
