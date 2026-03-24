using Xunit;

namespace NexJob.MongoDB.Tests;

/// <summary>
/// Shared fixture that provides the MongoDB connection string for all tests.
/// Override with environment variable NEXJOB_MONGO_URI if needed.
/// </summary>
public static class MongoTestFixture
{
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("NEXJOB_MONGO_URI")
        ?? "mongodb://localhost:27017";
}

[CollectionDefinition("MongoDB")]
public sealed class MongoDbCollection : ICollectionFixture<MongoCollectionFixture> { }

/// <summary>Placeholder fixture — each test creates its own isolated database.</summary>
public sealed class MongoCollectionFixture { }
