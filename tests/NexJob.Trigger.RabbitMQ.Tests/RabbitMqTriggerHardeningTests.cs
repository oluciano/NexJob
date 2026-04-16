using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NexJob.Internal;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace NexJob.Trigger.RabbitMQ.Tests;

/// <summary>
/// Hardening unit tests for <see cref="RabbitMqTriggerHandler"/>.
/// Targets 100% branch coverage for RabbitMQ message processing and reconnection logic.
/// </summary>
public sealed class RabbitMqTriggerHardeningTests
{
    private readonly Mock<IConnectionFactory> _factoryMock = new();
    private readonly Mock<IConnection> _connectionMock = new();
    private readonly Mock<IModel> _channelMock = new();
    private readonly Mock<IScheduler> _schedulerMock = new();
    private readonly RabbitMqTriggerOptions _options = new()
    {
        QueueName = "test-q",
        ReconnectDelay = TimeSpan.FromMilliseconds(10),
    };
    private readonly NexJobOptions _nexJobOptions = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMqTriggerHardeningTests"/> class.
    /// </summary>
    public RabbitMqTriggerHardeningTests()
    {
        _factoryMock.Setup(x => x.CreateConnection()).Returns(_connectionMock.Object);
        _connectionMock.Setup(x => x.CreateModel()).Returns(_channelMock.Object);
    }

    private RabbitMqTriggerHandler CreateSut()
    {
        return new RabbitMqTriggerHandler(
            Options.Create(_options),
            _factoryMock.Object,
            _schedulerMock.Object,
            _nexJobOptions,
            NullLogger<RabbitMqTriggerHandler>.Instance);
    }

    // ─── Metadata Extraction Branches ──────────────────────────────────────

    /// <summary>Tests traceparent extraction with various header states.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task OnMessageReceived_TraceparentBranches()
    {
        var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);

        var props = new Mock<IBasicProperties>();
        IDictionary<string, object>? nullHeaders = null;
        props.Setup(p => p.Headers).Returns(nullHeaders!);

        var ea = new BasicDeliverEventArgs("t1", 1, false, "ex", "rk", props.Object, new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("{}")));

        var method = typeof(RabbitMqTriggerHandler).GetMethod("OnMessageReceivedAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(sut, new object[] { new object(), ea })!;

        _channelMock.Verify(x => x.BasicNack(1, false, false), Times.AtLeastOnce);
    }

    // ─── Idempotency Key Fallback ──────────────────────────────────────────

    /// <summary>Tests fallback from CorrelationId to MessageId for idempotency key.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task OnMessageReceived_UsesMessageId_WhenCorrelationIdIsMissing()
    {
        var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);

        var props = new Mock<IBasicProperties>();
        props.Setup(p => p.Headers).Returns(new Dictionary<string, object> { ["nexjob.job_type"] = Encoding.UTF8.GetBytes("Job") });
        string? nullCorrelation = null;
        props.Setup(p => p.CorrelationId).Returns(nullCorrelation!);
        props.Setup(p => p.MessageId).Returns("msg-123");

        var ea = new BasicDeliverEventArgs("tag", 1, false, "ex", "rk", props.Object, new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("{}")));

        var method = typeof(RabbitMqTriggerHandler).GetMethod("OnMessageReceivedAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(sut, new object[] { new object(), ea })!;

        _schedulerMock.Verify(x => x.EnqueueAsync(It.Is<JobRecord>(j => j.IdempotencyKey == "msg-123"), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Teardown Branches ────────────────────────────────────────────────

    /// <summary>Tests that teardown handles closed channels and null connections gracefully.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task StopAsync_HandlesClosedResources()
    {
        var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);

        _channelMock.Setup(x => x.IsOpen).Returns(false);

        await sut.StopAsync(CancellationToken.None);

        _channelMock.Verify(x => x.BasicCancel(It.IsAny<string>()), Times.Never);
        _channelMock.Verify(x => x.Dispose(), Times.Once);
    }

    /// <summary>Tests that teardown swallows exceptions.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Teardown_SwallowsExceptions()
    {
        var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);

        _channelMock.Setup(x => x.Dispose()).Throws(new Exception("Disposal error"));

        await sut.StopAsync(CancellationToken.None);

        _channelMock.Verify(x => x.Dispose(), Times.Once);
    }

    // ─── Reconnection Lifecycle Branches ───────────────────────────────────

    /// <summary>Tests the reconnection loop logic upon connection failure.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task StartAsync_WhenConnectionFails_RetriesUntilSuccess()
    {
        _factoryMock.SetupSequence(x => x.CreateConnection())
            .Throws(new Exception("Rabbit down"))
            .Returns(_connectionMock.Object);

        var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(50);

        _factoryMock.Verify(x => x.CreateConnection(), Times.AtLeast(2));
    }
}
