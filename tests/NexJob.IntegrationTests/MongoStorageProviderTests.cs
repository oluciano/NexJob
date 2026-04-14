using MongoDB.Bson;
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

    protected override async Task<IStorageProvider> CreateStorageAsync()
    {
        var client = new MongoClient(_fixture.Container.GetConnectionString());

        // Create a unique database for each test — ensures complete isolation like PostgreSQL tests
        var dbName = $"nexjob_test_{Guid.NewGuid():N}";
        var database = client.GetDatabase(dbName);

        var provider = new MongoStorageProvider(database);

        // Wait until the idempotency index is confirmed present
        // Necessary in CI environments where index propagation is slower
        var collection = database.GetCollection<BsonDocument>("nexjob_jobs");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            using (var indexCursor = await collection.Indexes.ListAsync())
            {
                var indexList = await indexCursor.ToListAsync();
                if (indexList.FindIndex(i => i["name"] == "idempotency_key") >= 0)
                {
                    break;
                }
            }

            await Task.Delay(25);
        }

        return provider;
    }
}
