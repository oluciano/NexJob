using FluentAssertions;
using NexJob.Dashboard.Pages;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Per-job attempt limit (#352): call site wins over <see cref="RetryAttribute"/>, which wins over
/// <see cref="NexJobOptions.MaxAttempts"/>.
/// </summary>
public sealed class MaxAttemptsOverrideTests
{
    private readonly InMemoryStorageProvider _storage = new();
    private readonly NexJobOptions _options = new();
    private readonly DefaultScheduler _sut;

    /// <summary>Initializes a new instance of the <see cref="MaxAttemptsOverrideTests"/> class.</summary>
    public MaxAttemptsOverrideTests()
    {
        _sut = new DefaultScheduler(_storage, _storage, _storage, _options, new JobWakeUpChannel());
    }

    // ─── N1: the explicit value is stored, whatever the class says ──────────

    /// <summary>N1: enqueue of a no-input job stores the explicit limit.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task EnqueueAsync_NoInput_WithMaxAttempts_StoresExplicitValue()
    {
        var id = await _sut.EnqueueAsync<RetryEightJob>(maxAttempts: 2);

        (await StoredMaxAttemptsAsync(id)).Should().Be(2);
    }

    /// <summary>N1: enqueue of a job with input stores the explicit limit.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task EnqueueAsync_WithInput_WithMaxAttempts_StoresExplicitValue()
    {
        var id = await _sut.EnqueueAsync<RetryEightInputJob, string>("x", maxAttempts: 2);

        (await StoredMaxAttemptsAsync(id)).Should().Be(2);
    }

    /// <summary>N1: delayed schedule of a no-input job stores the explicit limit.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ScheduleAsync_NoInput_WithMaxAttempts_StoresExplicitValue()
    {
        var id = await _sut.ScheduleAsync<RetryEightJob>(TimeSpan.FromMinutes(5), maxAttempts: 2);

        (await StoredMaxAttemptsAsync(id)).Should().Be(2);
    }

    /// <summary>N1: delayed schedule of a job with input stores the explicit limit.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ScheduleAsync_WithInput_WithMaxAttempts_StoresExplicitValue()
    {
        var id = await _sut.ScheduleAsync<RetryEightInputJob, string>("x", TimeSpan.FromMinutes(5), maxAttempts: 2);

        (await StoredMaxAttemptsAsync(id)).Should().Be(2);
    }

    /// <summary>N1 (boundary): <c>1</c> is the smallest valid limit.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task EnqueueAsync_WithMaxAttemptsOne_StoresOne()
    {
        var id = await _sut.EnqueueAsync<RetryEightJob>(maxAttempts: 1);

        (await StoredMaxAttemptsAsync(id)).Should().Be(1);
    }

    /// <summary>N1: without an explicit value the class attribute is stored, so the record carries the real limit.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task EnqueueAsync_WithoutMaxAttempts_StoresTheRetryAttributeValue()
    {
        var id = await _sut.EnqueueAsync<RetryEightJob>();

        (await StoredMaxAttemptsAsync(id)).Should().Be(8);
    }

    /// <summary>N3 (guard): without an explicit value and without an attribute the global option is stored.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task EnqueueAsync_WithoutMaxAttemptsAndWithoutAttribute_StoresGlobalOption()
    {
        _options.MaxAttempts = 7;

        var id = await _sut.EnqueueAsync<PlainJob>();

        (await StoredMaxAttemptsAsync(id)).Should().Be(7);
    }

    // ─── N3: invalid input is rejected before touching storage ──────────────

    /// <summary>N3: a limit below one is rejected by every overload.</summary>
    /// <param name="value">The invalid limit.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task Overloads_WithMaxAttemptsBelowOne_Throw(int value)
    {
        var calls = new Func<Task>[]
        {
            () => _sut.EnqueueAsync<RetryEightJob>(maxAttempts: value),
            () => _sut.EnqueueAsync<RetryEightInputJob, string>("x", maxAttempts: value),
            () => _sut.ScheduleAsync<RetryEightJob>(TimeSpan.FromMinutes(1), maxAttempts: value),
            () => _sut.ScheduleAsync<RetryEightInputJob, string>("x", TimeSpan.FromMinutes(1), maxAttempts: value),
        };

        foreach (var call in calls)
        {
            await call.Should().ThrowAsync<ArgumentOutOfRangeException>();
        }

        (await _storage.FetchNextAsync(["default"])).Should().BeNull("nothing may be stored for a rejected call");
    }

    // ─── Retry policy: the stored limit decides ─────────────────────────────

    /// <summary>N1: a stored limit lower than the class attribute dead-letters at the stored limit.</summary>
    [Fact]
    public void ComputeRetryAt_WhenStoredLimitIsReached_ReturnsNullEvenIfAttributeAllowsMore()
    {
        var job = RecordFor<RetryEightJob>(maxAttempts: 2, attempts: 2);

        new DefaultJobRetryPolicy(_options).ComputeRetryAt(job, new InvalidOperationException()).Should().BeNull();
    }

    /// <summary>N2: <c>maxAttempts: 1</c> dead-letters on the very first failure.</summary>
    [Fact]
    public void ComputeRetryAt_WithLimitOne_DeadLettersOnFirstFailure()
    {
        var job = RecordFor<RetryEightJob>(maxAttempts: 1, attempts: 1);

        new DefaultJobRetryPolicy(_options).ComputeRetryAt(job, new InvalidOperationException()).Should().BeNull();
    }

    /// <summary>N1: below the stored limit the job is retried.</summary>
    [Fact]
    public void ComputeRetryAt_WhenStoredLimitNotReached_Retries()
    {
        var job = RecordFor<RetryEightJob>(maxAttempts: 3, attempts: 2);

        new DefaultJobRetryPolicy(_options).ComputeRetryAt(job, new InvalidOperationException()).Should().NotBeNull();
    }

    /// <summary>
    /// N3 (compatibility guard): a row stored with the global default (legacy, recurring, dashboard or trigger created)
    /// keeps honouring the class attribute. Also pins the documented limit: an explicit value equal to the global
    /// default cannot be told apart from it, so the attribute applies.
    /// </summary>
    [Fact]
    public void ComputeRetryAt_WhenStoredValueEqualsGlobalDefault_HonoursTheAttribute()
    {
        var policy = new DefaultJobRetryPolicy(_options);

        policy.ComputeRetryAt(RecordFor<RetryThreeJob>(maxAttempts: 10, attempts: 3), new InvalidOperationException()).Should().BeNull();
        policy.ComputeRetryAt(RecordFor<RetryThreeJob>(maxAttempts: 10, attempts: 2), new InvalidOperationException()).Should().NotBeNull();
    }

    /// <summary>N3 (compatibility guard): the rule follows a customised global default, not the literal 10.</summary>
    [Fact]
    public void ComputeRetryAt_WhenStoredValueEqualsCustomGlobalDefault_HonoursTheAttribute()
    {
        _options.MaxAttempts = 5;
        var policy = new DefaultJobRetryPolicy(_options);

        policy.ComputeRetryAt(RecordFor<RetryThreeJob>(maxAttempts: 5, attempts: 3), new InvalidOperationException()).Should().BeNull();
    }

    /// <summary>N3 (guard): without an attribute the stored limit is the limit.</summary>
    [Fact]
    public void ComputeRetryAt_WithoutAttribute_UsesStoredLimit()
    {
        var policy = new DefaultJobRetryPolicy(_options);

        policy.ComputeRetryAt(RecordFor<PlainJob>(maxAttempts: 2, attempts: 2), new InvalidOperationException()).Should().BeNull();
        policy.ComputeRetryAt(RecordFor<PlainJob>(maxAttempts: 2, attempts: 1), new InvalidOperationException()).Should().NotBeNull();
    }

    // ─── Dashboard shows the same limit the policy applies ──────────────────

    /// <summary>N1: the job detail budget shows the stored per-job limit, not the class attribute.</summary>
    [Fact]
    public void GetEffectiveMaxAttempts_WithStoredOverride_ReturnsStoredValue()
    {
        var job = RecordFor<RetryEightJob>(maxAttempts: 2, attempts: 1);

        Helpers.GetEffectiveMaxAttempts(job).Should().Be(2);
    }

    private static JobRecord RecordFor<TJob>(int maxAttempts, int attempts) =>
        new()
        {
            Id = JobId.New(),
            JobType = typeof(TJob).AssemblyQualifiedName!,
            MaxAttempts = maxAttempts,
            Attempts = attempts,
        };

    private async Task<int> StoredMaxAttemptsAsync(JobId id)
    {
        var record = await _storage.GetJobByIdAsync(id);
        record.Should().NotBeNull();
        return record!.MaxAttempts;
    }
}

[Retry(8)]
internal sealed class RetryEightJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

[Retry(8)]
internal sealed class RetryEightInputJob : IJob<string>
{
    public Task ExecuteAsync(string input, CancellationToken cancellationToken) => Task.CompletedTask;
}

[Retry(3)]
internal sealed class RetryThreeJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class PlainJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
