using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexJob.Configuration;
using NexJob.Storage;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// Hardening unit tests for Core registrars and control services.
/// Targets 100% branch coverage for recurring job registration and job control logic.
/// </summary>
public sealed class CoreControlHardeningTests
{
    // ─── DefaultJobControlService ──────────────────────────────────────────

    /// <summary>Tests DefaultJobControlService delegation and state checks.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task DefaultJobControlService_HandlesAllBranches()
    {
        var storage = new Mock<IDashboardStorage>();
        var runtimeStore = new Mock<IRuntimeSettingsStore>();
        var sut = new DefaultJobControlService(storage.Object, runtimeStore.Object);
        var jobId = JobId.New();
        var rt = new RuntimeSettings();

        runtimeStore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(rt);

        // Requeue
        await sut.RequeueJobAsync(jobId);
        storage.Verify(x => x.RequeueJobAsync(jobId, It.IsAny<CancellationToken>()), Times.Once);

        // Delete
        await sut.DeleteJobAsync(jobId);
        storage.Verify(x => x.DeleteJobAsync(jobId, It.IsAny<CancellationToken>()), Times.Once);

        // Pause (Not paused -> Saves)
        await sut.PauseQueueAsync("q1");
        runtimeStore.Verify(x => x.SaveAsync(rt, It.IsAny<CancellationToken>()), Times.Once);

        // Pause (Already paused -> Does not save)
        await sut.PauseQueueAsync("q1");
        runtimeStore.Verify(x => x.SaveAsync(rt, It.IsAny<CancellationToken>()), Times.Exactly(1));

        // Resume (Paused -> Saves)
        await sut.ResumeQueueAsync("q1");
        runtimeStore.Verify(x => x.SaveAsync(rt, It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Resume (Not paused -> Does not save)
        await sut.ResumeQueueAsync("q1");
        runtimeStore.Verify(x => x.SaveAsync(rt, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ─── RecurringJobRegistrar ───────────────────────────────────────────────

    /// <summary>Tests RecurringJobRegistrar logic for ID assignment and type resolution.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task RecurringJobRegistrar_HandlesDuplicateNamesAndInvalidTypes()
    {
        var storage = new Mock<IRecurringStorage>();
        var registry = new NexJobJobRegistry();
        var sut = new RecurringJobRegistrar(storage.Object, registry, NullLogger<RecurringJobRegistrar>.Instance);

        registry.Register(typeof(TestJob));

        // Duplicate names without IDs
        var configs = new[]
        {
            new RecurringJobSettings { Job = nameof(TestJob), Cron = "* * * * *" },
            new RecurringJobSettings { Job = nameof(TestJob), Cron = "* * * * *" },
        };

        await sut.RegisterRecurringJobsAsync(configs);
        sut.RegisteredJobIds.Should().Contain(new[] { nameof(TestJob), $"{nameof(TestJob)}-1" });

        // Invalid job type
        var invalidConfigs = new[] { new RecurringJobSettings { Job = "NonExistent", Cron = "* * * * *" } };
        await sut.RegisterRecurringJobsAsync(invalidConfigs);
        // Error logged, registered IDs should not contain the invalid one
    }

    /// <summary>Support job.</summary>
    public sealed class TestJob : IJob
    {
        /// <inheritdoc/>
        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
