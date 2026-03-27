using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Tests for <see cref="RetryAttribute"/> — both the pure <see cref="RetryAttribute.ComputeDelay"/>
/// math and the end-to-end dispatcher behaviour when a job is decorated with the attribute.
/// </summary>
public sealed class RetryAttributeTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    private static IHost BuildHost(Action<IServiceCollection> registerJobs, int globalMaxAttempts = 10)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = 2;
                    opt.MaxAttempts = globalMaxAttempts;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(20);
                    opt.RetryDelayFactory = _ => TimeSpan.FromSeconds(60); // long delay so retries never fire
                });
                registerJobs(services);
            })
            .Build();
    }

    // ─── ComputeDelay — unit tests ────────────────────────────────────────────

    [Fact]
    public void ComputeDelay_WithNoInitialDelay_ReturnsZero()
    {
        var attr = new RetryAttribute(3); // InitialDelay = null

        var result = attr.ComputeDelay(1);

        result.Should().Be(TimeSpan.Zero, "null InitialDelay is the signal to use the global factory");
    }

    [Fact]
    public void ComputeDelay_WithMultiplier2_DoublesEachAttempt()
    {
        // initial = 10s, multiplier = 2 — ignoring jitter, attempt N → 10 * 2^(N-1)
        var attr = new RetryAttribute(5) { InitialDelay = "00:00:10", Multiplier = 2.0 };

        // run enough samples to get a stable median despite ±10% jitter
        var delay1 = Average(attr, attempt: 1, samples: 200);
        var delay2 = Average(attr, attempt: 2, samples: 200);
        var delay3 = Average(attr, attempt: 3, samples: 200);

        // attempt 2 ≈ 2× attempt 1, attempt 3 ≈ 2× attempt 2 (within 20% to account for jitter)
        delay2.Should().BeCloseTo(delay1 * 2, TimeSpan.FromSeconds(4),
            "multiplier=2 should double the delay on each subsequent attempt");
        delay3.Should().BeCloseTo(delay2 * 2, TimeSpan.FromSeconds(8),
            "multiplier=2 should double the delay on each subsequent attempt");
    }

    [Fact]
    public void ComputeDelay_WithMaxDelay_NeverExceedsCap()
    {
        var attr = new RetryAttribute(10)
        {
            InitialDelay = "00:01:00", // 1 minute
            Multiplier = 10.0,
            MaxDelay = "00:05:00", // 5 minutes cap
        };

        // MaxDelay is capped at 5min, then ±10% jitter is applied — so observed maximum is 5min * 1.1 = 5.5min
        var absoluteMax = TimeSpan.FromMinutes(5).Multiply(1.15); // 15% headroom for jitter

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            var delay = attr.ComputeDelay(attempt);
            delay.Should().BeLessThanOrEqualTo(
                absoluteMax,
                $"delay at attempt {attempt} must not exceed MaxDelay cap (including jitter)");
        }
    }

    [Fact]
    public void ComputeDelay_HasJitter_WithinExpectedRange()
    {
        // Without jitter, attempt 1 = initial * 1 = 30s.
        // With ±10% jitter the result must be in [27s, 33s].
        var attr = new RetryAttribute(3) { InitialDelay = "00:00:30", Multiplier = 1.0 };

        for (var i = 0; i < 100; i++)
        {
            var delay = attr.ComputeDelay(1);
            delay.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(27),
                "jitter must not reduce delay below 90% of the calculated value");
            delay.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(33),
                "jitter must not increase delay above 110% of the calculated value");
        }
    }

    // ─── dispatcher integration ───────────────────────────────────────────────

    [Fact]
    public async Task Dispatcher_WithRetry0_DeadLettersImmediately()
    {
        using var host = BuildHost(s => s.AddTransient<RetryZeroJob>());
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<RetryZeroJob, RetryZeroInput>(new());

        await Task.Delay(500);

        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        var metrics = await storage.GetMetricsAsync();

        metrics.Failed.Should().Be(1, "[Retry(0)] must dead-letter on the very first failure");
        metrics.Succeeded.Should().Be(0);

        await host.StopAsync();
    }

    [Fact]
    public async Task Dispatcher_WithRetryNoInitialDelay_UsesGlobalFactory()
    {
        // [Retry(3)] with no InitialDelay → uses global RetryDelayFactory (60s in this host).
        // After first failure the job should be Scheduled (not Failed) because attempts < 3.
        using var host = BuildHost(s => s.AddTransient<RetryNoDelayJob>());
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<RetryNoDelayJob, RetryNoDelayInput>(new());

        await Task.Delay(500);

        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        var metrics = await storage.GetMetricsAsync();

        metrics.Failed.Should().Be(0, "[Retry(3)] with retries remaining must not dead-letter");

        await host.StopAsync();
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private static TimeSpan Average(RetryAttribute attr, int attempt, int samples)
    {
        var total = 0L;
        for (var i = 0; i < samples; i++)
        {
            total += attr.ComputeDelay(attempt).Ticks;
        }

        return TimeSpan.FromTicks(total / samples);
    }
}

// ─── Stub jobs ────────────────────────────────────────────────────────────────

public record RetryZeroInput;

/// <summary>Dead-letters immediately — [Retry(0)] means no retries at all.</summary>
[Retry(0)]
public sealed class RetryZeroJob : IJob<RetryZeroInput>
{
    /// <inheritdoc/>
    public Task ExecuteAsync(RetryZeroInput input, CancellationToken cancellationToken)
        => throw new InvalidOperationException("intentional failure — should dead-letter immediately");
}

public record RetryNoDelayInput;

/// <summary>Retries up to 3 times using the global delay factory (no InitialDelay).</summary>
[Retry(3)]
public sealed class RetryNoDelayJob : IJob<RetryNoDelayInput>
{
    /// <inheritdoc/>
    public Task ExecuteAsync(RetryNoDelayInput input, CancellationToken cancellationToken)
        => throw new InvalidOperationException("intentional failure — should retry using global factory");
}
