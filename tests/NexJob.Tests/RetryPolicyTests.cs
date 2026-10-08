using FluentAssertions;
using NexJob;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Tests for the default <see cref="NexJobOptions.RetryDelayFactory"/> formula:
/// <c>pow(attempt, 4) + 15 + Random.Next(30) × (attempt + 1)</c> seconds.
/// </summary>
public sealed class RetryPolicyTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a <see cref="NexJobOptions"/> whose <see cref="NexJobOptions.RetryDelayFactory"/>
    /// uses the default (unmodified) formula so we always exercise the real implementation.
    /// </summary>
    private static NexJobOptions DefaultOptions() => new();

    // ─── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void RetryDelay_Attempt1_IsWithinExpectedRange()
    {
        // pow(1, 4) = 1 ; rand term ∈ [0, 29] × 2 ∈ [0, 58]
        // total ∈ [1 + 15 + 0, 1 + 15 + 58] = [16, 74] seconds
        var factory = DefaultOptions().RetryDelayFactory;

        var delay = factory(1);

        delay.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(16),
            "minimum delay at attempt 1 is pow(1,4)+15 = 16 s");
        delay.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(74),
            "maximum delay at attempt 1 is pow(1,4)+15+29×2 = 74 s");
    }

    [Fact]
    public void RetryDelay_IsPositive_ForAllAttempts()
    {
        var factory = DefaultOptions().RetryDelayFactory;

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            var delay = factory(attempt);
            delay.Should().BePositive(
                $"retry delay must always be positive (attempt {attempt})");
        }
    }

    [Fact]
    public void RetryDelay_MinimumValue_IncreasesWithAttempts()
    {
        // Behavior changed in v5.10: this test used to assert on a lambda defined inside the test, which never
        // called production code and passed even with a constant delay. It now calls the default factory (#381).
        // The delay is pow(a,4) + 15 + rand(0..29) * (a+1), so the ranges for attempts 1, 3 and 5 do not overlap:
        // [16, 74], [96, 212] and [640, 814] seconds. A single sample each is therefore deterministic.
        var factory = DefaultOptions().RetryDelayFactory;

        var delayAt1 = factory(1);
        var delayAt3 = factory(3);
        var delayAt5 = factory(5);

        delayAt3.Should().BeGreaterThan(delayAt1,
            "minimum delay at attempt 3 must exceed minimum delay at attempt 1");
        delayAt5.Should().BeGreaterThan(delayAt3,
            "minimum delay at attempt 5 must exceed minimum delay at attempt 3");
    }

    [Fact]
    public void RetryDelayFactory_CanBeOverridden()
    {
        // Validates the contract that RetryDelayFactory is a replaceable delegate.
        var options = new NexJobOptions
        {
            RetryDelayFactory = _ => TimeSpan.FromSeconds(42),
        };

        options.RetryDelayFactory(1).Should().Be(TimeSpan.FromSeconds(42));
        options.RetryDelayFactory(5).Should().Be(TimeSpan.FromSeconds(42));
    }

    [Fact]
    public void RetryDelay_Attempt1_MinimumBound_IsAtLeast16Seconds()
    {
        // Regardless of the random component the formula guarantees at least
        // pow(attempt, 4) + 15 seconds when the random term is 0.
        // We run enough samples to be confident the minimum is never below 16 s.
        var factory = DefaultOptions().RetryDelayFactory;

        // 100 samples is enough to smoke-test the lower bound in practice
        for (var i = 0; i < 100; i++)
        {
            factory(1).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(16),
                "delay must never fall below the deterministic floor of 16 s at attempt 1");
        }
    }
}
