using FluentAssertions;
using NexJob.Storage;
using Xunit;

namespace NexJob.IntegrationTests;

/// <summary>
/// Abstract base class that defines the full contract test suite for any
/// <see cref="IStorageProvider"/> implementation.
/// Subclasses provide the concrete provider via <see cref="CreateStorageAsync"/>.
/// </summary>
public abstract class StorageProviderTestsBase
{
    /// <summary>Creates and returns a ready-to-use storage provider for a clean test run.</summary>
    protected abstract Task<IStorageProvider> CreateStorageAsync();

    // ── Enqueue & fetch ────────────────────────────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_then_FetchNextAsync_returns_the_job()
    {
        var storage = await CreateStorageAsync();
        var record  = MakeJob();

        await storage.EnqueueAsync(record);
        var fetched = await storage.FetchNextAsync(["default"]);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(record.Id);
        fetched.Status.Should().Be(JobStatus.Processing);
    }

    [Fact]
    public async Task FetchNextAsync_respects_queue_filter()
    {
        var storage = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob(queue: "high"));
        await storage.EnqueueAsync(MakeJob(queue: "low"));

        var fetched = await storage.FetchNextAsync(["low"]);

        fetched.Should().NotBeNull();
        fetched!.Queue.Should().Be("low");
    }

    [Fact]
    public async Task FetchNextAsync_returns_null_when_queue_is_empty()
    {
        var storage = await CreateStorageAsync();

        var fetched = await storage.FetchNextAsync(["default"]);

        fetched.Should().BeNull();
    }

    [Fact]
    public async Task FetchNextAsync_does_not_return_same_job_twice()
    {
        var storage = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());

        var first  = await storage.FetchNextAsync(["default"]);
        var second = await storage.FetchNextAsync(["default"]);

        first.Should().NotBeNull();
        second.Should().BeNull();
    }

    // ── Status transitions ─────────────────────────────────────────────────────

    [Fact]
    public async Task AcknowledgeAsync_sets_status_to_Succeeded()
    {
        var storage = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());
        var fetched = (await storage.FetchNextAsync(["default"]))!;

        await storage.AcknowledgeAsync(fetched.Id);
        var updated = await storage.GetJobByIdAsync(fetched.Id);

        updated!.Status.Should().Be(JobStatus.Succeeded);
        updated.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SetFailedAsync_with_retryAt_re_enqueues_job()
    {
        var storage = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());
        var fetched = (await storage.FetchNextAsync(["default"]))!;

        var retryAt = DateTimeOffset.UtcNow.AddMinutes(1);
        await storage.SetFailedAsync(fetched.Id,
            new InvalidOperationException("boom"), retryAt);

        var updated = await storage.GetJobByIdAsync(fetched.Id);
        updated!.LastErrorMessage.Should().Contain("boom");
        updated.RetryAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SetFailedAsync_without_retryAt_moves_job_to_Failed()
    {
        var storage = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());
        var fetched = (await storage.FetchNextAsync(["default"]))!;

        await storage.SetFailedAsync(fetched.Id,
            new InvalidOperationException("fatal"), retryAt: null);

        var updated = await storage.GetJobByIdAsync(fetched.Id);
        updated!.Status.Should().Be(JobStatus.Failed);
    }

    // ── Idempotency ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_with_same_idempotency_key_is_deduplicated()
    {
        var storage = await CreateStorageAsync();
        var key     = Guid.NewGuid().ToString();

        await storage.EnqueueAsync(MakeJob(idempotencyKey: key));
        await storage.EnqueueAsync(MakeJob(idempotencyKey: key));

        var metrics = await storage.GetMetricsAsync();
        metrics.Enqueued.Should().Be(1);
    }

    // ── Dashboard methods ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetMetricsAsync_counts_enqueued_jobs()
    {
        var storage = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());
        await storage.EnqueueAsync(MakeJob());

        var metrics = await storage.GetMetricsAsync();

        metrics.Enqueued.Should().Be(2);
    }

    [Fact]
    public async Task GetJobsAsync_returns_paged_results()
    {
        var storage = await CreateStorageAsync();
        for (var i = 0; i < 5; i++)
            await storage.EnqueueAsync(MakeJob());

        var page = await storage.GetJobsAsync(new JobFilter(), page: 1, pageSize: 3);

        page.Items.Should().HaveCount(3);
        page.TotalCount.Should().Be(5);
        page.TotalPages.Should().Be(2);
    }

    [Fact]
    public async Task GetJobByIdAsync_returns_null_for_unknown_id()
    {
        var storage = await CreateStorageAsync();

        var result = await storage.GetJobByIdAsync(new JobId(Guid.NewGuid()));

        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteJobAsync_removes_the_job()
    {
        var storage = await CreateStorageAsync();
        var job     = MakeJob();
        await storage.EnqueueAsync(job);

        await storage.DeleteJobAsync(job.Id);
        var result = await storage.GetJobByIdAsync(job.Id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RequeueJobAsync_resets_failed_job_to_enqueued()
    {
        var storage = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());
        var fetched = (await storage.FetchNextAsync(["default"]))!;
        await storage.SetFailedAsync(fetched.Id,
            new Exception("error"), retryAt: null);

        await storage.RequeueJobAsync(fetched.Id);
        var updated = await storage.GetJobByIdAsync(fetched.Id);

        updated!.Status.Should().Be(JobStatus.Enqueued);
        updated.Attempts.Should().Be(0);
    }

    // ── Recurring jobs ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertRecurringJobAsync_and_GetRecurringJobsAsync_roundtrip()
    {
        var storage   = await CreateStorageAsync();
        var recurring = new RecurringJobRecord
        {
            RecurringJobId = $"test-job-{Guid.NewGuid()}",
            JobType        = "MyJob",
            InputType      = "MyInput",
            InputJson      = "{}",
            Cron           = "* * * * *",
            Queue          = "default",
            NextExecution  = DateTimeOffset.UtcNow,
        };

        await storage.UpsertRecurringJobAsync(recurring);
        var all = await storage.GetRecurringJobsAsync();

        all.Should().Contain(r => r.RecurringJobId == recurring.RecurringJobId);
    }

    [Fact]
    public async Task DeleteRecurringJobAsync_removes_the_definition()
    {
        var storage   = await CreateStorageAsync();
        var id        = $"to-delete-{Guid.NewGuid()}";
        var recurring = new RecurringJobRecord
        {
            RecurringJobId = id,
            JobType        = "MyJob",
            InputType      = "MyInput",
            InputJson      = "{}",
            Cron           = "* * * * *",
            Queue          = "default",
        };
        await storage.UpsertRecurringJobAsync(recurring);

        await storage.DeleteRecurringJobAsync(id);
        var all = await storage.GetRecurringJobsAsync();

        all.Should().NotContain(r => r.RecurringJobId == id);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static JobRecord MakeJob(
        string queue           = "default",
        string? idempotencyKey = null) =>
        new()
        {
            Id             = new JobId(Guid.NewGuid()),
            JobType        = "NexJob.IntegrationTests.FakeJob",
            InputType      = "NexJob.IntegrationTests.FakeInput",
            InputJson      = $"{{\"seq\":\"{Guid.NewGuid()}\"}}",
            Queue          = queue,
            Priority       = JobPriority.Normal,
            Status         = JobStatus.Enqueued,
            MaxAttempts    = 5,
            CreatedAt      = DateTimeOffset.UtcNow,
            IdempotencyKey = idempotencyKey,
        };
}
