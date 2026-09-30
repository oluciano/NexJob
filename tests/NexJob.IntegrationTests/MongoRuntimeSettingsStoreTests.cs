using MongoDB.Driver;
using NexJob.Configuration;
using NexJob.MongoDB;
using Xunit;

namespace NexJob.IntegrationTests;

/// <summary>
/// Tests <see cref="MongoRuntimeSettingsStore"/> contract against a real MongoDB instance.
/// Requires Docker to be available on the host.
/// </summary>
public sealed class MongoRuntimeSettingsStoreTests
    : RuntimeSettingsStoreTestsBase, IClassFixture<MongoFixture>, IAsyncLifetime
{
    private readonly MongoFixture _fixture;
    private readonly MongoTestDatabases _databases = new();

    public MongoRuntimeSettingsStoreTests(MongoFixture fixture)
    {
        _fixture = fixture;
    }

    protected override async Task<IRuntimeSettingsStore> CreateStoreAsync()
    {
        var database = _databases.Create(_fixture.Container.GetConnectionString(), "nexjob_rt");
        return await Task.FromResult(new MongoRuntimeSettingsStore(database));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _databases.DisposeAsync();
}
