using Microsoft.Extensions.DependencyInjection;
using NexJob.MongoDB;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Restart scenarios on Mongo.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class MongoRecoveryTests
    : RecoveryScenarios,
      IClassFixture<MongoReliabilityFixture>
{
    private readonly MongoReliabilityFixture _fixture;

    public MongoRecoveryTests(MongoReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobMongoDB(_fixture.ConnectionString, databaseName: "nexjob_reliability");
}
