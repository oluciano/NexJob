using Microsoft.Extensions.DependencyInjection;
using NexJob.MongoDB;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Retry and dead-letter scenarios on Mongo.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class MongoRetryAndDeadLetterTests
    : RetryAndDeadLetterScenarios,
      IClassFixture<MongoReliabilityFixture>
{
    private readonly MongoReliabilityFixture _fixture;

    public MongoRetryAndDeadLetterTests(MongoReliabilityFixture fixture)
        => _fixture = fixture;

    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s => s.AddNexJobMongoDB(_fixture.ConnectionString, databaseName: "nexjob_reliability");
}
