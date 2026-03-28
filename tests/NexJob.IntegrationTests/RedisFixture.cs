using System;
using Testcontainers.Redis;
using Xunit;

namespace NexJob.IntegrationTests;

public sealed class RedisFixture : IAsyncLifetime
{
    public RedisContainer Container { get; } = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    // Use async/await para converter automaticamente ValueTask em Task
    public async Task InitializeAsync()
    {
        await Container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await Container.DisposeAsync();
    }
}
