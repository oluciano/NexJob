using FluentAssertions;
using NexJob;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests;

public sealed class DefaultSchedulerTests
{
    private readonly InMemoryStorageProvider _storage = new();
    private readonly JobWakeUpChannel _wakeUp = new();
    private readonly DefaultScheduler _sut;

    public DefaultSchedulerTests()
    {
        _sut = new DefaultScheduler(_storage, new NexJobOptions(), _wakeUp);
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

    // ─── RemoveRecurringAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task RemoveRecurringAsync_DeletesTheJobDefinition()
    {
        await _sut.RecurringAsync<StubJob, string>("cleanup", "x", "0 0 * * *");
        await _sut.RemoveRecurringAsync("cleanup");

        var due = await _storage.GetDueRecurringJobsAsync(DateTimeOffset.UtcNow.AddYears(1));

        due.Should().NotContain(r => r.RecurringJobId == "cleanup");
    }
}
