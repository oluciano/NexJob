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
/// Targets 100% branch coverage for RabbitMQ message processing and lifecycle.
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

    /// <summary>Constructor.</summary>
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

    /// <summary>Tests that traceparent is extracted correctly from headers.</summary>
    [Fact]
    public async Task OnMessageReceived_WithTraceparentHeader_PropagatesToJob()
    {
        var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);

        var props = new Mock<IBasicProperties>();
        var headers = new Dictionary<string, object>
        {
            ["traceparent"] = Encoding.UTF8.GetBytes("00-trace-01"),
            ["nexjob.job_type"] = Encoding.UTF8.GetBytes("MyJob"),
        };
        props.Setup(p => p.Headers).Returns(headers);

        var ea = new BasicDeliverEventArgs("tag", 1, false, "ex", "rk", props.Object, new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("{}")));

        // Use reflection to invoke the private handler
        var method = typeof(RabbitMqTriggerHandler).GetMethod("OnMessageReceivedAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(sut, new object[] { new object(), ea })!;

        _schedulerMock.Verify(x => x.EnqueueAsync(It.Is<JobRecord>(j => j.TraceParent == "00-trace-01"), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Tests that missing job_type header causes a Nack.</summary>
    [Fact]
    public async Task OnMessageReceived_MissingJobTypeHeader_NacksWithoutRequeue()
    {
        var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);

        var props = new Mock<IBasicProperties>();
        props.Setup(p => p.Headers).Returns(new Dictionary<string, object>()); // Empty headers

        var ea = new BasicDeliverEventArgs("tag", 1, false, "ex", "rk", props.Object, new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("{}")));

        var method = typeof(RabbitMqTriggerHandler).GetMethod("OnMessageReceivedAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(sut, new object[] { new object(), ea })!;

        _channelMock.Verify(x => x.BasicNack(1, false, false), Times.Once);
        _schedulerMock.Verify(x => x.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── Error Handling Branches ───────────────────────────────────────────

    /// <summary>Tests that scheduler failure causes a Nack without requeue.</summary>
    [Fact]
    public async Task OnMessageReceived_SchedulerThrows_NacksWithoutRequeue()
    {
        var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);

        var props = new Mock<IBasicProperties>();
        props.Setup(p => p.Headers).Returns(new Dictionary<string, object> { ["nexjob.job_type"] = Encoding.UTF8.GetBytes("Job") });
        _schedulerMock.Setup(x => x.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB Down"));

        var ea = new BasicDeliverEventArgs("tag", 1, false, "ex", "rk", props.Object, new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("{}")));

        var method = typeof(RabbitMqTriggerHandler).GetMethod("OnMessageReceivedAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(sut, new object[] { new object(), ea })!;

        _channelMock.Verify(x => x.BasicNack(1, false, false), Times.Once);
    }

    /// <summary>Tests that cancellation during processing causes a Nack with requeue.</summary>
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

    // ─── Lifecycle Branches ────────────────────────────────────────────────

    /// <summary>Tests that initial connection failure starts the reconnect loop.</summary>
    [Fact]
    public async Task StartAsync_ConnectionFails_StartsReconnectLoop()
    {
        // 1st attempt fails, 2nd succeeds
        _factoryMock.SetupSequence(x => x.CreateConnection())
            .Throws(new Exception("Rabbit down"))
            .Returns(_connectionMock.Object);

        var sut = CreateSut();

        // Act
        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(50); // Allow reconnect loop to run

        // Assert
        _factoryMock.Verify(x => x.CreateConnection(), Times.AtLeast(2));
    }

    /// <summary>Tests that StopAsync cancels the reconnect loop.</summary>
    [Fact]
    public async Task StopAsync_CancelsReconnectLoop()
    {
        _factoryMock.Setup(x => x.CreateConnection()).Throws(new Exception("Rabbit down"));
        var sut = CreateSut();

        await sut.StartAsync(CancellationToken.None);
        await sut.StopAsync(CancellationToken.None);

        // Verify it doesn't keep retrying forever
        var countBefore = Moq.Mock.Get(_factoryMock.Object).Invocations.Count;
        await Task.Delay(50);
        var countAfter = Moq.Mock.Get(_factoryMock.Object).Invocations.Count;

        countAfter.Should().Be(countBefore);
    }
}
