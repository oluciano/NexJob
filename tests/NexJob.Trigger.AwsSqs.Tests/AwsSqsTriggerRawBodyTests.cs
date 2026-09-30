using System.Text.Json;
using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace NexJob.Trigger.AwsSqs.Tests;

/// <summary>
/// Regression tests for #287: a trigger-bound <c>IJob&lt;string&gt;</c> receives the message body verbatim as text,
/// so the body must be stored as valid JSON (a JSON string) and round-trip byte for byte.
/// </summary>
public sealed class AwsSqsTriggerRawBodyTests
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

    /// <summary>N3: a null body becomes an empty string, stored as the JSON string <c>""</c>.</summary>
    [Fact]
    public async Task NullBody_IsStoredAsEmptyJsonString()
    {
        var (job, _) = await EnqueueAsync(null);

        job.InputJson.Should().Be("\"\"");
    }

    /// <summary>N2: a plain-text body is deleted from the queue like any other message after a successful enqueue.</summary>
    [Fact]
    public async Task PlainTextBody_IsDeletedAfterEnqueue()
    {
        var (_, sqsClient) = await EnqueueAsync("not json at all");

        sqsClient.DeleteCalls.Should().HaveCount(1);
    }

    private static async Task<(JobRecord Job, MockSqsClient Client)> EnqueueAsync(string? body)
    {
        var sqsClient = new MockSqsClient();
        var scheduler = new MockScheduler();
        var trigger = new AwsSqsTriggerHandler(
            Options.Create(new AwsSqsTriggerOptions
            {
                QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/test-queue",
                JobName = typeof(AwsSqsTriggerRawBodyTests).AssemblyQualifiedName!,
                MaxMessages = 1,
                WaitTimeSeconds = 1,
                VisibilityTimeoutSeconds = 5,
                VisibilityExtensionIntervalSeconds = 3,
            }),
            sqsClient,
            scheduler,
            new NexJobOptions { MaxAttempts = 3 },
            new MockLogger<AwsSqsTriggerHandler>());
        sqsClient.AddTestMessage(new Message
        {
            MessageId = "test-msg-001",
            Body = body!,
            ReceiptHandle = "test-receipt-handle",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>(),
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await trigger.StartAsync(cts.Token);
        await scheduler.WaitForEnqueueAsync(cts.Token);
        await trigger.StopAsync(cts.Token);

        return (scheduler.EnqueueCalls[0], sqsClient);
    }
}
