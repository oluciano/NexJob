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
    }

    protected override Task<IStorageProvider> CreateStorageAsync()
    {
        var client = new MongoClient(_fixture.Container.GetConnectionString());

        // Create a unique database for each test — ensures complete isolation like PostgreSQL tests
        var dbName = $"nexjob_test_{Guid.NewGuid():N}";
        var database = client.GetDatabase(dbName);

        return Task.FromResult<IStorageProvider>(new MongoStorageProvider(database));
    }
}
