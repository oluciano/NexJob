using System.Net;
using Microsoft.Extensions.Time.Testing;
using NexJob.Configuration;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Unit test suite for Queue Circuit Breaker behavior (3N Matrix: Positive, Negative, Boundary).
/// </summary>
public sealed class QueueCircuitBreakerTests
{
    private sealed class DownstreamApiException : HttpRequestException
    {
        public DownstreamApiException(string message)
            : base(message)
        {
        }
    }

    private sealed class BusinessValidationException : InvalidOperationException
    {
        public BusinessValidationException(string message)
            : base(message)
        {
        }
    }

    [Fact]
    public void N1_Positive_HealthyJobs_KeepCircuitClosed_And_ResetFailures()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider();
        var options = new QueueCircuitBreakerOptions
        {
            ConsecutiveFailuresThreshold = 3,
            OpenDuration = TimeSpan.FromMinutes(1),
        };
        options.BreakOn<DownstreamApiException>();

        var manager = new DefaultQueueCircuitBreakerManager(
            new Dictionary<string, QueueCircuitBreakerOptions>
            {
                ["payments"] = options,
            },
            timeProvider: fakeTime);

        // Act - Simulate 2 failures followed by 1 success
        manager.RecordOutcome("payments", succeeded: false, new DownstreamApiException("503 Service Unavailable"));
        manager.RecordOutcome("payments", succeeded: false, new DownstreamApiException("503 Service Unavailable"));
        var stateBeforeSuccess = manager.GetState("payments", out var concurrencyBefore);

        manager.RecordOutcome("payments", succeeded: true, exception: null);
        var stateAfterSuccess = manager.GetState("payments", out var concurrencyAfter);
        var status = manager.GetStatus("payments");

        // Assert
        Assert.Equal(QueueCircuitState.Closed, stateBeforeSuccess);
        Assert.Equal(QueueCircuitState.Closed, stateAfterSuccess);
        Assert.Equal(int.MaxValue, concurrencyAfter);
        Assert.NotNull(status);
        Assert.Equal(0, status.ConsecutiveFailures);
    }

    [Fact]
    public void N1_Positive_CanaryProbeSuccess_TransitionsHalfOpenToRecovering_AndThenClosed()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider();
        var options = new QueueCircuitBreakerOptions
        {
            ConsecutiveFailuresThreshold = 2,
            OpenDuration = TimeSpan.FromSeconds(60),
            RecoveryDuration = TimeSpan.FromSeconds(60),
            RecoveryConcurrency = 3,
        };
        options.BreakOn<DownstreamApiException>();

        var manager = new DefaultQueueCircuitBreakerManager(
            new Dictionary<string, QueueCircuitBreakerOptions>
            {
                ["payments"] = options,
            },
            timeProvider: fakeTime);

        // Trip to Open
        manager.RecordOutcome("payments", succeeded: false, new DownstreamApiException("503"));
        manager.RecordOutcome("payments", succeeded: false, new DownstreamApiException("503"));
        Assert.Equal(QueueCircuitState.Open, manager.GetState("payments", out _));

        // Advance fake time past cooldown
        fakeTime.Advance(TimeSpan.FromSeconds(65));

        // State becomes HalfOpen, allowing exactly 1 probe
        var halfOpenState = manager.GetState("payments", out var probeConcurrency);
        Assert.Equal(QueueCircuitState.HalfOpen, halfOpenState);
        Assert.Equal(1, probeConcurrency);

        // Subsequent check while probe in flight allows 0
        manager.GetState("payments", out var secondCheckConcurrency);
        Assert.Equal(0, secondCheckConcurrency);

        // Canary probe succeeds -> Moves to Recovering
        manager.RecordOutcome("payments", succeeded: true, exception: null);
        var recoveringState = manager.GetState("payments", out var rampUpConcurrency);
        Assert.Equal(QueueCircuitState.Recovering, recoveringState);
        Assert.Equal(3, rampUpConcurrency);

        // Advance fake time past recovery duration
        fakeTime.Advance(TimeSpan.FromSeconds(65));

        // Re-evaluating after recovery duration -> Closed
        var closedState = manager.GetState("payments", out var fullConcurrency);
        Assert.Equal(QueueCircuitState.Closed, closedState);
        Assert.Equal(int.MaxValue, fullConcurrency);
    }

    [Fact]
    public void N2_Negative_ConsecutiveFailures_TripCircuitOpen_AndPauseQueue()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider();
        var options = new QueueCircuitBreakerOptions
        {
            ConsecutiveFailuresThreshold = 3,
            OpenDuration = TimeSpan.FromMinutes(2),
        };
        options.BreakOn<DownstreamApiException>();

        var manager = new DefaultQueueCircuitBreakerManager(
            new Dictionary<string, QueueCircuitBreakerOptions>
            {
                ["payments"] = options,
            },
            timeProvider: fakeTime);

        // Act
        manager.RecordOutcome("payments", succeeded: false, new DownstreamApiException("Timeout 1"));
        manager.RecordOutcome("payments", succeeded: false, new DownstreamApiException("Timeout 2"));
        Assert.Equal(QueueCircuitState.Closed, manager.GetState("payments", out var allowedBefore));
        Assert.Equal(int.MaxValue, allowedBefore);

        // 3rd failure trips the circuit
        manager.RecordOutcome("payments", succeeded: false, new DownstreamApiException("Timeout 3"));
        var trippedState = manager.GetState("payments", out var allowedAfterTrip);
        var status = manager.GetStatus("payments");

        // Assert
        Assert.Equal(QueueCircuitState.Open, trippedState);
        Assert.Equal(0, allowedAfterTrip);
        Assert.NotNull(status);
        Assert.Equal(3, status.ConsecutiveFailures);
        Assert.NotNull(status.RemainingCooldown);
        Assert.True(status.RemainingCooldown.Value.TotalSeconds > 0);
    }

    [Fact]
    public void N2_Negative_CanaryProbeFailure_RetripsWithExponentialBackoff()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider();
        var options = new QueueCircuitBreakerOptions
        {
            ConsecutiveFailuresThreshold = 2,
            OpenDuration = TimeSpan.FromSeconds(60),
            BackoffMultiplier = 2.0,
            MaxOpenDuration = TimeSpan.FromMinutes(10),
        };
        options.BreakOn<DownstreamApiException>();

        var manager = new DefaultQueueCircuitBreakerManager(
            new Dictionary<string, QueueCircuitBreakerOptions>
            {
                ["payments"] = options,
            },
            timeProvider: fakeTime);

        // Trip Open
        manager.RecordOutcome("payments", succeeded: false, new DownstreamApiException("503"));
        manager.RecordOutcome("payments", succeeded: false, new DownstreamApiException("503"));
        Assert.Equal(QueueCircuitState.Open, manager.GetState("payments", out _));

        // Advance past Cooldown 1 (60s)
        fakeTime.Advance(TimeSpan.FromSeconds(65));
        Assert.Equal(QueueCircuitState.HalfOpen, manager.GetState("payments", out _));

        // Canary probe fails -> Re-trip with 2x backoff (60s * 2 = 120s)
        manager.RecordOutcome("payments", succeeded: false, new DownstreamApiException("503"));
        Assert.Equal(QueueCircuitState.Open, manager.GetState("payments", out var allowedAfterRetrip));
        Assert.Equal(0, allowedAfterRetrip);

        var status = manager.GetStatus("payments");
        Assert.NotNull(status);
        Assert.Equal(1, status.ConsecutiveProbeFailures);

        // Advance 70s -> still in cooldown (total cooldown is 120s)
        fakeTime.Advance(TimeSpan.FromSeconds(70));
        Assert.Equal(QueueCircuitState.Open, manager.GetState("payments", out _));

        // Advance another 60s (total 130s) -> cooldown completed, now HalfOpen
        fakeTime.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(QueueCircuitState.HalfOpen, manager.GetState("payments", out _));
    }

    [Fact]
    public void N3_Boundary_NonEligibleExceptions_DoNotTripCircuit()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider();
        var options = new QueueCircuitBreakerOptions
        {
            ConsecutiveFailuresThreshold = 2,
            OpenDuration = TimeSpan.FromMinutes(5),
        };
        options.BreakOn<DownstreamApiException>();

        var manager = new DefaultQueueCircuitBreakerManager(
            new Dictionary<string, QueueCircuitBreakerOptions>
            {
                ["payments"] = options,
            },
            timeProvider: fakeTime);

        // Act - Trigger multiple non-eligible business validation exceptions
        manager.RecordOutcome("payments", succeeded: false, new BusinessValidationException("Invalid card format"));
        manager.RecordOutcome("payments", succeeded: false, new BusinessValidationException("Invalid CVV"));
        manager.RecordOutcome("payments", succeeded: false, new BusinessValidationException("Expired card"));

        var state = manager.GetState("payments", out var allowed);
        var status = manager.GetStatus("payments");

        // Assert - Circuit remains closed because BusinessValidationException is not a DownstreamApiException
        Assert.Equal(QueueCircuitState.Closed, state);
        Assert.Equal(int.MaxValue, allowed);
        Assert.NotNull(status);
        Assert.Equal(0, status.ConsecutiveFailures);
    }

    [Fact]
    public void N3_Boundary_MultipleQueues_AreStrictlyIsolated()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider();
        var optionsPayments = new QueueCircuitBreakerOptions
        {
            ConsecutiveFailuresThreshold = 1,
            OpenDuration = TimeSpan.FromMinutes(5),
        };
        optionsPayments.BreakOn<DownstreamApiException>();

        var optionsEmails = new QueueCircuitBreakerOptions
        {
            ConsecutiveFailuresThreshold = 5,
            OpenDuration = TimeSpan.FromMinutes(5),
        };
        optionsEmails.BreakOn<DownstreamApiException>();

        var manager = new DefaultQueueCircuitBreakerManager(
            new Dictionary<string, QueueCircuitBreakerOptions>
            {
                ["payments"] = optionsPayments,
                ["emails"] = optionsEmails,
            },
            timeProvider: fakeTime);

        // Act - Trip payments queue
        manager.RecordOutcome("payments", succeeded: false, new DownstreamApiException("Stripe Down"));

        var paymentsState = manager.GetState("payments", out var paymentsAllowed);
        var emailsState = manager.GetState("emails", out var emailsAllowed);
        var defaultState = manager.GetState("default", out var defaultAllowed);

        // Assert
        Assert.Equal(QueueCircuitState.Open, paymentsState);
        Assert.Equal(0, paymentsAllowed);

        // emails and default queues are completely unaffected
        Assert.Equal(QueueCircuitState.Closed, emailsState);
        Assert.Equal(int.MaxValue, emailsAllowed);
        Assert.Equal(QueueCircuitState.Closed, defaultState);
        Assert.Equal(int.MaxValue, defaultAllowed);
    }

    [Fact]
    public void N3_Boundary_ManualReset_InstantlyRestoresClosedState()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider();
        var options = new QueueCircuitBreakerOptions
        {
            ConsecutiveFailuresThreshold = 2,
            OpenDuration = TimeSpan.FromHours(1),
        };
        options.BreakOn<DownstreamApiException>();

        var manager = new DefaultQueueCircuitBreakerManager(
            new Dictionary<string, QueueCircuitBreakerOptions>
            {
                ["payments"] = options,
            },
            timeProvider: fakeTime);

        manager.RecordOutcome("payments", succeeded: false, new DownstreamApiException("503"));
        manager.RecordOutcome("payments", succeeded: false, new DownstreamApiException("503"));
        Assert.Equal(QueueCircuitState.Open, manager.GetState("payments", out _));

        // Act - Operator manually resets circuit via ControlService or Dashboard
        manager.Reset("payments");

        // Assert
        var state = manager.GetState("payments", out var allowed);
        var status = manager.GetStatus("payments");

        Assert.Equal(QueueCircuitState.Closed, state);
        Assert.Equal(int.MaxValue, allowed);
        Assert.NotNull(status);
        Assert.Equal(0, status.ConsecutiveFailures);
        Assert.Equal(0, status.ConsecutiveProbeFailures);
        Assert.Null(status.OpenedAt);
        Assert.Null(status.NextProbeAt);
    }
}
