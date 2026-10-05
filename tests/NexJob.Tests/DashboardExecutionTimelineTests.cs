using FluentAssertions;
using NexJob.Dashboard.Pages;
using NexJob.Storage;
using Xunit;

namespace NexJob.Tests;

public sealed class DashboardExecutionTimelineTests
{
    [Fact]
    public void GetEffectiveMaxAttempts_WithRetryAttribute_ReturnsAttributeAttempts()
    {
        // N1 (Positive): Reads [Retry(3)] from the job type
        var job = new JobRecord
        {
            JobType = typeof(FlakyTimelineStubJob).AssemblyQualifiedName!,
            MaxAttempts = 10,
        };

        var effectiveMax = Helpers.GetEffectiveMaxAttempts(job);

        effectiveMax.Should().Be(3);
    }

    [Fact]
    public void GetEffectiveMaxAttempts_WithoutRetryAttribute_ReturnsJobMaxAttempts()
    {
        // N2 (Negative): Job type has no [Retry] attribute -> falls back to job.MaxAttempts
        var job = new JobRecord
        {
            JobType = typeof(RegularTimelineStubJob).AssemblyQualifiedName!,
            MaxAttempts = 7,
        };

        var effectiveMax = Helpers.GetEffectiveMaxAttempts(job);

        effectiveMax.Should().Be(7);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NonExistent.Type.That.Cannot.Be.Found, InvalidAssembly")]
    public void GetEffectiveMaxAttempts_InvalidOrUnresolvableType_ReturnsJobMaxAttempts(string? jobType)
    {
        // N3 (Invalid Input): Null, empty, or unresolvable type strings do not throw and fall back to job.MaxAttempts
        var job = new JobRecord
        {
            JobType = jobType!,
            MaxAttempts = 5,
        };

        var effectiveMax = Helpers.GetEffectiveMaxAttempts(job);

        effectiveMax.Should().Be(5);
    }

    [Fact]
    public void ExecutionTimeline_WhenDeadLettered_RendersExhaustedTitleAndDynamicStepper()
    {
        // N1 (Positive): A dead-lettered job displays the exhausted banner and linear attempt stepper
        var now = DateTimeOffset.UtcNow;
        var job = new JobRecord
        {
            Id = new JobId(Guid.NewGuid()),
            JobType = typeof(FlakyTimelineStubJob).AssemblyQualifiedName!,
            Queue = "default",
            Priority = JobPriority.Normal,
            Status = JobStatus.Failed,
            Attempts = 3,
            MaxAttempts = 10, // Default option is 10, but FlakyTimelineStubJob has [Retry(3)]
            CreatedAt = now.AddMinutes(-5),
            ProcessingStartedAt = now.AddMinutes(-1),
            CompletedAt = now,
            LastErrorMessage = "System.Net.Http.HttpRequestException: Service unavailable (HTTP 503)",
        };

        var html = HtmlFragments.ExecutionTimeline(job, now);

        // Header and banner assertions
        html.Should().Contain("Age: 5m ago");
        html.Should().NotContain("Age: in ");
        html.Should().Contain("🔁 Attempt 3/3");
        html.Should().NotContain("🔁 Attempt 3/10");
        html.Should().Contain("Retry Budget Exhausted — Moved to Dead-Letter");
        html.Should().Contain("retry-loop-banner exhausted");
        html.Should().Contain("💀 Dead-Letter Queue (Exhausted)");

        // Dynamic stepper assertions (all attempts shown, no emoji in brackets)
        html.Should().Contain("Attempt 1: Failed");
        html.Should().Contain("Backoff Delay");
        html.Should().Contain("Attempt 2: Failed");
        html.Should().Contain("Attempt 3: Dead-Letter");
        html.Should().NotContain("Attempt 1 [Failed 💥]");
    }

    [Fact]
    public void ExecutionTimeline_WhenSucceededAfterRetry_RendersRecoveredBanner()
    {
        // N1 (Positive): Job recovered on retry displays "Fault Recovered via Retry Loop"
        var now = DateTimeOffset.UtcNow;
        var job = new JobRecord
        {
            Id = new JobId(Guid.NewGuid()),
            JobType = typeof(FlakyTimelineStubJob).AssemblyQualifiedName!,
            Queue = "default",
            Priority = JobPriority.Normal,
            Status = JobStatus.Succeeded,
            Attempts = 2,
            MaxAttempts = 10,
            CreatedAt = now.AddMinutes(-2),
            ProcessingStartedAt = now.AddSeconds(-30),
            CompletedAt = now,
        };

        var html = HtmlFragments.ExecutionTimeline(job, now);

        html.Should().Contain("Fault Recovered via Retry Loop");
        html.Should().Contain("retry-loop-banner recovered");
        html.Should().Contain("🎉 Recovered on Attempt 2");
        html.Should().Contain("Attempt 1: Failed");
        html.Should().Contain("Backoff Delay");
        html.Should().Contain("Attempt 2: Succeeded");
    }

    [Fact]
    public void ExecutionTimeline_WhenAwaitingRetry_RendersNextRetryCountdown()
    {
        // N1 (Positive): Job in backoff awaiting next retry
        var now = DateTimeOffset.UtcNow;
        var job = new JobRecord
        {
            Id = new JobId(Guid.NewGuid()),
            JobType = typeof(FlakyTimelineStubJob).AssemblyQualifiedName!,
            Queue = "default",
            Priority = JobPriority.Normal,
            Status = JobStatus.Enqueued,
            Attempts = 1,
            MaxAttempts = 10,
            CreatedAt = now.AddMinutes(-1),
            RetryAt = now.AddSeconds(30),
        };

        var html = HtmlFragments.ExecutionTimeline(job, now);

        html.Should().Contain("Retry Loop Active &amp; Backoff in Progress");
        html.Should().Contain("Next Retry in");
        html.Should().Contain("Attempt 1: Failed");
        html.Should().Contain("Backoff Delay");
        html.Should().Contain("Attempt 2: Scheduled");
    }
}

[Retry(3)]
public sealed class FlakyTimelineStubJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class RegularTimelineStubJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
