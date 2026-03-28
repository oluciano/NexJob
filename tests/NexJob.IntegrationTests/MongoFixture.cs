using System;
using Testcontainers.MongoDb;
using Xunit;

namespace NexJob.IntegrationTests;

public sealed class MongoFixture : IAsyncLifetime
{
    public MongoDbContainer Container { get; } = new MongoDbBuilder()
        .WithImage("mongo:7").Build();

    public async Task InitializeAsync()
    {
        await Container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await Container.DisposeAsync();
    }
}
