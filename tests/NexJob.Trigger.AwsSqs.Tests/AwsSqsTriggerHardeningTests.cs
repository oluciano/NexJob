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

        var method = typeof(AwsSqsTrigger).GetMethod("ExtractTraceparent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

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
        var method = typeof(AwsSqsTrigger).GetMethod("ExtendVisibilityAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(sut, new object[] { "rh123", cts.Token })!;

        _sqsMock.Verify(x => x.ChangeMessageVisibilityAsync(It.IsAny<ChangeMessageVisibilityRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
