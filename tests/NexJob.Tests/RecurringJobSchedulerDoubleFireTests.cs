using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NexJob.Configuration;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Tests for issue #260: a due recurring job fires once even when several instances read the due list
/// before either advanced it, and an invalid time zone never causes a job to be enqueued on every poll.
/// </summary>
public sealed class RecurringJobSchedulerDoubleFireTests
{
    private static readonly MethodInfo EnqueueDueJobs = typeof(RecurringJobSchedulerService)
        .GetMethod("EnqueueDueJobsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    // ─── N1: two instances, same due job ─────────────────────────────────────

    /// <summary>N1 (Positive): two schedulers holding the same stale due list enqueue the occurrence once.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task TwoSchedulers_SameDueJob_EnqueuedOnce()
    {
        var storage = new InMemoryStorageProvider();
        await storage.UpsertRecurringJobAsync(NewRecurring("r1", nextExecution: DateTimeOffset.UtcNow.AddSeconds(-1)));
        var staleDueList = Snapshot(await storage.GetDueRecurringJobsAsync(DateTimeOffset.UtcNow));
        var schedulerA = CreateScheduler(storage, staleDueList, new ErrorLog());
        var schedulerB = CreateScheduler(storage, staleDueList, new ErrorLog());

        await RunAsync(schedulerA);

        // The first run finishes before the second instance acts, which releases the SkipIfRunning idempotency key.
        var fired = (await storage.FetchNextAsync(["default"]))!;
        await storage.CommitJobResultAsync(fired.Id, new JobExecutionResult { Succeeded = true, Logs = [] });
        await RunAsync(schedulerB);

        (await storage.GetMetricsAsync()).Enqueued.Should().Be(0, "the second instance must notice the first already fired this occurrence");
        (await storage.GetMetricsAsync()).Succeeded.Should().Be(1);
    }

    /// <summary>N3 (Boundary): with AllowConcurrent there is still exactly one firing per occurrence across instances.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task AllowConcurrent_StillSingleFirePerOccurrence()
    {
        var storage = new InMemoryStorageProvider();
        await storage.UpsertRecurringJobAsync(NewRecurring("r1", DateTimeOffset.UtcNow.AddSeconds(-1), RecurringConcurrencyPolicy.AllowConcurrent));
        var staleDueList = Snapshot(await storage.GetDueRecurringJobsAsync(DateTimeOffset.UtcNow));
        var schedulerA = CreateScheduler(storage, staleDueList, new ErrorLog());
        var schedulerB = CreateScheduler(storage, staleDueList, new ErrorLog());

        await RunAsync(schedulerA);
        await RunAsync(schedulerB);

        (await storage.GetMetricsAsync()).Enqueued.Should().Be(1);
    }

    // ─── N2: invalid time zone ───────────────────────────────────────────────

    /// <summary>N2 (Negative): an unresolvable time zone enqueues nothing, logs an error and leaves the schedule untouched.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task InvalidTimeZone_NothingEnqueued_ErrorLogged()
    {
        var storage = new InMemoryStorageProvider();
        var due = DateTimeOffset.UtcNow.AddSeconds(-1);
        await storage.UpsertRecurringJobAsync(NewRecurring("bad-tz", due, timeZoneId: "Not/AZone"));
        var log = new ErrorLog();
        var scheduler = CreateScheduler(storage, Snapshot(await storage.GetDueRecurringJobsAsync(DateTimeOffset.UtcNow)), log);

        await RunAsync(scheduler);
        await RunAsync(scheduler);

        (await storage.GetMetricsAsync()).Enqueued.Should().Be(0, "a job whose next run cannot be computed must not be enqueued every poll");
        log.Errors.Should().NotBeEmpty();
    }

    /// <summary>N3 (Boundary): a job due exactly now fires once and its next execution moves into the future.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task DueExactlyNow_FiresOnce()
    {
        var storage = new InMemoryStorageProvider();
        await storage.UpsertRecurringJobAsync(NewRecurring("r1", DateTimeOffset.UtcNow));
        var scheduler = CreateScheduler(storage, null, new ErrorLog());

        await RunAsync(scheduler);
        await RunAsync(scheduler);

        (await storage.GetMetricsAsync()).Enqueued.Should().Be(1);
        (await storage.GetRecurringJobByIdAsync("r1"))!.NextExecution.Should().BeAfter(DateTimeOffset.UtcNow.AddSeconds(-1));
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static RecurringJobRecord NewRecurring(
        string id,
        DateTimeOffset nextExecution,
        RecurringConcurrencyPolicy policy = RecurringConcurrencyPolicy.SkipIfRunning,
        string? timeZoneId = null) => new()
        {
            RecurringJobId = id,
            JobType = "TestJob",
            InputType = "TestInput",
            InputJson = "{}",
            Cron = "0 3 * * *",
            TimeZoneId = timeZoneId,
            Queue = "default",
            NextExecution = nextExecution,
            CreatedAt = DateTimeOffset.UtcNow,
            ConcurrencyPolicy = policy,
        };

    // Providers backed by a database return fresh copies, so an instance can hold a due list that is already out of date.
    private static List<RecurringJobRecord> Snapshot(IReadOnlyList<RecurringJobRecord> due) =>
        due.Select(r => NewRecurring(r.RecurringJobId, r.NextExecution!.Value, r.ConcurrencyPolicy, r.TimeZoneId)).ToList();

    private static RecurringJobSchedulerService CreateScheduler(InMemoryStorageProvider storage, List<RecurringJobRecord>? dueList, ErrorLog log)
    {
        IRecurringStorage recurring = storage;
        if (dueList is not null)
        {
            var mock = new Mock<IRecurringStorage>();
            mock.Setup(x => x.GetDueRecurringJobsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dueList);
            mock.Setup(x => x.TryAcquireRecurringJobLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .Returns((string id, TimeSpan ttl, CancellationToken ct) => storage.TryAcquireRecurringJobLockAsync(id, ttl, ct));
            mock.Setup(x => x.ReleaseRecurringJobLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string id, CancellationToken ct) => storage.ReleaseRecurringJobLockAsync(id, ct));
            mock.Setup(x => x.GetRecurringJobByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string id, CancellationToken ct) => storage.GetRecurringJobByIdAsync(id, ct));
            mock.Setup(x => x.SetRecurringJobNextExecutionAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .Returns((string id, DateTimeOffset next, CancellationToken ct) => storage.SetRecurringJobNextExecutionAsync(id, next, ct));
            recurring = mock.Object;
        }

        return new RecurringJobSchedulerService(
            storage,
            recurring,
            new Mock<IRuntimeSettingsStore>().Object,
            new NexJobOptions { PollingInterval = TimeSpan.FromSeconds(1) },
            log);
    }

    private static Task RunAsync(RecurringJobSchedulerService scheduler) =>
        (Task)EnqueueDueJobs.Invoke(scheduler, [CancellationToken.None])!;

    private sealed class ErrorLog : ILogger<RecurringJobSchedulerService>
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                Errors.Add(formatter(state, exception));
            }
        }
    }
}
