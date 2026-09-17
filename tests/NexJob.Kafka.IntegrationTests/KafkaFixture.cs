using Testcontainers.Kafka;
using Xunit;

namespace NexJob.Kafka.IntegrationTests;

/// <summary>
/// Shared Testcontainers Kafka fixture for integration tests.
/// </summary>
public sealed class KafkaFixture : IAsyncLifetime
{
    private readonly KafkaContainer _kafkaContainer = new KafkaBuilder()
        .WithImage("confluentinc/cp-kafka:7.6.0")
        .Build();

    /// <summary>
    /// Gets the bootstrap servers connection string for the Kafka container.
    /// </summary>
    public string BootstrapServers => _kafkaContainer.GetBootstrapAddress();

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        await _kafkaContainer.StartAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        await _kafkaContainer.DisposeAsync();
    }
}

/// <summary>
/// xUnit collection definition for sharing the Kafka container across test classes.
/// </summary>
[CollectionDefinition("Kafka")]
public sealed class KafkaTestCollection : ICollectionFixture<KafkaFixture>
{
}
