using Microsoft.Extensions.Logging;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// 3N Unit Testing Matrix for Job Checkpoints &amp; State Resumption (Positive, Negative, Boundary).
/// </summary>
public sealed class JobCheckpointTests
{
    private sealed record BatchState
    {
        public int LastProcessedId { get; set; }
        public int ItemsCount { get; set; }
        public string? Tag { get; set; }
    }

    [Fact]
    public async Task N1_Positive_SaveAndResumeCheckpoint_PreservesStateAcrossAttempts()
    {
        // Arrange
        var storage = new InMemoryStorageProvider();
        var jobId = new JobId(Guid.NewGuid());
        var jobRecord = new JobRecord
        {
            Id = jobId,
            JobType = "SampleBatchJob",
            Queue = "default",
            Attempts = 0,
            MaxAttempts = 3,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = JobStatus.Enqueued,
        };

        await storage.EnqueueAsync(jobRecord);

        // Fetch job for attempt 1
        var initialBatch = await storage.FetchBatchAsync(["default"], maxBatchSize: 1);
        Assert.Single(initialBatch);
        var activeJob = initialBatch[0];

        // Attempt 1: worker processes first batch and saves checkpoint
        var context1 = new JobContext(activeJob, storage);
        Assert.Null(context1.GetCheckpoint<BatchState>());

        var state1 = new BatchState
        {
            LastProcessedId = 500,
            ItemsCount = 500,
            Tag = "batch-1",
        };

        await context1.SaveCheckpointAsync(state1, percent: 50, message: "Processed 500 items");

        // Simulate retry (transient failure): commit failure with retryAt
        await storage.CommitJobResultAsync(jobId, new JobExecutionResult
        {
            Succeeded = false,
            RetryAt = DateTimeOffset.UtcNow.AddSeconds(-1), // already due
            Exception = new InvalidOperationException("Pod restarting"),
            Logs = [],
        });

        // Promote scheduled jobs so it becomes Enqueued again
        var resumedBatch = await storage.FetchBatchAsync(["default"], maxBatchSize: 1);
        Assert.Single(resumedBatch);
        var resumedRecord = resumedBatch[0];

        Assert.NotNull(resumedRecord.CheckpointJson);
        Assert.Equal(50, resumedRecord.ProgressPercent);
        Assert.Equal("Processed 500 items", resumedRecord.ProgressMessage);

        // Attempt 2: new worker receives resumed job and continues from checkpoint
        var context2 = new JobContext(resumedRecord, storage);
        var loadedState = context2.GetCheckpoint<BatchState>();

        Assert.NotNull(loadedState);
        Assert.Equal(500, loadedState.LastProcessedId);
        Assert.Equal(500, loadedState.ItemsCount);
        Assert.Equal("batch-1", loadedState.Tag);

        // Finalize job: complete with success
        await storage.CommitJobResultAsync(jobId, new JobExecutionResult
        {
            Succeeded = true,
            Logs = [],
        });

        // Verify checkpoint was auto-cleared on success
        Assert.Null(resumedRecord.CheckpointJson);
    }

    [Fact]
    public void N2_Negative_CorruptedOrIncompatibleJson_ReturnsNullGracefully()
    {
        // Arrange: JobRecord with malformed / incompatible checkpoint JSON
        var storage = new InMemoryStorageProvider();
        var jobRecord = new JobRecord
        {
            Id = new JobId(Guid.NewGuid()),
            JobType = "SampleBatchJob",
            Queue = "default",
            Attempts = 1,
            MaxAttempts = 3,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = JobStatus.Processing,
            CheckpointJson = "{ invalid-json ::: not a real payload }",
        };

        var context = new JobContext(jobRecord, storage);

        // Act & Assert: GetCheckpoint should not throw, returns null gracefully
        var checkpoint = context.GetCheckpoint<BatchState>();
        Assert.Null(checkpoint);
    }

    [Fact]
    public async Task N3_Boundary_NullStateAndInvalidPercentage_ThrowsArgumentExceptions()
    {
        // Arrange
        var storage = new InMemoryStorageProvider();
        var jobRecord = new JobRecord
        {
            Id = new JobId(Guid.NewGuid()),
            JobType = "SampleBatchJob",
            Queue = "default",
            Attempts = 1,
            MaxAttempts = 3,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = JobStatus.Processing,
        };

        var context = new JobContext(jobRecord, storage);

        // Boundary: Passing null state throws ArgumentNullException
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            context.SaveCheckpointAsync<BatchState>(null!));

        // Boundary: Percentage < 0 throws ArgumentOutOfRangeException
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            context.SaveCheckpointAsync(new BatchState(), percent: -5));

        // Boundary: Percentage > 100 throws ArgumentOutOfRangeException
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            context.SaveCheckpointAsync(new BatchState(), percent: 105));
    }

    // Issue #588: a checkpoint that exists but cannot be read is not the first attempt; it must be visible.
    [Fact]
    public void N1_UnreadableCheckpoint_ReturnsNull_AndLogsWarningWithJobAndStateType()
    {
        var logger = new CapturingLogger();
        var job = NewJob("{\"LastProcessedId\":\"not-a-number\"}");
        var context = new JobContext(job, new InMemoryStorageProvider(), logger);

        var checkpoint = context.GetCheckpoint<BatchState>();

        Assert.Null(checkpoint);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(job.Id.ToString(), entry.Message, StringComparison.Ordinal);
        Assert.Contains("SampleBatchJob", entry.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(BatchState), entry.Message, StringComparison.Ordinal);
        Assert.NotNull(entry.Exception);
    }

    [Fact]
    public void N2_ReadableCheckpoint_IsReturnedUnchanged_AndNothingIsLogged()
    {
        var logger = new CapturingLogger();
        var context = new JobContext(NewJob("{\"LastProcessedId\":7,\"ItemsCount\":9,\"Tag\":\"t\"}"), new InMemoryStorageProvider(), logger);

        var checkpoint = context.GetCheckpoint<BatchState>();

        Assert.Equal(new BatchState { LastProcessedId = 7, ItemsCount = 9, Tag = "t" }, checkpoint);
        Assert.Empty(logger.Entries);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void N3_NoCheckpoint_IsTheFirstAttempt_ReturnsNullWithoutWarning(string? checkpointJson)
    {
        var logger = new CapturingLogger();
        var context = new JobContext(NewJob(checkpointJson), new InMemoryStorageProvider(), logger);

        Assert.Null(context.GetCheckpoint<BatchState>());
        Assert.Empty(logger.Entries);
    }

    private static JobRecord NewJob(string? checkpointJson) => new()
    {
        Id = new JobId(Guid.NewGuid()),
        JobType = "SampleBatchJob",
        Queue = "default",
        Attempts = 2,
        MaxAttempts = 3,
        CreatedAt = DateTimeOffset.UtcNow,
        Status = JobStatus.Processing,
        CheckpointJson = checkpointJson,
    };

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
