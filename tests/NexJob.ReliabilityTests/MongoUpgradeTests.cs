using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using NexJob.MongoDB;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Upgrade scenarios on MongoDB: documents written by the previous version have no <c>ExpiresAt</c> element.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class MongoUpgradeTests
    : UpgradeScenarios,
      IClassFixture<MongoReliabilityFixture>
{
    private readonly MongoReliabilityFixture _fixture;

    public MongoUpgradeTests(MongoReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobMongoDB(_fixture.ConnectionString, databaseName: "nexjob_reliability");

    /// <inheritdoc/>
    protected override async Task MoveBackToPreviousVersionAsync()
    {
        var jobs = new MongoClient(_fixture.ConnectionString).GetDatabase("nexjob_reliability").GetCollection<BsonDocument>("nexjob_jobs");
        await jobs.UpdateManyAsync(FilterDefinition<BsonDocument>.Empty, Builders<BsonDocument>.Update.Unset("ExpiresAt"));
    }

    /// <inheritdoc/>
    protected override Task AssertUpgradedAsync() => Task.CompletedTask; // no schema: the element is added when a job is written
}
