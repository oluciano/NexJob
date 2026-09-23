using System.Reflection;
using NexJob;
using Xunit;

namespace NexJob.Tests;

public sealed class ListenerRegistryTests
{
    // N1 - Positive: Happy path registration, status updates, and retrieval
    [Fact]
    public void RegisterAndGet_ReturnsRegisteredListeners()
    {
        var registry = new DefaultListenerRegistry();
        var registration = new ListenerRegistration(
            Id: "rabbitmq:orders-queue",
            Broker: "RabbitMQ",
            Endpoint: "orders-queue",
            TargetJobType: "ProcessOrderJob",
            JobTag: "trigger:rabbitmq");

        registry.Register(registration);

        var list = registry.GetAll();
        Assert.Single(list);
        var item = list[0];
        Assert.Equal("rabbitmq:orders-queue", item.Id);
        Assert.Equal("RabbitMQ", item.Broker);
        Assert.Equal("orders-queue", item.Endpoint);
        Assert.Equal("ProcessOrderJob", item.TargetJobType);
        Assert.Equal(ListenerStatus.Starting, item.Status);
        Assert.Equal("trigger:rabbitmq", item.JobTag);
    }

    [Fact]
    public void UpdateStatus_ChangesStateAndTimestamp()
    {
        var registry = new DefaultListenerRegistry();
        var registration = new ListenerRegistration(
            Id: "kafka:payment-topic",
            Broker: "Kafka",
            Endpoint: "payment-topic",
            TargetJobType: "ProcessPaymentJob",
            JobTag: "trigger:kafka");

        registry.Register(registration);
        registry.UpdateStatus("kafka:payment-topic", ListenerStatus.Listening, "Connected to cluster");

        var snapshot = registry.Get("kafka:payment-topic");
        Assert.NotNull(snapshot);
        Assert.Equal(ListenerStatus.Listening, snapshot!.Status);
        Assert.Equal("Connected to cluster", snapshot.StatusDescription);
    }

    // N2 - Negative: Failure and reconnecting state transitions
    [Fact]
    public void UpdateStatus_ToFaultedOrReconnecting_CapturesError()
    {
        var registry = new DefaultListenerRegistry();
        var registration = new ListenerRegistration(
            Id: "rabbitmq:dead-broker",
            Broker: "RabbitMQ",
            Endpoint: "dead-queue",
            TargetJobType: "MyJob",
            JobTag: "trigger:rabbitmq");

        registry.Register(registration);
        registry.UpdateStatus("rabbitmq:dead-broker", ListenerStatus.Reconnecting, "Connection reset by peer");

        var item = registry.Get("rabbitmq:dead-broker");
        Assert.NotNull(item);
        Assert.Equal(ListenerStatus.Reconnecting, item!.Status);
        Assert.Equal("Connection reset by peer", item.StatusDescription);

        registry.UpdateStatus("rabbitmq:dead-broker", ListenerStatus.Stopped);
        Assert.Equal(ListenerStatus.Stopped, registry.Get("rabbitmq:dead-broker")!.Status);
    }

    // N3 - Invalid Input / Boundaries: Nulls, empty IDs, non-existent entries
    [Fact]
    public void Register_NullOrEmptyId_ThrowsArgumentException()
    {
        var registry = new DefaultListenerRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
        Assert.Throws<ArgumentException>(() => registry.Register(new ListenerRegistration(
            Id: string.Empty,
            Broker: "Kafka",
            Endpoint: "topic",
            TargetJobType: "Job",
            JobTag: "tag")));
    }

    [Fact]
    public void UpdateStatus_NonExistentId_IsGracefulNoOp()
    {
        var registry = new DefaultListenerRegistry();
        // Should not throw
        registry.UpdateStatus("non-existent-id", ListenerStatus.Listening);
        Assert.Null(registry.Get("non-existent-id"));
        Assert.Empty(registry.GetAll());
    }
}
