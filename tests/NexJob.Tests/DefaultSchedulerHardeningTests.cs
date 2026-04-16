using FluentAssertions;
using Moq;
using NexJob.Exceptions;
using NexJob.Storage;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// Hardening unit tests for <see cref="DefaultScheduler"/>.
/// Targets 100% branch coverage for the core scheduling entry point.
/// </summary>
public sealed class DefaultSchedulerHardeningTests
{
    private readonly Mock<IJobStorage> _jobStorage = new();
    private readonly Mock<IRecurringStorage> _recurringStorage = new();
    private readonly Mock<IDashboardStorage> _dashboardStorage = new();
    private readonly NexJobOptions _options = new();
    private readonly JobWakeUpChannel _wakeUp = new();
    private readonly DefaultScheduler _sut;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultSchedulerHardeningTests"/> class.
    /// </summary>
    public DefaultSchedulerHardeningTests()
    {
        _sut = new DefaultScheduler(
            _jobStorage.Object,
            _recurringStorage.Object,
            _dashboardStorage.Object,
            _options,
            _wakeUp);
    }

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

        Func<Task> act = () => _sut.EnqueueAsync(job);

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

        var result = await _sut.EnqueueAsync(job);

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

        await _sut.EnqueueAsync(job);

        // Signal capacity is 1, so multiple signals don't block.
        // We verify the side effect: the channel is ready to be read.
        _wakeUp.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None).IsCompleted.Should().BeTrue();
    }

    /// <summary>Tests the string slicing logic when JobType has no namespace.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task EnqueueAsync_JobTypeWithoutNamespace_HandlesSlicingCorrectly()
    {
        var job = new JobRecord { JobType = "SimpleJobName" }; // No dots or commas
        _jobStorage.Setup(x => x.EnqueueAsync(job, It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnqueueResult(JobId.New(), WasRejected: false));

        var result = await _sut.EnqueueAsync(job);

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

        await _sut.EnqueueAsync<TestJob>(deadlineAfter: TimeSpan.FromMinutes(10));

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
        await _sut.RecurringAsync<TestJob>("rec-1", "0 0 * * *");

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
