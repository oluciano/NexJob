using System.Globalization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using NexJob.Tests;
using Xunit;

namespace NexJob.ReliabilityTests.Validation;

/// <summary>
/// A Kafka cluster of three KRaft brokers (each is also a controller), for the validation rounds. The brokers
/// advertise a host port chosen up front, so the test process can reach every one of them.
/// </summary>
public sealed class KafkaClusterFixture : IAsyncLifetime
{
    private const string ClusterId = "MkU3OEVBNTcwNTJENDM2Qk";
    private readonly INetwork _network = new NetworkBuilder().Build();
    private readonly int[] _hostPorts = [TestPorts.Next(), TestPorts.Next(), TestPorts.Next()];
    private readonly List<IContainer> _brokers = [];

    /// <summary>Gets the bootstrap servers of all three brokers.</summary>
    public string BootstrapServers => string.Join(",", _hostPorts.Select(p => $"localhost:{p}"));

    /// <summary>Stops one broker, as in a crash.</summary>
    /// <param name="index">The zero-based broker to stop.</param>
    /// <returns>A task that completes when the broker has stopped.</returns>
    public Task StopBrokerAsync(int index) => _brokers[index].StopAsync();

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        await _network.CreateAsync();
        for (var i = 0; i < 3; i++)
        {
            var id = i + 1;
            _brokers.Add(new ContainerBuilder()
                .WithImage("confluentinc/cp-kafka:7.6.0")
                .WithNetwork(_network)
                .WithNetworkAliases($"kafka-{id}")
                .WithPortBinding(_hostPorts[i], 9094)
                .WithEnvironment("CLUSTER_ID", ClusterId)
                .WithEnvironment("KAFKA_NODE_ID", id.ToString(CultureInfo.InvariantCulture))
                .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
                .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", "1@kafka-1:9093,2@kafka-2:9093,3@kafka-3:9093")
                .WithEnvironment("KAFKA_LISTENERS", "INTERNAL://:9092,CONTROLLER://:9093,EXTERNAL://:9094")
                .WithEnvironment("KAFKA_ADVERTISED_LISTENERS", $"INTERNAL://kafka-{id}:9092,EXTERNAL://localhost:{_hostPorts[i]}")
                .WithEnvironment("KAFKA_LISTENER_SECURITY_PROTOCOL_MAP", "CONTROLLER:PLAINTEXT,INTERNAL:PLAINTEXT,EXTERNAL:PLAINTEXT")
                .WithEnvironment("KAFKA_INTER_BROKER_LISTENER_NAME", "INTERNAL")
                .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
                .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "3")
                .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR", "3")
                .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_MIN_ISR", "2")
                .WithEnvironment("KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS", "0")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Kafka Server started"))
                .Build());
        }

        await Task.WhenAll(_brokers.Select(b => b.StartAsync()));
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        foreach (var broker in _brokers)
        {
            await broker.DisposeAsync();
        }

        await _network.DisposeAsync();
    }
}
