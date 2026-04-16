using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using NexJob.Storage;
using Xunit;

namespace NexJob.Tests;

public sealed class NexJobHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_StorageResponds_BelowThreshold_ReturnsHealthy()
    {
        const int defaultThreshold = 100;
        NexJobHealthCheck.FailedJobThreshold = defaultThreshold;

        var storage = new Mock<IDashboardStorage>();
        storage
            .Setup(x => x.GetMetricsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobMetrics
            {
                Enqueued = 1,
                Processing = 2,
                Succeeded = 3,
                Failed = 4,
                Scheduled = 5,
                Recurring = 6,
            });

        var sut = new NexJobHealthCheck(storage.Object);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        NexJobHealthCheck.FailedJobThreshold.Should().Be(defaultThreshold);
    }

    [Fact]
    public async Task CheckHealthAsync_StorageResponds_AboveThreshold_ReturnsDegraded()
    {
        const int defaultThreshold = 100;
        NexJobHealthCheck.FailedJobThreshold = defaultThreshold;

        var storage = new Mock<IDashboardStorage>();
        storage
            .Setup(x => x.GetMetricsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobMetrics
            {
                Failed = defaultThreshold + 1,
            });

        var sut = new NexJobHealthCheck(storage.Object);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        NexJobHealthCheck.FailedJobThreshold.Should().Be(defaultThreshold);
    }

    [Fact]
    public async Task CheckHealthAsync_StorageThrowsOperationCanceled_ReturnsUnhealthy()
    {
        const int defaultThreshold = 100;
        NexJobHealthCheck.FailedJobThreshold = defaultThreshold;

        var storage = new Mock<IDashboardStorage>();
        storage
            .Setup(x => x.GetMetricsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var sut = new NexJobHealthCheck(storage.Object);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        NexJobHealthCheck.FailedJobThreshold.Should().Be(defaultThreshold);
    }

    [Fact]
    public async Task CheckHealthAsync_StorageThrowsException_ReturnsUnhealthy()
    {
        const int defaultThreshold = 100;
        NexJobHealthCheck.FailedJobThreshold = defaultThreshold;

        var storage = new Mock<IDashboardStorage>();
        storage
            .Setup(x => x.GetMetricsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var sut = new NexJobHealthCheck(storage.Object);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        NexJobHealthCheck.FailedJobThreshold.Should().Be(defaultThreshold);
    }
}
