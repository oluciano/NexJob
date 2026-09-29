using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace NexJob.IntegrationTests;

/// <summary>Verifies the cleanup helper that keeps the Mongo tests below the container's open-file limit (issue #248).</summary>
public sealed class MongoTestDatabasesTests : IClassFixture<MongoFixture>
{
    private readonly MongoFixture _fixture;

    public MongoTestDatabasesTests(MongoFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DisposeAsync_DropsEveryDatabaseItCreated()
    {
        var databases = new MongoTestDatabases();
        var first = databases.Create(_fixture.Container.GetConnectionString(), "cleanup_a");
        var second = databases.Create(_fixture.Container.GetConnectionString(), "cleanup_b");
        await first.GetCollection<BsonDocument>("c").InsertOneAsync(new BsonDocument("x", 1));
        await second.GetCollection<BsonDocument>("c").InsertOneAsync(new BsonDocument("x", 1));
        (await ListDatabaseNamesAsync()).Should().Contain([first.DatabaseNamespace.DatabaseName, second.DatabaseNamespace.DatabaseName]);

        await databases.DisposeAsync();

        (await ListDatabaseNamesAsync()).Should().NotContain(
            [first.DatabaseNamespace.DatabaseName, second.DatabaseNamespace.DatabaseName],
            "each test must leave no database behind");
    }

    [Fact]
    public async Task DisposeAsync_WithoutCreatedDatabases_DoesNothing()
    {
        var databases = new MongoTestDatabases();

        var dispose = async () => await databases.DisposeAsync();

        await dispose.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisposeAsync_WhenTheDatabaseWasAlreadyDropped_DoesNotThrow()
    {
        var databases = new MongoTestDatabases();
        var database = databases.Create(_fixture.Container.GetConnectionString(), "cleanup_c");
        await database.GetCollection<BsonDocument>("c").InsertOneAsync(new BsonDocument("x", 1));
        await new MongoClient(_fixture.Container.GetConnectionString()).DropDatabaseAsync(database.DatabaseNamespace.DatabaseName);

        var dispose = async () => await databases.DisposeAsync();

        await dispose.Should().NotThrowAsync();
    }

    private async Task<List<string>> ListDatabaseNamesAsync()
    {
        var client = new MongoClient(_fixture.Container.GetConnectionString());
        using var cursor = await client.ListDatabaseNamesAsync();
        return await cursor.ToListAsync();
    }
}
