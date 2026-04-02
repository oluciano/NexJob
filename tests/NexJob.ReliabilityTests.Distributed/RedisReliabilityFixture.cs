using Testcontainers.Redis;
using Xunit;

namespace NexJob.ReliabilityTests.Distributed;

/// <summary>
/// Fixture for Redis container with connection string access.
/// </summary>
public sealed class RedisReliabilityFixture : IAsyncLifetime
{
    public RedisContainer Container { get; } = new RedisBuilder()
        .WithImage("redis:7-alpine")
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
