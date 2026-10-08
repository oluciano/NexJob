using FluentAssertions;
using Moq;
using NexJob;
using NexJob.Exceptions;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.Tests;

public sealed class DefaultSchedulerTests
{
    private readonly InMemoryStorageProvider _storage = new();
    private readonly JobWakeUpChannel _wakeUp = new();
    private readonly DefaultScheduler _sut;

    public DefaultSchedulerTests()
    {
        _sut = new DefaultScheduler(_storage, _storage, _storage, new NexJobOptions(), _wakeUp);

        _hardenedSut = new DefaultScheduler(
            _jobStorage.Object,
            _recurringStorage.Object,
            _dashboardStorage.Object,
            _options,
            _hardenedWakeup);
    }

    // ─── EnqueueAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_ReturnsNonEmptyJobId()
    {
        var id = await _sut.EnqueueAsync<StubJob, string>("hello");

        id.Value.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task EnqueueAsync_JobIsImmediatelyFetchable()
    {
        await _sut.EnqueueAsync<StubJob, string>("hello");

        var fetched = await _storage.FetchNextAsync(["default"]);

        fetched.Should().NotBeNull();
        fetched!.Status.Should().Be(JobStatus.Processing);
    }

    [Fact]
    public async Task EnqueueAsync_SerializesInputCorrectly()
    {
        await _sut.EnqueueAsync<StubJob, string>("my-payload");

        var fetched = await _storage.FetchNextAsync(["default"]);

        fetched!.InputJson.Should().Contain("my-payload");
    }

    [Fact]
    public async Task EnqueueAsync_UsesSpecifiedQueue()
    {
        await _sut.EnqueueAsync<StubJob, string>("hi", queue: "critical-queue");

        var notInDefault = await _storage.FetchNextAsync(["default"]);
        notInDefault.Should().BeNull();

        var fetched = await _storage.FetchNextAsync(["critical-queue"]);
        fetched.Should().NotBeNull();
    }

    [Fact]
    public async Task EnqueueAsync_UsesSpecifiedPriority()
    {
        await _sut.EnqueueAsync<StubJob, string>("lo", priority: JobPriority.Low);
        await _sut.EnqueueAsync<StubJob, string>("hi", priority: JobPriority.High);

        var first = await _storage.FetchNextAsync(["default"]);

        first!.Priority.Should().Be(JobPriority.High);
    }

    [Fact]
    public async Task EnqueueAsync_WithIdempotencyKey_ReturnsSameIdForDuplicate()
    {
        var id1 = await _sut.EnqueueAsync<StubJob, string>("v1", idempotencyKey: "order-99");
        var id2 = await _sut.EnqueueAsync<StubJob, string>("v2", idempotencyKey: "order-99");

        id2.Should().Be(id1, "duplicate idempotency key must return the existing job id");
    }

    [Fact]
    public async Task EnqueueAsync_StoresCorrectJobTypeAndInputType()
    {
        await _sut.EnqueueAsync<StubJob, string>("x");

        var fetched = await _storage.FetchNextAsync(["default"]);

        fetched!.JobType.Should().Contain(nameof(StubJob));
        fetched.InputType.Should().Contain(nameof(String));
    }

    // ─── ScheduleAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task ScheduleAsync_ReturnsNonEmptyJobId()
    {
        var id = await _sut.ScheduleAsync<StubJob, string>("hello", TimeSpan.FromMinutes(5));

        id.Value.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task ScheduleAsync_JobIsNotImmediatelyFetchable()
    {
        await _sut.ScheduleAsync<StubJob, string>("hello", TimeSpan.FromMinutes(5));

        var fetched = await _storage.FetchNextAsync(["default"]);

        fetched.Should().BeNull("scheduled job is not due yet");
    }

    [Fact]
    public async Task ScheduleAsync_JobBecomesAvailableWhenDue()
    {
        // Schedule in the past → immediately due
        await _sut.ScheduleAsync<StubJob, string>("hello", TimeSpan.FromMilliseconds(-1));

        var fetched = await _storage.FetchNextAsync(["default"]);

        fetched.Should().NotBeNull("job scheduled in the past should be promoted immediately");
    }

    // ─── ScheduleAtAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ScheduleAtAsync_JobIsNotFetchableBeforeRunAt()
    {
        await _sut.ScheduleAtAsync<StubJob, string>("hello", DateTimeOffset.UtcNow.AddHours(1));

        var fetched = await _storage.FetchNextAsync(["default"]);

        fetched.Should().BeNull();
    }

    [Fact]
    public async Task ScheduleAtAsync_JobIsFetchableAfterRunAt()
    {
        await _sut.ScheduleAtAsync<StubJob, string>("hello", DateTimeOffset.UtcNow.AddMilliseconds(-1));

        var fetched = await _storage.FetchNextAsync(["default"]);

        fetched.Should().NotBeNull();
    }

    // ─── RecurringAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task RecurringAsync_CreatesRecurringJobRecord()
    {
        await _sut.RecurringAsync<StubJob, string>("daily-report", "payload", "0 9 * * *");

        var due = await _storage.GetDueRecurringJobsAsync(DateTimeOffset.UtcNow.AddDays(2));

        due.Should().ContainSingle(r => r.RecurringJobId == "daily-report");
    }

    // ─── ContinueWithAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ContinueWithAsync_JobStaysAwaitingUntilParentCompletes()
    {
        var parentId = await _sut.EnqueueAsync<StubJob, string>("parent");

        await _sut.ContinueWithAsync<StubJob, string>(parentId, "child");

        // Only parent should be fetchable
        var first = await _storage.FetchNextAsync(["default"]);
        first!.Id.Should().Be(parentId, "continuation must wait for parent");

        var second = await _storage.FetchNextAsync(["default"]);
        second.Should().BeNull("continuation is still waiting");

        // Complete parent
        await _storage.AcknowledgeAsync(parentId);
        await _storage.EnqueueContinuationsAsync(parentId);

        var cont = await _storage.FetchNextAsync(["default"]);
        cont.Should().NotBeNull("continuation should now be runnable");
    }

    [Fact]
    public async Task ContinueWithAsync_NoInput_JobStaysAwaitingUntilParentCompletes()
    {
        var parentId = await _sut.EnqueueAsync<StubNoInputJob>();
        await _sut.ContinueWithAsync<StubNoInputJob>(parentId);

        var jobs = await _storage.GetJobsAsync(new JobFilter(), 1, 10);
        var child = jobs.Items.FirstOrDefault(j => j.ParentJobId == parentId);

        child.Should().NotBeNull();
        child!.Status.Should().Be(JobStatus.AwaitingContinuation);
    }

    // ─── RemoveRecurringAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task RemoveRecurringAsync_DeletesTheJobDefinition()
    {
        await _sut.RecurringAsync<StubJob, string>("cleanup", "x", "0 0 * * *");
        await _sut.RemoveRecurringAsync("cleanup");

        var due = await _storage.GetDueRecurringJobsAsync(DateTimeOffset.UtcNow.AddYears(1));

        due.Should().NotContain(r => r.RecurringJobId == "cleanup");
    }

    private readonly Mock<IJobStorage> _jobStorage = new();
    private readonly Mock<IRecurringStorage> _recurringStorage = new();
    private readonly Mock<IDashboardStorage> _dashboardStorage = new();
    private readonly NexJobOptions _options = new();
    private readonly JobWakeUpChannel _hardenedWakeup = new();
    private readonly DefaultScheduler _hardenedSut;

    // ─── EnqueueAsync(JobRecord) Branches ───────────────────────────────────

    /// <summary>Tests that EnqueueAsync throws DuplicateJobException when rejected with an idempotency key.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task EnqueueAsync_RejectedWithIdempotencyKey_ThrowsDuplicateJobException()
    {
        var job = new JobRecord { IdempotencyKey = "k1", JobType = "Job" };
        var existingId = JobId.New();
        _jobStorage.Setup(x => x.EnqueueAsync(job, It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnqueueResult(existingId, WasRejected: true));

        Func<Task> act = () => _hardenedSut.EnqueueAsync(job);

        await act.Should().ThrowAsync<DuplicateJobException>();
    }

    /// <summary>Tests that EnqueueAsync does not throw when rejected without an idempotency key.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task EnqueueAsync_RejectedWithoutIdempotencyKey_ReturnsExistingId()
    {
        var job = new JobRecord { IdempotencyKey = null, JobType = "Job" };
        var existingId = JobId.New();
        _jobStorage.Setup(x => x.EnqueueAsync(job, It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnqueueResult(existingId, WasRejected: true));

        var result = await _hardenedSut.EnqueueAsync(job);

        result.Should().Be(existingId);
    }

    /// <summary>Tests that EnqueueAsync signals wake-up only when NOT rejected.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task EnqueueAsync_WhenNotRejected_SignalsWakeUp()
    {
        var job = new JobRecord { JobType = "Job" };
        _jobStorage.Setup(x => x.EnqueueAsync(job, It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnqueueResult(JobId.New(), WasRejected: false));

        await _hardenedSut.EnqueueAsync(job);

        // Signal capacity is 1, so multiple signals don't block.
        // We verify the side effect: the channel is ready to be read.
        _hardenedWakeup.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None).IsCompleted.Should().BeTrue();
    }

    /// <summary>Tests the string slicing logic when JobType has no namespace.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task EnqueueAsync_JobTypeWithoutNamespace_HandlesSlicingCorrectly()
    {
        var job = new JobRecord { JobType = "SimpleJobName" }; // No dots or commas
        _jobStorage.Setup(x => x.EnqueueAsync(job, It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnqueueResult(JobId.New(), WasRejected: false));

        var result = await _hardenedSut.EnqueueAsync(job);

        result.Should().NotBe(default(JobId));
    }

    // ─── EnqueueAsync Generic Branches ──────────────────────────────────────

    /// <summary>Tests that EnqueueAsync calculates ExpiresAt correctly when deadline is provided.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task EnqueueAsync_WithDeadline_CalculatesExpiresAt()
    {
        _jobStorage.Setup(x => x.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnqueueResult(JobId.New(), WasRejected: false));

        await _hardenedSut.EnqueueAsync<TestJob>(deadlineAfter: TimeSpan.FromMinutes(10));

        _jobStorage.Verify(x => x.EnqueueAsync(
            It.Is<JobRecord>(j => j.ExpiresAt.HasValue && j.ExpiresAt > DateTimeOffset.UtcNow),
            It.IsAny<DuplicatePolicy>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── RecurringAsync Branches ───────────────────────────────────────────

    /// <summary>Tests that RecurringAsync uses UTC as default timezone.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task RecurringAsync_NoTimeZone_UsesUtc()
    {
        await _hardenedSut.RecurringAsync<TestJob>("rec-1", "0 0 * * *");

        _recurringStorage.Verify(x => x.UpsertRecurringJobAsync(
            It.Is<RecurringJobRecord>(r => r.TimeZoneId == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── ParseCron Branches ────────────────────────────────────────────────

    /// <summary>Tests that ParseCron falls back to standard format on cron format error.</summary>
    [Fact]
    public void ParseCron_FallbackToStandardFormat()
    {
        // "0 0 * * *" is standard (5 fields).
        // Our code tries 6 fields (with seconds) first, which will throw CronFormatException.
        var result = DefaultScheduler.ParseCron("0 0 * * *");

        result.Should().NotBeNull();
    }

    /// <summary>Tests that ParseCron supports 6-field format (seconds).</summary>
    [Fact]
    public void ParseCron_SupportsSecondsFormat()
    {
        // "0 0 0 * * *" is 6 fields.
        var result = DefaultScheduler.ParseCron("0 0 0 * * *");

        result.Should().NotBeNull();
    }

    /// <summary>Tests that ParseCron throws when both formats are invalid.</summary>
    [Fact]
    public void ParseCron_InvalidFormat_Throws()
    {
        Action act = () => DefaultScheduler.ParseCron("invalid cron");

        act.Should().Throw<System.Exception>(); // CronExpression.Parse throws various errors
    }

    // ─── support types ───────────────────────────────────────────────────────

    /// <summary>Test job.</summary>
    public sealed class TestJob : IJob
    {
        /// <inheritdoc/>
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
