using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Exceptions that must not be retried (#353): a match goes straight to <c>Failed</c> and the dead-letter handler runs,
/// without waiting for the remaining attempts.
/// </summary>
public sealed class IgnoreRetryAttemptExceptionsTests
{
    private readonly NexJobOptions _options = new();

    // ─── N1: matching exceptions are not retried ────────────────────────────

    /// <summary>N1: an exception listed on the attribute is not retried although attempts remain.</summary>
    [Fact]
    public void ComputeRetryAt_WhenExceptionIsListedOnTheAttribute_ReturnsNull()
    {
        var job = RecordFor<IgnoringJob>();

        Policy().ComputeRetryAt(job, new DomainRuleException()).Should().BeNull();
    }

    /// <summary>N1: a type derived from a listed type matches too.</summary>
    [Fact]
    public void ComputeRetryAt_WhenExceptionDerivesFromAListedType_ReturnsNull()
    {
        var job = RecordFor<IgnoringJob>();

        Policy().ComputeRetryAt(job, new DerivedRuleException()).Should().BeNull();
    }

    /// <summary>N1: the global list applies to jobs without an attribute.</summary>
    [Fact]
    public void ComputeRetryAt_WhenExceptionIsListedGlobally_ReturnsNull()
    {
        _options.IgnoreRetryAttemptExceptions = [typeof(InvalidOperationException)];

        Policy().ComputeRetryAt(RecordFor<NoAttributeJob>(), new ObjectDisposedException(null)).Should().BeNull();
    }

    /// <summary>N1: the global and the per-job lists are combined.</summary>
    [Fact]
    public void ComputeRetryAt_UsesTheUnionOfGlobalAndAttributeLists()
    {
        _options.IgnoreRetryAttemptExceptions = [typeof(InvalidOperationException)];
        var policy = Policy();
        var job = RecordFor<IgnoringJob>();

        policy.ComputeRetryAt(job, new InvalidOperationException()).Should().BeNull();
        policy.ComputeRetryAt(job, new DomainRuleException()).Should().BeNull();
    }

    /// <summary>N1: the execution timeout (#354) can be listed to dead-letter without retrying.</summary>
    [Fact]
    public void ComputeRetryAt_WhenTimeoutExceptionIsListed_ReturnsNull()
    {
        _options.IgnoreRetryAttemptExceptions = [typeof(TimeoutException)];

        Policy().ComputeRetryAt(RecordFor<NoAttributeJob>(), new TimeoutException()).Should().BeNull();
    }

    /// <summary>N1: exceptions wrapped by reflection or by a single-element aggregate are unwrapped.</summary>
    [Fact]
    public void ComputeRetryAt_WhenListedExceptionIsWrapped_ReturnsNull()
    {
        var policy = Policy();
        var job = RecordFor<IgnoringJob>();

        policy.ComputeRetryAt(job, new TargetInvocationException(new DomainRuleException())).Should().BeNull();
        policy.ComputeRetryAt(job, new AggregateException(new DomainRuleException())).Should().BeNull();
        policy.ComputeRetryAt(job, new AggregateException(new TargetInvocationException(new DomainRuleException()))).Should().BeNull();
    }

    /// <summary>N1: the retry bypass is logged, so an operator can tell it from an exhausted budget.</summary>
    [Fact]
    public void ComputeRetryAt_WhenRetryIsBypassed_LogsIt()
    {
        using var sink = new LevelLogSink();
        using var factory = LoggerFactory.Create(b => b.AddProvider(sink));
        var policy = new DefaultJobRetryPolicy(_options, factory.CreateLogger<DefaultJobRetryPolicy>());

        policy.ComputeRetryAt(RecordFor<IgnoringJob>(), new DomainRuleException());

        sink.Entries.Should().Contain(e => e.Message.Contains(nameof(DomainRuleException), StringComparison.Ordinal)
            && e.Message.Contains("not be retried", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>N1: end to end, the executor dead-letters at once and tells the dead-letter dispatcher.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenListedExceptionIsThrown_FailsAtOnceAndDeadLetters()
    {
        var storage = new Mock<IJobStorage>();
        var invoker = new Mock<IJobInvokerFactory>();
        var deadLetter = new Mock<IDeadLetterDispatcher>();
        var job = RecordFor<IgnoringJob>();
        var failure = new DomainRuleException();
        invoker.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        var sut = new JobExecutor(
            storage.Object,
            invoker.Object,
            Policy(),
            deadLetter.Object,
            new ThrottleRegistry(),
            _options,
            Enumerable.Empty<IJobExecutionFilter>(),
            NullLogger<JobExecutor>.Instance);

        await sut.ExecuteJobAsync(job);

        storage.Verify(x => x.CommitJobResultAsync(
            job.Id,
            It.Is<JobExecutionResult>(r => !r.Succeeded && r.RetryAt == null && !r.RefundAttempt),
            It.IsAny<CancellationToken>()), Times.Once);
        deadLetter.Verify(x => x.DispatchAsync(job, failure, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── N2: everything else is retried as before ───────────────────────────

    /// <summary>N2: an exception that is not listed is retried.</summary>
    [Fact]
    public void ComputeRetryAt_WhenExceptionIsNotListed_Retries()
    {
        Policy().ComputeRetryAt(RecordFor<IgnoringJob>(), new HttpRequestException()).Should().NotBeNull();
    }

    /// <summary>N2: a listed type deeper in a multi-exception aggregate is not enough; only a single wrapped cause is unwrapped.</summary>
    [Fact]
    public void ComputeRetryAt_WhenAggregateHoldsSeveralExceptions_IsNotUnwrapped()
    {
        var failure = new AggregateException(new DomainRuleException(), new HttpRequestException());

        Policy().ComputeRetryAt(RecordFor<IgnoringJob>(), failure).Should().NotBeNull();
    }

    /// <summary>N2: a base type of a listed type does not match; matching goes down the hierarchy only.</summary>
    [Fact]
    public void ComputeRetryAt_WhenExceptionIsABaseOfAListedType_Retries()
    {
        _options.IgnoreRetryAttemptExceptions = [typeof(DerivedRuleException)];

        Policy().ComputeRetryAt(RecordFor<NoAttributeJob>(), new DomainRuleException()).Should().NotBeNull();
    }

    // ─── N3: invalid and empty input ────────────────────────────────────────

    /// <summary>N3: null and empty lists mean "retry everything".</summary>
    [Fact]
    public void ComputeRetryAt_WithNullOrEmptyLists_RetriesEverything()
    {
        var policy = Policy();

        policy.ComputeRetryAt(RecordFor<NullListJob>(), new DomainRuleException()).Should().NotBeNull();
        policy.ComputeRetryAt(RecordFor<EmptyListJob>(), new DomainRuleException()).Should().NotBeNull();
        policy.ComputeRetryAt(RecordFor<NoAttributeJob>(), new DomainRuleException()).Should().NotBeNull();
    }

    /// <summary>N3: a type that is not an exception is rejected on the attribute.</summary>
    [Fact]
    public void RetryAttribute_WithNonExceptionType_Throws()
    {
        var act = () => new RetryAttribute(3) { IgnoreRetryAttemptExceptions = [typeof(string)] };

        act.Should().Throw<ArgumentException>().WithMessage("*Exception*");
    }

    /// <summary>N3: a null entry is rejected on the attribute.</summary>
    [Fact]
    public void RetryAttribute_WithNullEntry_Throws()
    {
        var act = () => new RetryAttribute(3) { IgnoreRetryAttemptExceptions = [null!] };

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>N3: the global list rejects non-exception types, null entries and a null list.</summary>
    [Fact]
    public void Options_RejectInvalidIgnoreLists()
    {
        var options = new NexJobOptions();

        ((Action)(() => options.IgnoreRetryAttemptExceptions = [typeof(int)])).Should().Throw<ArgumentException>();
        ((Action)(() => options.IgnoreRetryAttemptExceptions = [null!])).Should().Throw<ArgumentException>();
        ((Action)(() => options.IgnoreRetryAttemptExceptions = null!)).Should().Throw<ArgumentNullException>();
        options.IgnoreRetryAttemptExceptions.Should().BeEmpty();
    }

    private DefaultJobRetryPolicy Policy() => new(_options);

    private static JobRecord RecordFor<TJob>() =>
        new()
        {
            Id = JobId.New(),
            JobType = typeof(TJob).AssemblyQualifiedName!,
            Attempts = 1,
        };
}

/// <summary>A domain rule violation used by the tests.</summary>
public class DomainRuleException : Exception
{
}

/// <summary>A more specific domain rule violation used by the tests.</summary>
public sealed class DerivedRuleException : DomainRuleException
{
}

[Retry(5, IgnoreRetryAttemptExceptions = [typeof(DomainRuleException)])]
internal sealed class IgnoringJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

[Retry(5, IgnoreRetryAttemptExceptions = null)]
internal sealed class NullListJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

[Retry(5, IgnoreRetryAttemptExceptions = [])]
internal sealed class EmptyListJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class NoAttributeJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
