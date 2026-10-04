using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using NexJob.MongoDB;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// <c>AddNexJobMongoDB(IMongoDatabase)</c> against a real MongoDB that requires a user and password.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class MongoLiveObjectTests
    : LiveObjectOverloadScenarios,
      IClassFixture<MongoReliabilityFixture>
{
    private readonly MongoReliabilityFixture _fixture;

    public MongoLiveObjectTests(MongoReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override (Action<IServiceCollection> Register, IAsyncDisposable Owned) CreateOwnedObject()
    {
        var database = new MongoClient(_fixture.ConnectionString).GetDatabase("nexjob_reliability");
        return (s => s.AddNexJobMongoDB(database), new Owned(database));
    }

    /// <inheritdoc/>
    protected override async Task AssertStillUsableAsync(IAsyncDisposable owned)
    {
        var database = ((Owned)owned).Database;
        var reply = await database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
        Assert.Equal(1.0, reply["ok"].ToDouble());
    }

    private sealed class Owned(IMongoDatabase database) : IAsyncDisposable
    {
        public IMongoDatabase Database { get; } = database;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
