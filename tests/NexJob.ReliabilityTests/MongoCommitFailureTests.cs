using Microsoft.Extensions.DependencyInjection;
using NexJob.MongoDB;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// A failing result commit on a MongoDB database.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class MongoCommitFailureTests
    : CommitFailureScenarios,
      IClassFixture<MongoReliabilityFixture>
{
    private readonly MongoReliabilityFixture _fixture;

    public MongoCommitFailureTests(MongoReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobMongoDB(_fixture.ConnectionString, databaseName: "nexjob_reliability");
}
