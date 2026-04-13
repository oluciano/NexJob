using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.Trigger.AwsSqs.Tests;

/// <summary>
/// Tests for <see cref="AwsSqsTrigger"/>.
/// Verifies message processing, visibility extension, enqueue, deletion, and graceful shutdown.
/// </summary>
public sealed class AwsSqsTriggerTests
{
    // ─── Happy path: message received → job enqueued → message deleted ───────

    [Fact]
    public async Task HappyPath_MessageReceived_JobEnqueuedAndDeleted()
    {
        // Arrange
        var sqsClient = new MockSqsClient();
        var storageProvider = new MockStorageProvider();
        var wakeUpChannel = new JobWakeUpChannel();
        var options = Options.Create(new AwsSqsTriggerOptions
        {
            QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/test-queue",
            JobName = typeof(TestJob).AssemblyQualifiedName!,
            MaxMessages = 1,
            WaitTimeSeconds = 1,
            VisibilityTimeoutSeconds = 5,
            VisibilityExtensionIntervalSeconds = 3,
        });
        var nexJobOptions = new NexJobOptions { MaxAttempts = 3 };
        var logger = new MockLogger<AwsSqsTrigger>();

        var trigger = new AwsSqsTrigger(
            options,
            sqsClient,
            storageProvider,
            wakeUpChannel,
            nexJobOptions,
            logger);

        sqsClient.AddTestMessage(new Message
        {
            MessageId = "test-msg-001",
            Body = "{\"key\":\"value\"}",
            ReceiptHandle = "test-receipt-handle",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>(),
        });

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await trigger.StartAsync(cts.Token);

        await storageProvider.WaitForEnqueueAsync(cts.Token);
        await trigger.StopAsync(cts.Token);

        // Assert
        storageProvider.EnqueueCalls.Should().HaveCount(1);
        var enqueuedJob = storageProvider.EnqueueCalls[0];
        enqueuedJob.IdempotencyKey.Should().Be("test-msg-001");
        enqueuedJob.Queue.Should().Be("default");
        enqueuedJob.TraceParent.Should().BeNull();
        sqsClient.DeleteCalls.Should().HaveCount(1);
        sqsClient.DeleteCalls[0].Should().Be("test-receipt-handle");
    }

    // ─── Enqueue failure: message NOT deleted ────────────────────────────────

    [Fact]
    public async Task EnqueueFailure_MessageNotDeleted()
    {
        var sqsClient = new MockSqsClient();
        var storageProvider = new MockStorageProvider { ShouldFailEnqueue = true };
        var wakeUpChannel = new JobWakeUpChannel();
        var options = Options.Create(new AwsSqsTriggerOptions
        {
            QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/test-queue",
            JobName = typeof(TestJob).AssemblyQualifiedName!,
            MaxMessages = 1,
            WaitTimeSeconds = 1,
            VisibilityTimeoutSeconds = 5,
            VisibilityExtensionIntervalSeconds = 3,
        });
        var nexJobOptions = new NexJobOptions { MaxAttempts = 3 };
        var logger = new MockLogger<AwsSqsTrigger>();

        var trigger = new AwsSqsTrigger(
            options,
            sqsClient,
            storageProvider,
            wakeUpChannel,
            nexJobOptions,
            logger);

        sqsClient.AddTestMessage(new Message
        {
            MessageId = "test-msg-fail",
            Body = "{\"key\":\"value\"}",
            ReceiptHandle = "test-receipt-handle-fail",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>(),
        });

        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await trigger.StartAsync(cts2.Token);

        await storageProvider.WaitForEnqueueAttemptAsync(cts2.Token);
        await trigger.StopAsync(cts2.Token);

        storageProvider.EnqueueCalls.Should().HaveCount(1);
        sqsClient.DeleteCalls.Should().BeEmpty("enqueue failed — message should not be deleted");
    }

    // ─── Visibility extension: extension called before timeout ───────────────

    [Fact]
    public async Task VisibilityExtension_ExtendedWhileProcessing()
    {
        var sqsClient = new MockSqsClient();
        var storageProvider = new MockStorageProvider { EnqueueDelay = TimeSpan.FromSeconds(4) };
        var wakeUpChannel = new JobWakeUpChannel();
        var options = Options.Create(new AwsSqsTriggerOptions
        {
            QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/test-queue",
            JobName = typeof(TestJob).AssemblyQualifiedName!,
            MaxMessages = 1,
            WaitTimeSeconds = 1,
            VisibilityTimeoutSeconds = 2,
            VisibilityExtensionIntervalSeconds = 1,
        });
        var nexJobOptions = new NexJobOptions { MaxAttempts = 3 };
        var logger = new MockLogger<AwsSqsTrigger>();

        var trigger = new AwsSqsTrigger(
            options,
            sqsClient,
            storageProvider,
            wakeUpChannel,
            nexJobOptions,
            logger);

        sqsClient.AddTestMessage(new Message
        {
            MessageId = "test-msg-visibility",
            Body = "{}",
            ReceiptHandle = "test-receipt-handle-vis",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>(),
        });

        using var cts3 = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await trigger.StartAsync(cts3.Token);

        await storageProvider.WaitForEnqueueAsync(cts3.Token);
        await trigger.StopAsync(cts3.Token);

        sqsClient.VisibilityExtensionCalls.Should().BeGreaterThanOrEqualTo(
            1,
            "visibility should be extended at least once while processing");
    }

    // ─── Graceful shutdown: CancellationToken stops the loop ─────────────────

    [Fact]
    public async Task GracefulShutdown_CancellationToken_StopsCleanly()
    {
        var sqsClient = new MockSqsClient { BlockOnReceive = true };
        var storageProvider = new MockStorageProvider();
        var wakeUpChannel = new JobWakeUpChannel();
        var options = Options.Create(new AwsSqsTriggerOptions
        {
            QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/test-queue",
            JobName = typeof(TestJob).AssemblyQualifiedName!,
            MaxMessages = 1,
            WaitTimeSeconds = 20,
            VisibilityTimeoutSeconds = 30,
            VisibilityExtensionIntervalSeconds = 15,
        });
        var nexJobOptions = new NexJobOptions { MaxAttempts = 3 };
        var logger = new MockLogger<AwsSqsTrigger>();

        var trigger = new AwsSqsTrigger(
            options,
            sqsClient,
            storageProvider,
            wakeUpChannel,
            nexJobOptions,
            logger);

        using var cts4 = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await trigger.StartAsync(cts4.Token);

        await Task.Delay(TimeSpan.FromSeconds(2));
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await trigger.StopAsync(stopCts.Token);

        stopCts.Token.IsCancellationRequested.Should().BeFalse("stop should complete before timeout");
    }

    // ─── Trace propagation: traceparent extracted and set on JobRecord ───────

    [Fact]
    public async Task TracePropagation_TraceparentSetOnJobRecord()
    {
        var sqsClient = new MockSqsClient();
        var storageProvider = new MockStorageProvider();
        var wakeUpChannel = new JobWakeUpChannel();
        var options = Options.Create(new AwsSqsTriggerOptions
        {
            QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/test-queue",
            JobName = typeof(TestJob).AssemblyQualifiedName!,
            MaxMessages = 1,
            WaitTimeSeconds = 1,
            VisibilityTimeoutSeconds = 5,
            VisibilityExtensionIntervalSeconds = 3,
        });
        var nexJobOptions = new NexJobOptions { MaxAttempts = 3 };
        var logger = new MockLogger<AwsSqsTrigger>();

        var trigger = new AwsSqsTrigger(
            options,
            sqsClient,
            storageProvider,
            wakeUpChannel,
            nexJobOptions,
            logger);

        sqsClient.AddTestMessage(new Message
        {
            MessageId = "test-msg-trace",
            Body = "{}",
            ReceiptHandle = "test-receipt-handle-trace",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["traceparent"] = new MessageAttributeValue
                {
                    DataType = "String",
                    StringValue = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
                },
            },
        });

        using var cts5 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await trigger.StartAsync(cts5.Token);

        await storageProvider.WaitForEnqueueAsync(cts5.Token);
        await trigger.StopAsync(cts5.Token);

        storageProvider.EnqueueCalls.Should().HaveCount(1);
        storageProvider.EnqueueCalls[0].TraceParent.Should().Be("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
    }

    // ─── Test job type ───────────────────────────────────────────────────────

    private sealed class TestJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
