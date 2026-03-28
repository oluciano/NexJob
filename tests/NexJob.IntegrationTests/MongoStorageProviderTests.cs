using MongoDB.Driver;
using NexJob.MongoDB;
using NexJob.Storage;
using Testcontainers.MongoDb;
using Xunit;

namespace NexJob.IntegrationTests;

/// <summary>
/// Runs the full <see cref="StorageProviderTestsBase"/> contract against a real
/// MongoDB instance spun up via Testcontainers.
/// Requires Docker to be available on the host.
/// </summary>
public sealed class MongoStorageProviderTests : StorageProviderTestsBase, IClassFixture<MongoFixture>
{
    private readonly MongoFixture _fixture;

    public MongoStorageProviderTests(MongoFixture fixture)
    {
        _fixture = fixture;

        var client = new MongoClient(_fixture.Container.GetConnectionString());

        client.DropDatabase("nexjob_test");

        var database = client.GetDatabase("nexjob_test");
        _ = new NexJob.MongoDB.MongoStorageProvider(database);
    }

    protected override Task<IStorageProvider> CreateStorageAsync()
    {
        var client = new MongoClient(_fixture.Container.GetConnectionString());
        var database = client.GetDatabase("nexjob_test");
        return Task.FromResult<IStorageProvider>(new MongoStorageProvider(database));
    }
}
