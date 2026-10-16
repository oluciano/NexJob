using FluentAssertions;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// The duplicate-handling rules shared by every storage provider: which statuses are active, and whether an enqueue whose
/// idempotency key matches an existing job is rejected.
/// </summary>
public sealed class DuplicateRulesTests
{
    // ─── N1: positive ──────────────────────────────────────────────────────

    /// <summary>N1: only the statuses where the job is still going to run are active.</summary>
    /// <param name="status">The status.</param>
    /// <param name="expected">Whether it is active.</param>
    [Theory]
    [InlineData(JobStatus.Enqueued, true)]
    [InlineData(JobStatus.Processing, true)]
    [InlineData(JobStatus.Scheduled, true)]
    [InlineData(JobStatus.AwaitingContinuation, true)]
    [InlineData(JobStatus.Succeeded, false)]
    [InlineData(JobStatus.Failed, false)]
    [InlineData(JobStatus.Expired, false)]
    public void IsActive_IdentifiesTheActiveStatuses(JobStatus status, bool expected)
        => DuplicateRules.IsActive(status).Should().Be(expected);

    /// <summary>N1: a duplicate of a job that is still active is never rejected and resolves to the existing id, whatever the policy.</summary>
    /// <param name="status">An active status.</param>
    /// <param name="policy">The policy.</param>
    [Theory]
    [InlineData(JobStatus.Enqueued, DuplicatePolicy.AllowAfterFailed)]
    [InlineData(JobStatus.Processing, DuplicatePolicy.RejectIfFailed)]
    [InlineData(JobStatus.Scheduled, DuplicatePolicy.RejectAlways)]
    [InlineData(JobStatus.AwaitingContinuation, DuplicatePolicy.RejectAlways)]
    public void Resolve_WhenTheExistingJobIsActive_ReturnsItNotRejected(JobStatus status, DuplicatePolicy policy)
    {
        var id = JobId.New();

        var result = DuplicateRules.Resolve(id, status, policy);

        result.JobId.Should().Be(id);
        result.WasRejected.Should().BeFalse();
    }

    // ─── N2: negative ──────────────────────────────────────────────────────

    /// <summary>N2: once the existing job is finished, the policy decides whether the new enqueue is rejected.</summary>
    /// <param name="status">A finished status.</param>
    /// <param name="policy">The policy.</param>
    /// <param name="rejected">Whether the enqueue is rejected.</param>
    [Theory]
    [InlineData(JobStatus.Failed, DuplicatePolicy.AllowAfterFailed, false)]
    [InlineData(JobStatus.Failed, DuplicatePolicy.RejectIfFailed, true)]
    [InlineData(JobStatus.Failed, DuplicatePolicy.RejectAlways, true)]
    [InlineData(JobStatus.Succeeded, DuplicatePolicy.AllowAfterFailed, false)]
    [InlineData(JobStatus.Succeeded, DuplicatePolicy.RejectIfFailed, false)]
    [InlineData(JobStatus.Succeeded, DuplicatePolicy.RejectAlways, true)]
    [InlineData(JobStatus.Expired, DuplicatePolicy.AllowAfterFailed, false)]
    [InlineData(JobStatus.Expired, DuplicatePolicy.RejectIfFailed, false)]
    [InlineData(JobStatus.Expired, DuplicatePolicy.RejectAlways, true)]
    public void Resolve_WhenTheExistingJobIsFinished_AppliesThePolicy(JobStatus status, DuplicatePolicy policy, bool rejected)
    {
        var id = JobId.New();

        var result = DuplicateRules.Resolve(id, status, policy);

        result.JobId.Should().Be(id);
        result.WasRejected.Should().Be(rejected);
    }

    // ─── N3: invalid / boundary ────────────────────────────────────────────

    /// <summary>N3: an undefined policy never rejects a failed job, and an undefined status is not active and is rejected only by <c>RejectAlways</c>.</summary>
    [Fact]
    public void Resolve_WithUndefinedValues_FallsBackToTheNarrowestRejection()
    {
        var id = JobId.New();

        DuplicateRules.IsActive((JobStatus)99).Should().BeFalse();
        DuplicateRules.Resolve(id, JobStatus.Failed, (DuplicatePolicy)99).WasRejected.Should().BeFalse();
        DuplicateRules.Resolve(id, (JobStatus)99, DuplicatePolicy.RejectIfFailed).WasRejected.Should().BeFalse();
        DuplicateRules.Resolve(id, (JobStatus)99, DuplicatePolicy.RejectAlways).WasRejected.Should().BeTrue();
    }
}
