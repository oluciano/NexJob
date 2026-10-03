using Microsoft.Extensions.Time.Testing;
using Moq;
using NexJob.Configuration;
using NexJob.Storage;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// <see cref="IJobControlService.ResetQueueCircuitAsync"/> only talks to the in-memory circuit breaker, so it needs no
/// database: these tests cover it with the real manager (#140).
/// </summary>
public sealed class JobControlServiceCircuitResetTests
{
    public sealed class DownstreamFailureException(string message) : Exception(message)
    {
    }

    // ─── N1: reset closes an open circuit ─────────────────────────────────────

    [Fact]
    public async Task ResetQueueCircuitAsync_ClosesAnOpenCircuit()
    {
        var manager = MakeManager("payments", "orders");
        Open(manager, "payments");
        Assert.Equal(QueueCircuitState.Open, manager.GetState("payments", out _));
        var sut = MakeService(manager);

        await sut.ResetQueueCircuitAsync("payments");

        Assert.Equal(QueueCircuitState.Closed, manager.GetState("payments", out _));
        Assert.Equal(0, manager.GetStatus("payments")!.ConsecutiveFailures);
    }

    // ─── N2: reset is scoped to one queue ─────────────────────────────────────

    [Fact]
    public async Task ResetQueueCircuitAsync_DoesNotTouchTheCircuitOfAnotherQueue()
    {
        var manager = MakeManager("payments", "orders");
        Open(manager, "payments");
        Open(manager, "orders");
        var sut = MakeService(manager);

        await sut.ResetQueueCircuitAsync("payments");

        Assert.Equal(QueueCircuitState.Closed, manager.GetState("payments", out _));
        Assert.Equal(QueueCircuitState.Open, manager.GetState("orders", out _));
    }

    [Fact]
    public async Task ResetQueueCircuitAsync_NeverTouchesStorage()
    {
        var manager = MakeManager("payments");
        var storage = new Mock<IDashboardStorage>(MockBehavior.Strict);
        var runtimeStore = new Mock<IRuntimeSettingsStore>(MockBehavior.Strict);
        var sut = new DefaultJobControlService(storage.Object, runtimeStore.Object, manager);

        await sut.ResetQueueCircuitAsync("payments");

        storage.VerifyNoOtherCalls();
        runtimeStore.VerifyNoOtherCalls();
    }

    // ─── N3: boundaries ───────────────────────────────────────────────────────

    [Fact]
    public async Task ResetQueueCircuitAsync_WithoutACircuitBreakerManager_DoesNothing()
    {
        var sut = new DefaultJobControlService(
            new Mock<IDashboardStorage>(MockBehavior.Strict).Object,
            new Mock<IRuntimeSettingsStore>(MockBehavior.Strict).Object);

        var error = await Record.ExceptionAsync(() => sut.ResetQueueCircuitAsync("payments"));

        Assert.Null(error);
    }

    [Theory]
    [InlineData("never-configured")]
    [InlineData("")]
    public async Task ResetQueueCircuitAsync_ForAQueueWithoutACircuit_DoesNotThrow(string queue)
    {
        var sut = MakeService(MakeManager("payments"));

        var error = await Record.ExceptionAsync(() => sut.ResetQueueCircuitAsync(queue));

        Assert.Null(error);
    }

    private static DefaultQueueCircuitBreakerManager MakeManager(params string[] queues)
    {
        var circuits = new Dictionary<string, QueueCircuitBreakerOptions>();
        foreach (var queue in queues)
        {
            var options = new QueueCircuitBreakerOptions
            {
                ConsecutiveFailuresThreshold = 2,
                OpenDuration = TimeSpan.FromMinutes(5),
            };
            options.BreakOn<DownstreamFailureException>();
            circuits[queue] = options;
        }

        return new DefaultQueueCircuitBreakerManager(circuits, timeProvider: new FakeTimeProvider());
    }

    private static void Open(DefaultQueueCircuitBreakerManager manager, string queue)
    {
        manager.RecordOutcome(queue, succeeded: false, new DownstreamFailureException("503"));
        manager.RecordOutcome(queue, succeeded: false, new DownstreamFailureException("503"));
    }

    private static DefaultJobControlService MakeService(IQueueCircuitBreakerManager manager) =>
        new(
            new Mock<IDashboardStorage>().Object,
            new Mock<IRuntimeSettingsStore>().Object,
            manager);
}
