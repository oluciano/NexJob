using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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

    // ─── 3N Matrix Tests (Configurable via NexJobOptions) ────────────────────

    // N1: Positive paths
    [Fact]
    public async Task CheckHealthAsync_WithOptions_StorageResponds_BelowThreshold_ReturnsHealthy()
    {
        var options = new NexJobOptions
        {
            HealthCheckFailedThreshold = 50,
        };

        var storage = new Mock<IDashboardStorage>();
        storage
            .Setup(x => x.GetMetricsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobMetrics
            {
                Enqueued = 5,
                Processing = 2,
                Succeeded = 100,
                Failed = 20,
                Scheduled = 1,
                Recurring = 2,
            });

        var sut = new NexJobHealthCheck(storage.Object, options);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("NexJob storage is responsive.");
        result.Data["failed"].Should().Be(20);
        result.Data["succeeded"].Should().Be(100);
    }

    [Fact]
    public async Task CheckHealthAsync_DependencyInjection_ResolvesAndExecutesHealthy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexJob(opt =>
        {
            opt.HealthCheckTimeout = TimeSpan.FromSeconds(10);
            opt.HealthCheckFailedThreshold = 50;
        });

        using var provider = services.BuildServiceProvider();
        var sut = provider.GetRequiredService<NexJobHealthCheck>();

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("NexJob storage is responsive.");
    }

    // N2: Negative paths
    [Fact]
    public async Task CheckHealthAsync_WithOptions_FailedJobsExceedThreshold_ReturnsDegraded()
    {
        var options = new NexJobOptions
        {
            HealthCheckFailedThreshold = 50,
        };

        var storage = new Mock<IDashboardStorage>();
        storage
            .Setup(x => x.GetMetricsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobMetrics
            {
                Failed = 51,
            });

        var sut = new NexJobHealthCheck(storage.Object, options);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("Dead-letter queue contains 51 failed jobs (threshold: 50).");
        result.Data["failed"].Should().Be(51);
    }

    [Fact]
    public async Task CheckHealthAsync_WithOptions_StorageOperationCanceled_ReturnsUnhealthy()
    {
        var options = new NexJobOptions
        {
            HealthCheckTimeout = TimeSpan.FromSeconds(3),
        };

        var storage = new Mock<IDashboardStorage>();
        storage
            .Setup(x => x.GetMetricsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var sut = new NexJobHealthCheck(storage.Object, options);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("NexJob storage did not respond within 3 seconds.");
    }

    [Fact]
    public async Task CheckHealthAsync_WithOptions_StorageThrowsException_ReturnsUnhealthy()
    {
        var options = new NexJobOptions();
        var storage = new Mock<IDashboardStorage>();
        storage
            .Setup(x => x.GetMetricsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage down"));

        var sut = new NexJobHealthCheck(storage.Object, options);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("NexJob storage is unreachable.");
        result.Exception.Should().BeOfType<InvalidOperationException>();
    }

    // N3: Boundary & Inputs
    [Fact]
    public async Task CheckHealthAsync_WithOptions_ExactThreshold_ReturnsHealthy()
    {
        var options = new NexJobOptions
        {
            HealthCheckFailedThreshold = 50,
        };

        var storage = new Mock<IDashboardStorage>();
        storage
            .Setup(x => x.GetMetricsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobMetrics
            {
                Failed = 50,
            });

        var sut = new NexJobHealthCheck(storage.Object, options);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_WithOptions_CustomTimeout_EnforcesConfiguredTimeout()
    {
        var options = new NexJobOptions
        {
            HealthCheckTimeout = TimeSpan.FromMilliseconds(50),
        };

        var storage = new Mock<IDashboardStorage>();
        storage
            .Setup(x => x.GetMetricsAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async ct =>
            {
                await Task.Delay(500, ct);
                return new JobMetrics();
            });

        var sut = new NexJobHealthCheck(storage.Object, options);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("did not respond within 0.05 seconds.");
    }

    [Fact]
    public async Task CheckHealthAsync_Fallback_WhenConstructedWithoutOptions_UsesStaticFailedJobThreshold()
    {
        var originalThreshold = NexJobHealthCheck.FailedJobThreshold;
        try
        {
            NexJobHealthCheck.FailedJobThreshold = 10;

            var storage = new Mock<IDashboardStorage>();
            storage
                .Setup(x => x.GetMetricsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new JobMetrics
                {
                    Failed = 11,
                });

            var sut = new NexJobHealthCheck(storage.Object);

            var result = await sut.CheckHealthAsync(new HealthCheckContext());

            result.Status.Should().Be(HealthStatus.Degraded);
            result.Description.Should().Contain("threshold: 10");
        }
        finally
        {
            NexJobHealthCheck.FailedJobThreshold = originalThreshold;
        }
    }

    [Fact]
    public void Constructor_NullArguments_ThrowsArgumentNullException()
    {
        var storage = new Mock<IDashboardStorage>();

        var actStorageNull = () => new NexJobHealthCheck(null!);
        actStorageNull.Should().Throw<ArgumentNullException>().WithParameterName("storage");

        var actOptionsNull = () => new NexJobHealthCheck(storage.Object, null!);
        actOptionsNull.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }
}
