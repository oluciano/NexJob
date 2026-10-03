using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using NexJob;
using NexJob.MongoDB;
using NexJob.Storage;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Nodes of two versions sharing one MongoDB during a rolling upgrade. The driver throws on a document element a class
/// does not know, and v5.7.0 does not ignore extra elements, so what a newer node writes decides whether an older node
/// can still read it. The element names below are frozen from <c>JobDocument</c> as of v5.7.0.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class MongoMixedVersionTests
    : DistributedReliabilityTestBase,
      IClassFixture<MongoReliabilityFixture>
{
    private const string Database = "nexjob_reliability";

    // Frozen: the elements of JobDocument in v5.7.0 (Id is stored as _id; ExecutionLogs is named execution_logs).
    private static readonly string[] PreviousVersionJobElements =
    [
        "_id", "JobType", "InputType", "InputJson", "SchemaVersion", "Queue", "Priority", "Status", "IdempotencyKey",
        "Attempts", "MaxAttempts", "CreatedAt", "ScheduledAt", "ProcessingStartedAt", "HeartbeatAt", "CompletedAt",
        "RetryAt", "LastErrorMessage", "LastErrorStackTrace", "ParentJobId", "RecurringJobId", "execution_logs", "Tags",
        "ProgressPercent", "ProgressMessage", "CheckpointJson",
    ];

    private readonly MongoReliabilityFixture _fixture;

    public MongoMixedVersionTests(MongoReliabilityFixture fixture)
        => _fixture = fixture;

    // ─── N1: what a new node writes, an old node can read ─────────────────────

    [Fact]
    public async Task JobWithoutADeadline_IsStoredWithOnlyTheElementsOfThePreviousVersion()
    {
        var queue = NewQueue();
        using var host = BuildUnstartedHost();
        await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<StepJob>(queue: queue);

        var document = await Jobs().Find(Builders<BsonDocument>.Filter.Eq("Queue", queue)).SingleAsync();

        document.Names.Except(PreviousVersionJobElements).Should().BeEmpty(
            "an element the previous version does not know makes it throw when it reads the job");
    }

    // ─── N2: the documented limit ─────────────────────────────────────────────

    [Fact]
    public async Task JobWithADeadline_CarriesTheNewElement_SoItCannotBeReadByThePreviousVersion()
    {
        var queue = NewQueue();
        using var host = BuildUnstartedHost();
        await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<StepJob>(queue: queue, deadlineAfter: TimeSpan.FromHours(1));

        var document = await Jobs().Find(Builders<BsonDocument>.Filter.Eq("Queue", queue)).SingleAsync();

        document.Names.Should().Contain("ExpiresAt", "a deadline has to be stored; use deadlines only once every node is upgraded");
    }

    // ─── N3: a node tolerates what a newer one writes ─────────────────────────

    [Fact]
    public async Task Job_WithAnElementFromANewerVersion_IsStillReadAndFetched()
    {
        var queue = NewQueue();
        using var host = BuildUnstartedHost();
        var jobId = await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<StepJob>(queue: queue);
        await Jobs().UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("Queue", queue),
            Builders<BsonDocument>.Update.Set("FieldFromANewerVersion", "x"));
        var storage = host.Services.GetRequiredService<IStorageProvider>();

        var read = await storage.GetJobByIdAsync(jobId);
        var fetched = await storage.FetchNextAsync([queue]);

        read.Should().NotBeNull();
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(jobId);
    }

    [Fact]
    public async Task RecurringJob_WithAnElementFromANewerVersion_IsStillRead()
    {
        using var host = BuildUnstartedHost();
        var storage = host.Services.GetRequiredService<IStorageProvider>();
        var id = $"mixed-{Guid.NewGuid():N}";
        await storage.UpsertRecurringJobAsync(new RecurringJobRecord
        {
            RecurringJobId = id,
            JobType = typeof(StepJob).AssemblyQualifiedName!,
            InputType = typeof(string).AssemblyQualifiedName!,
            InputJson = "{}",
            Cron = "0 0 1 1 *",
            Queue = "default",
            NextExecution = DateTimeOffset.UtcNow.AddDays(30),
            CreatedAt = DateTimeOffset.UtcNow,
            ConcurrencyPolicy = RecurringConcurrencyPolicy.SkipIfRunning,
        });
        await Database_().GetCollection<BsonDocument>("nexjob_recurring_jobs").UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", id),
            Builders<BsonDocument>.Update.Set("FieldFromANewerVersion", "x"));

        var all = await storage.GetRecurringJobsAsync();

        all.Should().Contain(r => r.RecurringJobId == id);
    }

    [Fact]
    public async Task Server_WithAnElementFromANewerVersion_IsStillRead()
    {
        using var host = BuildUnstartedHost();
        var storage = host.Services.GetRequiredService<IStorageProvider>();
        var id = $"mixed-server-{Guid.NewGuid():N}";
        await storage.RegisterServerAsync(new ServerRecord
        {
            Id = id,
            WorkerCount = 1,
            Queues = ["default"],
            StartedAt = DateTimeOffset.UtcNow,
            HeartbeatAt = DateTimeOffset.UtcNow,
        });
        await Database_().GetCollection<BsonDocument>("nexjob_servers").UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", id),
            Builders<BsonDocument>.Update.Set("FieldFromANewerVersion", "x"));

        var servers = await storage.GetActiveServersAsync(TimeSpan.FromMinutes(1));

        servers.Should().Contain(s => s.Id == id);
    }

    private static string NewQueue() => $"mixed-{Guid.NewGuid():N}";

    private Microsoft.Extensions.Hosting.IHost BuildUnstartedHost() =>
        BuildHost(
            s => s.AddNexJobMongoDB(_fixture.ConnectionString, databaseName: Database),
            s => s.AddTransient<StepJob>().AddSingleton(new ExecutionLog()),
            workers: 1);

    private IMongoDatabase Database_() => new MongoClient(_fixture.ConnectionString).GetDatabase(Database);

    private IMongoCollection<BsonDocument> Jobs() => Database_().GetCollection<BsonDocument>("nexjob_jobs");
}
