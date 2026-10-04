using Microsoft.Extensions.DependencyInjection;
using NexJob.MongoDB;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Several nodes on one Mongo database.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class MongoMultiNodeTests
    : MultiNodeScenarios,
      IClassFixture<MongoReliabilityFixture>
{
    private readonly MongoReliabilityFixture _fixture;

    public MongoMultiNodeTests(MongoReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobMongoDB(_fixture.ConnectionString, databaseName: "nexjob_reliability");
}
