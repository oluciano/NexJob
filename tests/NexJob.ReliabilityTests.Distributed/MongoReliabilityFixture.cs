using Testcontainers.MongoDb;
using Xunit;

namespace NexJob.ReliabilityTests.Distributed;

/// <summary>
/// Fixture for MongoDB container with connection string access.
/// </summary>
public sealed class MongoReliabilityFixture : IAsyncLifetime
{
    public MongoDbContainer Container { get; } = new MongoDbBuilder()
        .WithImage("mongo:7")
        .Build();

    public string ConnectionString => Container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await Container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await Container.DisposeAsync();
    }
}
