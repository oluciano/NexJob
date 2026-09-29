using System;
using Docker.DotNet.Models;
using Testcontainers.MongoDb;
using Xunit;

namespace NexJob.IntegrationTests;

public sealed class MongoFixture : IAsyncLifetime
{
    public MongoDbContainer Container { get; } = new MongoDbBuilder()
        .WithImage("mongo:7")

        // Docker's default soft limit (1024 open files) is too low: every test creates a database and mongod
        // aborts with "Too many open files" once the suite is large enough (issue #248).
        .WithCreateParameterModifier(parameters => parameters.HostConfig.Ulimits =
        [
            new Ulimit { Name = "nofile", Soft = 64000, Hard = 64000 },
        ])
        .Build();

    public async Task InitializeAsync()
    {
        await Container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await Container.DisposeAsync();
    }
}
