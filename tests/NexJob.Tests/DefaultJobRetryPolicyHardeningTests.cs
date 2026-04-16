using FluentAssertions;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// Hardening unit tests for <see cref="DefaultJobRetryPolicy"/>.
/// Targets 100% branch coverage for retry delay calculation logic.
/// </summary>
public sealed class DefaultJobRetryPolicyHardeningTests
{
    private readonly NexJobOptions _options = new()
    {
        MaxAttempts = 3,
        RetryDelayFactory = attempt => TimeSpan.FromSeconds(attempt * 10),
    };

    /// <summary>Tests that retry delay uses global options when no attribute is present.</summary>
    [Fact]
    public void ComputeRetryAt_NoAttribute_UsesOptionsFactory()
    {
        var sut = new DefaultJobRetryPolicy(_options);
        var job = new JobRecord { Attempts = 1, MaxAttempts = 3, JobType = typeof(SimpleJob).AssemblyQualifiedName ?? string.Empty };

        var result = sut.ComputeRetryAt(job, new Exception());

        result.Should().NotBeNull();
        // 1st attempt = 10s delay. We account for jitter ±10% (9s to 11s)
        result!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow.AddSeconds(10), TimeSpan.FromSeconds(2));
    }

    /// <summary>Tests that retry delay uses attribute settings when present.</summary>
    [Fact]
    public void ComputeRetryAt_WithAttribute_UsesAttributeSettings()
    {
        var sut = new DefaultJobRetryPolicy(_options);
        var job = new JobRecord { Attempts = 1, MaxAttempts = 5, JobType = typeof(RetryJob).AssemblyQualifiedName ?? string.Empty };

        var result = sut.ComputeRetryAt(job, new Exception());

        result.Should().NotBeNull();
        // Attribute has InitialDelay=5s. Jitter ±10% (4.5s to 5.5s)
        result!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow.AddSeconds(5), TimeSpan.FromSeconds(1));
    }

    /// <summary>Tests that retry returns null when max attempts are reached.</summary>
    [Fact]
    public void ComputeRetryAt_MaxAttemptsReached_ReturnsNull()
    {
        var sut = new DefaultJobRetryPolicy(_options);
        var job = new JobRecord { Attempts = 3, MaxAttempts = 3, JobType = typeof(SimpleJob).AssemblyQualifiedName ?? string.Empty };

        var result = sut.ComputeRetryAt(job, new Exception());

        result.Should().BeNull();
    }

    /// <summary>Tests that invalid job types default to global options without crashing.</summary>
    [Fact]
    public void ComputeRetryAt_InvalidJobType_UsesOptionsFactory()
    {
        var sut = new DefaultJobRetryPolicy(_options);
        var job = new JobRecord { Attempts = 1, MaxAttempts = 3, JobType = "InvalidType" };

        var result = sut.ComputeRetryAt(job, new Exception());

        result.Should().NotBeNull();
        result!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow.AddSeconds(10), TimeSpan.FromSeconds(2));
    }

    /// <summary>Simple job.</summary>
    public sealed class SimpleJob : IJob
    {
        /// <inheritdoc/>
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Retry job.</summary>
    [Retry(5, InitialDelay = "00:00:05")]
    public sealed class RetryJob : IJob
    {
        /// <inheritdoc/>
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
