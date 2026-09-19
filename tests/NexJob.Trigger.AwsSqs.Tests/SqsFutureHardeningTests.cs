using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.Trigger.AwsSqs.Tests;

/// <summary>
/// Hardening and regression tests for AWS SQS trigger (TD001).
/// Verifies visibility timeout reset to 0 on enqueue failure and 3N test matrix.
/// </summary>
public sealed class SqsFutureHardeningTests
{
    private const string TestQueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/test-queue";

    // ─── N1: Positive — Enqueue succeeds, message deleted, visibility not reset ─

    /// <summary>
    /// N1 (Positive): Successful enqueue deletes message from SQS and never resets visibility to 0.
    /// </summary>
    [Fact]
    public async Task Sqs_SuccessfulEnqueue_DeletesMessage_DoesNotResetVisibilityToZero()
    {
        // Arrange
        var sqsClient = new MockSqsClient();
        var scheduler = new MockScheduler();
        var trigger = CreateTrigger(sqsClient, scheduler);

        sqsClient.AddTestMessage(new Message
        {
            MessageId = "msg-success-01",
            Body = "{\"data\":\"ok\"}",
            ReceiptHandle = "rh-success-01",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>(),
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await trigger.StartAsync(cts.Token);
        await scheduler.WaitForEnqueueAsync(cts.Token);
        await trigger.StopAsync(cts.Token);

        // Assert
        sqsClient.DeleteCalls.Should().ContainSingle().Which.Should().Be("rh-success-01");
        sqsClient.ChangeVisibilityRequests.Should().NotContain(r => r.VisibilityTimeout == 0);
    }

    // ─── N2: Negative — Enqueue failure resets visibility to 0 and stops extension ─

    /// <summary>
    /// TD001: AWS SQS visibility extension should not overlap with retry visibility.
    /// Expected: When enqueue fails, visibility extension loop stops immediately,
    /// visibility timeout is reset to 0, and message is NOT deleted.
    /// </summary>
    [Fact]
    public async Task Sqs_EnqueueFailure_ShouldStopVisibilityExtensionImmediately()
    {
        // Arrange
        var sqsClient = new MockSqsClient();
        var scheduler = new MockScheduler { ShouldFailEnqueue = true };
        var trigger = CreateTrigger(
            sqsClient,
            scheduler,
            visibilityTimeoutSeconds: 10,
            visibilityExtensionIntervalSeconds: 1);

        sqsClient.AddTestMessage(new Message
        {
            MessageId = "msg-td001-fail",
            Body = "{\"data\":\"fail\"}",
            ReceiptHandle = "rh-td001-fail",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>(),
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await trigger.StartAsync(cts.Token);
        await scheduler.WaitForEnqueueAttemptAsync(cts.Token);

        // Wait brief delay to ensure no trailing extension call occurs after failure
        await Task.Delay(TimeSpan.FromMilliseconds(500), cts.Token);
        await trigger.StopAsync(cts.Token);

        // Assert — message must NOT be deleted
        sqsClient.DeleteCalls.Should().BeEmpty("enqueue failed so message must not be deleted from SQS");

        // Assert — visibility timeout must be immediately reset to 0
        var resetRequests = sqsClient.ChangeVisibilityRequests.Where(r => r.VisibilityTimeout == 0).ToList();
        resetRequests.Should().ContainSingle("visibility timeout must be reset to 0 exactly once upon enqueue failure");
        resetRequests[0].ReceiptHandle.Should().Be("rh-td001-fail");
        resetRequests[0].QueueUrl.Should().Be(TestQueueUrl);

        // Assert — extension loop stopped immediately; no subsequent extension calls
        sqsClient.VisibilityExtensionCalls.Should().Be(0, "extension loop must be cancelled before extending");
    }

    /// <summary>
    /// N2 (Negative): When enqueue fails and resetting visibility throws an exception,
    /// the trigger loop survives and continues processing subsequent messages.
    /// </summary>
    [Fact]
    public async Task Sqs_EnqueueFailure_VisibilityResetThrows_DoesNotCrashTriggerLoop()
    {
        // Arrange
        var sqsClient = new MockSqsClient
        {
            ChangeVisibilityException = new AmazonSQSException("Simulated SQS error on visibility change"),
        };
        var scheduler = new MockScheduler();

        // First message will fail enqueue, second message will succeed
        scheduler.FailEnqueuePredicate = job => job.IdempotencyKey == "msg-first-fail";

        var trigger = CreateTrigger(sqsClient, scheduler);

        sqsClient.AddTestMessage(new Message
        {
            MessageId = "msg-first-fail",
            Body = "{\"key\":1}",
            ReceiptHandle = "rh-first-fail",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>(),
        });

        sqsClient.AddTestMessage(new Message
        {
            MessageId = "msg-second-success",
            Body = "{\"key\":2}",
            ReceiptHandle = "rh-second-success",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>(),
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        await trigger.StartAsync(cts.Token);

        // Wait for both attempts (one failed, one succeeded)
        for (var i = 0; i < 50 && sqsClient.DeleteCalls.Count == 0; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), cts.Token);
        }

        await trigger.StopAsync(cts.Token);

        // Assert — first message was NOT deleted, second message WAS deleted
        sqsClient.DeleteCalls.Should().ContainSingle().Which.Should().Be("rh-second-success");
        sqsClient.ChangeVisibilityRequests.Should().Contain(r => r.VisibilityTimeout == 0 && r.ReceiptHandle == "rh-first-fail");
    }

    // ─── N3: Boundary / Inputs ──────────────────────────────────────────────

    /// <summary>
    /// N3 (Boundary/Inputs): When cancellation token is cancelled during message processing,
    /// OperationCanceledException propagates and loop shuts down cleanly.
    /// </summary>
    [Fact]
    public async Task Sqs_Enqueue_CancellationToken_PropagatesCancellation()
    {
        // Arrange
        var sqsClient = new MockSqsClient();
        var scheduler = new MockScheduler { EnqueueDelay = TimeSpan.FromSeconds(10) };
        var trigger = CreateTrigger(sqsClient, scheduler);

        sqsClient.AddTestMessage(new Message
        {
            MessageId = "msg-cancel",
            Body = "{}",
            ReceiptHandle = "rh-cancel",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>(),
        });

        using var startCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await trigger.StartAsync(startCts.Token);

        // Cancel during enqueue
        using var stopCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await trigger.StopAsync(stopCts.Token);

        // Assert
        sqsClient.DeleteCalls.Should().BeEmpty();
    }

    /// <summary>
    /// N3 (Boundary/Inputs): Message with null body and empty attributes still safely resets visibility to 0.
    /// </summary>
    [Fact]
    public async Task Sqs_EnqueueFailure_MessageWithNullBody_ResetsVisibilitySafely()
    {
        // Arrange
        var sqsClient = new MockSqsClient();
        var scheduler = new MockScheduler { ShouldFailEnqueue = true };
        var trigger = CreateTrigger(sqsClient, scheduler);

        sqsClient.AddTestMessage(new Message
        {
            MessageId = "msg-null-body",
            Body = null,
            ReceiptHandle = "rh-null-body",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>(),
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await trigger.StartAsync(cts.Token);
        await scheduler.WaitForEnqueueAttemptAsync(cts.Token);
        await trigger.StopAsync(cts.Token);

        // Assert
        sqsClient.DeleteCalls.Should().BeEmpty();
        sqsClient.ChangeVisibilityRequests.Should().ContainSingle(r => r.VisibilityTimeout == 0 && r.ReceiptHandle == "rh-null-body");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static AwsSqsTriggerHandler CreateTrigger(
        ISqsClient sqsClient,
        IScheduler scheduler,
        int visibilityTimeoutSeconds = 5,
        int visibilityExtensionIntervalSeconds = 3)
    {
        var options = Options.Create(new AwsSqsTriggerOptions
        {
            QueueUrl = TestQueueUrl,
            JobName = typeof(TestJob).AssemblyQualifiedName!,
            MaxMessages = 1,
            WaitTimeSeconds = 1,
            VisibilityTimeoutSeconds = visibilityTimeoutSeconds,
            VisibilityExtensionIntervalSeconds = visibilityExtensionIntervalSeconds,
        });

        var nexJobOptions = new NexJobOptions { MaxAttempts = 3 };
        var logger = new MockLogger<AwsSqsTriggerHandler>();

        return new AwsSqsTriggerHandler(
            options,
            sqsClient,
            scheduler,
            nexJobOptions,
            logger);
    }

    private sealed class TestJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
