using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using NexJob.MongoDB;
using Xunit;
using Xunit.Abstractions;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Connections seen by MongoDB while NexJob works.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class MongoConnectionTests
    : ConnectionCountScenarios,
      IClassFixture<MongoReliabilityFixture>
{
    private readonly MongoReliabilityFixture _fixture;
    private readonly Lazy<IMongoDatabase> _admin;

    public MongoConnectionTests(MongoReliabilityFixture fixture, ITestOutputHelper output)
        : base(output)
    {
        _fixture = fixture;
        _admin = new Lazy<IMongoDatabase>(() => new MongoClient(fixture.ConnectionString).GetDatabase("admin"));
    }

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobMongoDB(_fixture.ConnectionString, databaseName: "nexjob_reliability");

    /// <inheritdoc/>
    protected override async Task<int> CountServerConnectionsAsync()
    {
        var status = await _admin.Value.RunCommandAsync<BsonDocument>(new BsonDocument("serverStatus", 1));
        return status["connections"]["current"].ToInt32();
    }
}
