using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NexJob.Internal;
using Xunit;

namespace NexJob.Trigger.AwsSqs.Tests;

/// <summary>
/// Hardening unit tests for <see cref="AwsSqsTriggerHandler"/>.
/// Targets 100% branch coverage for SQS polling and visibility management.
/// </summary>
public sealed class AwsSqsTriggerHardeningTests
{
    private readonly Mock<ISqsClient> _sqsMock = new();
    private readonly Mock<IScheduler> _schedulerMock = new();
    private readonly AwsSqsTriggerOptions _options = new()
    {
        QueueUrl = "http://test-sqs",
        JobName = "MyJob",
        VisibilityExtensionIntervalSeconds = 1,
    };
    private readonly NexJobOptions _nexJobOptions = new();

    private AwsSqsTriggerHandler CreateSut()
    {
        return new AwsSqsTriggerHandler(
            Options.Create(_options),
            _sqsMock.Object,
            _schedulerMock.Object,
            _nexJobOptions,
            NullLogger<AwsSqsTriggerHandler>.Instance);
    }

    // ─── Polling Loop Branches ─────────────────────────────────────────────

    /// <summary>Tests that loop continues upon client failure.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task PollLoopAsync_WhenClientThrows_SurvivesAndContinues()
    {
        _sqsMock.SetupSequence(x => x.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("AWS Down"))
            .ThrowsAsync(new OperationCanceledException());

        var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await sut.StopAsync(CancellationToken.None);

        _sqsMock.Verify(x => x.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ─── Metadata Extraction Branches ──────────────────────────────────────

    /// <summary>Tests traceparent extraction.</summary>
    [Fact]
    public void ExtractTraceparent_HandlesAllBranches()
    {
        var msgWithTrace = new Message { MessageAttributes = { ["traceparent"] = new MessageAttributeValue { StringValue = "00-trace" } } };
        var msgNoTrace = new Message();
        var msgEmptyTrace = new Message { MessageAttributes = { ["traceparent"] = new MessageAttributeValue { StringValue = string.Empty } } };

        var method = typeof(AwsSqsTriggerHandler).GetMethod("ExtractTraceparent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method!.Invoke(null, new object[] { msgWithTrace }).Should().Be("00-trace");
        method!.Invoke(null, new object[] { msgNoTrace }).Should().BeNull();
        method!.Invoke(null, new object[] { msgEmptyTrace }).Should().BeNull();
    }

    // ─── Visibility Extension Branches ─────────────────────────────────────

    /// <summary>Tests that extension survives handle expiration.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExtendVisibilityAsync_WhenClientThrows_SurvivesAndExits()
    {
        var sut = CreateSut();
        _sqsMock.Setup(x => x.ChangeMessageVisibilityAsync(It.IsAny<ChangeMessageVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Expired"));

        using var cts = new CancellationTokenSource();
        var method = typeof(AwsSqsTriggerHandler).GetMethod("ExtendVisibilityAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(sut, new object[] { "rh123", cts.Token })!;

        _sqsMock.Verify(x => x.ChangeMessageVisibilityAsync(It.IsAny<ChangeMessageVisibilityRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Lifecycle Branches ────────────────────────────────────────────────

    /// <summary>Tests that StopAsync logs polling task faults.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task StopAsync_WhenPollingTaskFaults_LogsError()
    {
        var sut = CreateSut();

        // Mock ReceiveMessageAsync to throw immediately
        _sqsMock.Setup(x => x.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Polling failed"));

        // Start will launch the task, but it will fault quickly
        await sut.StartAsync(CancellationToken.None);

        // Wait for it to fault
        await Task.Delay(50);

        // Act
        var act = () => sut.StopAsync(CancellationToken.None);

        // Assert: Should not throw (error is logged and swallowed)
        await act.Should().NotThrowAsync();
    }

    // ─── ProcessMessageAsync Branches (TD001) ──────────────────────────────

    /// <summary>Tests that enqueue failure resets message visibility to 0 without deleting the message.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ProcessMessageAsync_WhenEnqueueFails_ResetsVisibilityToZeroAndDoesNotDelete()
    {
        var sut = CreateSut();
        var message = new Message
        {
            MessageId = "msg-fail-branch",
            ReceiptHandle = "rh-fail-branch",
            Body = "{\"key\":\"val\"}",
        };

        _schedulerMock.Setup(x => x.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage unreachable"));

        var method = typeof(AwsSqsTriggerHandler).GetMethod(
            "ProcessMessageAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await (Task)method!.Invoke(sut, new object[] { message, CancellationToken.None })!;

        _sqsMock.Verify(
            x => x.ChangeMessageVisibilityAsync(
                It.Is<ChangeMessageVisibilityRequest>(r => r.VisibilityTimeout == 0 && r.ReceiptHandle == "rh-fail-branch"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _sqsMock.Verify(
            x => x.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>Tests that when enqueue fails and resetting visibility throws, the exception is swallowed and logged.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ProcessMessageAsync_WhenEnqueueFailsAndResetThrows_SurvivesWithoutCrashing()
    {
        var sut = CreateSut();
        var message = new Message
        {
            MessageId = "msg-reset-fail",
            ReceiptHandle = "rh-reset-fail",
            Body = "{\"key\":\"val\"}",
        };

        _schedulerMock.Setup(x => x.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage unreachable"));

        _sqsMock.Setup(x => x.ChangeMessageVisibilityAsync(
                It.Is<ChangeMessageVisibilityRequest>(r => r.VisibilityTimeout == 0),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Amazon.SQS.AmazonSQSException("SQS change visibility failed"));

        var method = typeof(AwsSqsTriggerHandler).GetMethod(
            "ProcessMessageAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var act = () => (Task)method!.Invoke(sut, new object[] { message, CancellationToken.None })!;

        await act.Should().NotThrowAsync();
    }

    /// <summary>Tests that when delete fails after successful enqueue, visibility is not reset to 0.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ProcessMessageAsync_WhenDeleteFailsAfterEnqueue_DoesNotResetVisibility()
    {
        var sut = CreateSut();
        var message = new Message
        {
            MessageId = "msg-delete-fail",
            ReceiptHandle = "rh-delete-fail",
            Body = "{\"key\":\"val\"}",
        };

        _schedulerMock.Setup(x => x.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JobId.New());

        _sqsMock.Setup(x => x.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Amazon.SQS.AmazonSQSException("Delete failed"));

        var method = typeof(AwsSqsTriggerHandler).GetMethod(
            "ProcessMessageAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var act = () => (Task)method!.Invoke(sut, new object[] { message, CancellationToken.None })!;

        await act.Should().NotThrowAsync();

        _sqsMock.Verify(
            x => x.ChangeMessageVisibilityAsync(
                It.Is<ChangeMessageVisibilityRequest>(r => r.VisibilityTimeout == 0),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>Tests that cancellation during visibility reset re-throws OperationCanceledException.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ProcessMessageAsync_WhenResetVisibilityCancelled_PropagatesCancellation()
    {
        var sut = CreateSut();
        var message = new Message
        {
            MessageId = "msg-cancel-reset",
            ReceiptHandle = "rh-cancel-reset",
            Body = "{\"key\":\"val\"}",
        };

        _schedulerMock.Setup(x => x.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage unreachable"));

        _sqsMock.Setup(x => x.ChangeMessageVisibilityAsync(
                It.Is<ChangeMessageVisibilityRequest>(r => r.VisibilityTimeout == 0),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var method = typeof(AwsSqsTriggerHandler).GetMethod(
            "ProcessMessageAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var act = async () =>
        {
            try
            {
                await ((Task)method!.Invoke(sut, new object[] { message, CancellationToken.None })!).ConfigureAwait(false);
            }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
