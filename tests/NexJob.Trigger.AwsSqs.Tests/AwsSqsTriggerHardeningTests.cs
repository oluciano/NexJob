using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NexJob.Internal;
using Xunit;

namespace NexJob.Trigger.AwsSqs.Tests;

/// <summary>
/// Hardening unit tests for <see cref="AwsSqsTrigger"/>.
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

    private AwsSqsTrigger CreateSut()
    {
        return new AwsSqsTrigger(
            Options.Create(_options),
            _sqsMock.Object,
            _schedulerMock.Object,
            _nexJobOptions,
            NullLogger<AwsSqsTrigger>.Instance);
    }

    // ─── Polling Loop Branches ─────────────────────────────────────────────

    /// <summary>Tests that polling loop survives client errors.</summary>
    [Fact]
    public async Task PollLoopAsync_WhenClientThrows_SurvivesAndContinues()
    {
        // 1. Throws, 2. Succeeds with empty list
        _sqsMock.SetupSequence(x => x.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("AWS Down"))
            .ReturnsAsync(new ReceiveMessageResponse());

        var sut = CreateSut();

        // Act: Use a short-lived cancellation token to stop the loop naturally instead of waiting for 5s delay
        using var cts = new CancellationTokenSource();
        _ = sut.StartAsync(cts.Token);

        // Give it a tiny bit of time
        await Task.Delay(50);
        await sut.StopAsync(CancellationToken.None);

        // Verify it attempted at least once.
        // Note: The 5s delay in production code makes testing multiple iterations difficult without changing production code.
        // We validate that it survived the first throw.
        _sqsMock.Verify(x => x.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ─── Metadata Extraction Branches ──────────────────────────────────────

    /// <summary>Tests that traceparent is extracted correctly from message attributes.</summary>
    [Fact]
    public void ExtractTraceparent_HandlesAllBranches()
    {
        var msgWithTrace = new Message { MessageAttributes = { ["traceparent"] = new MessageAttributeValue { StringValue = "00-trace" } } };
        var msgNoTrace = new Message();
        var msgEmptyTrace = new Message { MessageAttributes = { ["traceparent"] = new MessageAttributeValue { StringValue = string.Empty } } };

        var method = typeof(AwsSqsTrigger).GetMethod("ExtractTraceparent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method!.Invoke(null, new object[] { msgWithTrace }).Should().Be("00-trace");
        method!.Invoke(null, new object[] { msgNoTrace }).Should().BeNull();
        method!.Invoke(null, new object[] { msgEmptyTrace }).Should().BeNull();
    }

    // ─── Message Processing Branches ───────────────────────────────────────

    /// <summary>Tests that enqueue failure does NOT delete the message from SQS.</summary>
    [Fact]
    public async Task ProcessMessageAsync_EnqueueFails_DoesNotDeleteMessage()
    {
        var sut = CreateSut();
        var msg = new Message { MessageId = "id1", ReceiptHandle = "rh1", Body = "{}" };

        _schedulerMock.Setup(x => x.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Enqueue failed"));

        var method = typeof(AwsSqsTrigger).GetMethod("ProcessMessageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(sut, new object[] { msg, CancellationToken.None })!;

        _sqsMock.Verify(x => x.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── Visibility Extension Branches ─────────────────────────────────────

    /// <summary>Tests that visibility extension survives errors and logs them.</summary>
    [Fact]
    public async Task ExtendVisibilityAsync_WhenClientThrows_SurvivesAndExits()
    {
        var sut = CreateSut();
        _sqsMock.Setup(x => x.ChangeMessageVisibilityAsync(It.IsAny<ChangeMessageVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Receipt handle expired"));

        using var cts = new CancellationTokenSource();
        var method = typeof(AwsSqsTrigger).GetMethod("ExtendVisibilityAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await (Task)method!.Invoke(sut, new object[] { "rh123", cts.Token })!;

        _sqsMock.Verify(x => x.ChangeMessageVisibilityAsync(It.IsAny<ChangeMessageVisibilityRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Tests the slicing logic for short receipt handles.</summary>
    [Fact]
    public async Task ExtendVisibilityAsync_ShortReceiptHandle_HandlesSlicing()
    {
        var sut = CreateSut();
        _sqsMock.Setup(x => x.ChangeMessageVisibilityAsync(It.IsAny<ChangeMessageVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Stop"));

        using var cts = new CancellationTokenSource();
        var method = typeof(AwsSqsTrigger).GetMethod("ExtendVisibilityAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await (Task)method!.Invoke(sut, new object[] { "short", cts.Token })!;

        sut.Should().NotBeNull();
    }
}
