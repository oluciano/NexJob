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

    /// <summary>Tests correct extraction of traceparent and job_type from headers.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task OnMessageReceived_WithValidHeaders_EnqueuesCorrectly()
    {
        var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);

        var props = new Mock<IBasicProperties>();
        props.Setup(p => p.Headers).Returns(new Dictionary<string, object>
        {
            ["traceparent"] = Encoding.UTF8.GetBytes("00-trace-01"),
            ["nexjob.job_type"] = Encoding.UTF8.GetBytes("TestJob"),
        });

        var ea = new BasicDeliverEventArgs("tag", 1, false, "ex", "rk", props.Object, new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("{}")));

        // Invoke private handler via reflection
        var method = typeof(RabbitMqTriggerHandler).GetMethod("OnMessageReceivedAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(sut, new object[] { new object(), ea })!;

        _schedulerMock.Verify(x => x.EnqueueAsync(It.Is<JobRecord>(j => j.TraceParent == "00-trace-01" && j.JobType == "TestJob"), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()), Times.Once);
        _channelMock.Verify(x => x.BasicAck(1, false), Times.Once);
    }

    // ─── Error Handling Branches ───────────────────────────────────────────

    /// <summary>Tests that missing job_type header causes a Nack without requeue.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task OnMessageReceived_MissingJobType_NacksWithoutRequeue()
    {
        var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);

        var props = new Mock<IBasicProperties>();
        props.Setup(p => p.Headers).Returns(new Dictionary<string, object>());

        var ea = new BasicDeliverEventArgs("tag", 1, false, "ex", "rk", props.Object, new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("{}")));

        var method = typeof(RabbitMqTriggerHandler).GetMethod("OnMessageReceivedAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(sut, new object[] { new object(), ea })!;

        _channelMock.Verify(x => x.BasicNack(1, false, false), Times.Once);
    }

    /// <summary>Tests that cancellation during enqueue causes a Nack with requeue.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task OnMessageReceived_Cancellation_NacksWithRequeue()
    {
        var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);

        var props = new Mock<IBasicProperties>();
        props.Setup(p => p.Headers).Returns(new Dictionary<string, object> { ["nexjob.job_type"] = Encoding.UTF8.GetBytes("Job") });

        _schedulerMock.Setup(x => x.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var ea = new BasicDeliverEventArgs("tag", 1, false, "ex", "rk", props.Object, new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("{}")));

        var method = typeof(RabbitMqTriggerHandler).GetMethod("OnMessageReceivedAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(sut, new object[] { new object(), ea })!;

        _channelMock.Verify(x => x.BasicNack(1, false, true), Times.Once);
    }

    // ─── Reconnection Lifecycle Branches ───────────────────────────────────

    /// <summary>Tests the reconnection loop logic upon connection failure.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task StartAsync_WhenConnectionFails_RetriesUntilSuccess()
    {
        // 1st attempt fails, 2nd succeeds
        _factoryMock.SetupSequence(x => x.CreateConnection())
            .Throws(new Exception("Rabbit down"))
            .Returns(_connectionMock.Object);

        var sut = CreateSut();

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(50); // Allow reconnect task to run

        _factoryMock.Verify(x => x.CreateConnection(), Times.AtLeast(2));
    }
}
