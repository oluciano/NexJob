using FluentAssertions;
using Xunit;

namespace NexJob.Internal.Tests;

public sealed class JobRetryPolicyTests
{
    [Fact]
    public void ComputeRetryAt_AttemptsRemaining_ReturnsRetryTimestamp()
    {
        // Arrange
        var policy = MakePolicy(_ => TimeSpan.FromMinutes(1));
        var job = MakeJob<PlainRetryJob>(attempts: 1, maxAttempts: 3);

        // Act
        var retryAt = policy.ComputeRetryAt(job, new InvalidOperationException("failure"));

        // Assert
        retryAt.Should().NotBeNull();
    }

    [Fact]
    public void ComputeRetryAt_AttemptsExhausted_ReturnsNull()
    {
        // Arrange
        var policy = MakePolicy(_ => TimeSpan.FromMinutes(1));
        var job = MakeJob<PlainRetryJob>(attempts: 3, maxAttempts: 3);

        // Act
        var retryAt = policy.ComputeRetryAt(job, new InvalidOperationException("failure"));

        // Assert
        retryAt.Should().BeNull();
    }

    [Fact]
    public void ComputeRetryAt_WithRetryAttribute_UsesAttributeDelay()
    {
        // Arrange
        var policy = MakePolicy(_ => throw new InvalidOperationException("options factory should not run"));
        var job = MakeJob<AttributedRetryJob>(attempts: 1, maxAttempts: 10);
        var before = DateTimeOffset.UtcNow;

        // Act
        var retryAt = policy.ComputeRetryAt(job, new InvalidOperationException("failure"));

        // Assert
        retryAt.Should().NotBeNull();
        retryAt!.Value.Should().BeOnOrAfter(before.AddSeconds(9));
        retryAt.Value.Should().BeOnOrBefore(DateTimeOffset.UtcNow.AddSeconds(11));
    }

    [Fact]
    public void ComputeRetryAt_WithoutRetryAttribute_UsesOptionsFactory()
    {
        // Arrange
        var policy = MakePolicy(_ => TimeSpan.FromMinutes(7));
        var job = MakeJob<PlainRetryJob>(attempts: 1, maxAttempts: 3);
        var before = DateTimeOffset.UtcNow;

        // Act
        var retryAt = policy.ComputeRetryAt(job, new InvalidOperationException("failure"));

        // Assert
        retryAt.Should().NotBeNull();
        retryAt!.Value.Should().BeOnOrAfter(before.AddMinutes(7));
        retryAt.Value.Should().BeOnOrBefore(DateTimeOffset.UtcNow.AddMinutes(7).AddSeconds(1));
    }

    [Fact]
    public void ComputeRetryAt_ReturnedTimestamp_IsInTheFuture()
    {
        // Arrange
        var policy = MakePolicy(_ => TimeSpan.FromSeconds(1));
        var job = MakeJob<PlainRetryJob>(attempts: 1, maxAttempts: 3);

        // Act
        var retryAt = policy.ComputeRetryAt(job, new InvalidOperationException("failure"));

        // Assert
        retryAt.Should().NotBeNull();
        retryAt!.Value.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void ComputeRetryAt_Attempt1_UsesAttemptCount()
    {
        // Arrange
        var observedAttempt = 0;
        var policy = MakePolicy(attempt =>
        {
            observedAttempt = attempt;
            return TimeSpan.FromMinutes(1);
        });
        var job = MakeJob<PlainRetryJob>(attempts: 1, maxAttempts: 3);

        // Act
        _ = policy.ComputeRetryAt(job, new InvalidOperationException("failure"));

        // Assert
        observedAttempt.Should().Be(1);
    }

    private static DefaultJobRetryPolicy MakePolicy(Func<int, TimeSpan>? factory = null)
    {
        var options = new NexJobOptions();
        if (factory is not null)
        {
            options.RetryDelayFactory = factory;
        }

        return new DefaultJobRetryPolicy(options);
    }

    private static JobRecord MakeJob<TJob>(int attempts, int maxAttempts)
    {
        return new JobRecord
        {
            Id = JobId.New(),
            JobType = typeof(TJob).AssemblyQualifiedName!,
            InputType = typeof(NoInput).AssemblyQualifiedName!,
            InputJson = "{}",
            Queue = "default",
            Attempts = attempts,
            MaxAttempts = maxAttempts,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = JobStatus.Processing,
        };
    }

    private sealed class PlainRetryJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Retry(2, InitialDelay = "00:00:10")]
    private sealed class AttributedRetryJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

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
