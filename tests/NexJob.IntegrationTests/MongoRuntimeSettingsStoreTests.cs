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
    : RuntimeSettingsStoreTestsBase, IClassFixture<MongoFixture>
{
    private readonly MongoFixture _fixture;

    public MongoRuntimeSettingsStoreTests(MongoFixture fixture)
    {
        _fixture = fixture;
    }

    protected override async Task<IRuntimeSettingsStore> CreateStoreAsync()
    {
        var client = new MongoClient(_fixture.Container.GetConnectionString());
        var dbName = $"nexjob_rt_{Guid.NewGuid():N}";
        var database = client.GetDatabase(dbName);
        return await Task.FromResult(new MongoRuntimeSettingsStore(database));
    }
}
