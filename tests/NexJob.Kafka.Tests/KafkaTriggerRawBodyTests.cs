using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace NexJob.Trigger.Kafka.Tests;

/// <summary>
/// Regression tests for #287: a trigger-bound <c>IJob&lt;string&gt;</c> receives the message body verbatim as text,
/// so the body must be stored as valid JSON (a JSON string) and round-trip byte for byte.
/// </summary>
public sealed class KafkaTriggerRawBodyTests
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
        var job = await EnqueueAsync(body);

        var act = () => JsonDocument.Parse(job.InputJson);
        act.Should().NotThrow("InputJson must be valid JSON so a jsonb column accepts it");
        JsonSerializer.Deserialize<string>(job.InputJson).Should().Be(body);
        job.InputType.Should().Be(typeof(string).AssemblyQualifiedName);
    }

    /// <summary>N3: a null value becomes an empty string, stored as the JSON string <c>""</c>.</summary>
    [Fact]
    public async Task NullValue_IsStoredAsEmptyJsonString()
    {
        var job = await EnqueueAsync(null);

        job.InputJson.Should().Be("\"\"");
    }

    /// <summary>N2: a plain-text body is a normal message, never a permanent failure.</summary>
    [Fact]
    public async Task PlainTextBody_IsEnqueued_NotTreatedAsPermanentFailure()
    {
        var scheduler = new MockScheduler();
        var job = await EnqueueAsync("not json at all", scheduler);

        scheduler.EnqueueCalls.Should().HaveCount(1);
        job.Tags.Should().Contain("trigger:kafka");
    }

    private static async Task<JobRecord> EnqueueAsync(string? body, MockScheduler? scheduler = null)
    {
        scheduler ??= new MockScheduler();
        var consumer = new Mock<IKafkaConsumer>();
        consumer.SetupSequence(m => m.Consume(It.IsAny<TimeSpan>()))
            .Returns(new ConsumeResult<string, string>
            {
                Message = new Message<string, string>
                {
                    Key = "k",
                    Value = body!,
                    Headers = new Headers { { "nexjob.job_type", Encoding.UTF8.GetBytes("TestJobType") } },
                },
                Topic = "test-topic",
                Partition = 0,
                Offset = 1,
            })
            .Returns((ConsumeResult<string, string>?)null);

        var handler = new KafkaTriggerHandler(
            Options.Create(new KafkaTriggerOptions
            {
                BootstrapServers = "localhost:9092",
                Topic = "test-topic",
                GroupId = "test-group",
                TargetQueue = "default",
            }),
            consumer.Object,
            scheduler,
            new NexJobOptions { MaxAttempts = 3 },
            new Mock<ILogger<KafkaTriggerHandler>>().Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await handler.StartAsync(cts.Token);
        await scheduler.WaitForEnqueueAsync(cts.Token);
        await handler.StopAsync(cts.Token);

        return scheduler.EnqueueCalls[0];
    }
}
