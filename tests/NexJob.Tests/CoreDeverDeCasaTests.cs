using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexJob.Configuration;
using NexJob.Storage;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// Mandatory unit tests for NexJob Core components (v1/v2/v3).
/// Part of the "Dever de Casa" initiative to reach 80% coverage.
/// </summary>
public sealed class CoreDeverDeCasaTests
{
    // ─── JobRecordFactory ───────────────────────────────────────────────────

    /// <summary>Tests that JobRecordFactory builds correct records with input.</summary>
    [Fact]
    public void JobRecordFactory_BuildGeneric_WithInput_SetsCorrectTypes()
    {
        var input = new TestInput { Value = "test" };
        var options = new NexJobOptions();
        var job = JobRecordFactory.Build<TestJobWithInput, TestInput>(input, options);

        job.JobType.Should().Be(typeof(TestJobWithInput).AssemblyQualifiedName);
        job.InputType.Should().Be(typeof(TestInput).AssemblyQualifiedName);
        job.InputJson.Should().Be(JsonSerializer.Serialize(input));
    }

    /// <summary>Tests that JobRecordFactory captures TraceParent from Activity.</summary>
    [Fact]
    public void JobRecordFactory_Build_CapturesTraceParentFromActivity()
    {
        using var activity = new Activity("Test").Start();
        activity.SetIdFormat(ActivityIdFormat.W3C);
        var options = new NexJobOptions();

        var job = JobRecordFactory.Build<TestJob>(options);

        job.TraceParent.Should().Be(activity.Id);
    }

    // ─── DefaultJobInvokerFactory ──────────────────────────────────────────

    /// <summary>Tests that DefaultJobInvokerFactory prepares invocation correctly.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task DefaultJobInvokerFactory_PrepareAsync_ResolvesTypes()
    {
        var storage = new Mock<IJobStorage>();
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var scope = new Mock<IServiceScope>();
        var services = new ServiceCollection();

        services.AddTransient<TestJob>();
        var migrationMock = new Mock<IMigrationPipeline>();
        migrationMock.Setup(x => x.Migrate(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Type>()))
            .Returns<string, int, int, Type>((json, _, _, _) => json);
        services.AddSingleton(migrationMock.Object);
        services.AddSingleton(new Mock<IJobContextAccessor>().Object);

        var sp = services.BuildServiceProvider();
        scope.Setup(x => x.ServiceProvider).Returns(sp);
        scopeFactory.Setup(x => x.CreateScope()).Returns(scope.Object);

        var sut = new DefaultJobInvokerFactory(storage.Object, scopeFactory.Object);
        var job = new JobRecord
        {
            Id = JobId.New(),
            JobType = typeof(TestJob).AssemblyQualifiedName ?? string.Empty,
            InputType = typeof(NoInput).AssemblyQualifiedName ?? string.Empty,
            InputJson = "{}",
        };

        var result = await sut.PrepareAsync(job, CancellationToken.None);

        result.Should().NotBeNull();
        result.JobInstance.Should().BeOfType<TestJob>();
    }

    // ─── JobTypeResolver ─────────────────────────────────────────────────────

    /// <summary>Tests that JobTypeResolver resolves known types.</summary>
    [Fact]
    public void JobTypeResolver_ResolveJobType_KnownType_ReturnsType()
    {
        var typeName = typeof(TestJob).AssemblyQualifiedName!;
        var result = JobTypeResolver.ResolveJobType(typeName);
        result.Should().Be(typeof(TestJob));
    }

    // ─── DefaultJobRetryPolicy ───────────────────────────────────────────────

    /// <summary>Tests that DefaultJobRetryPolicy calculates retry delay correctly without attribute.</summary>
    [Fact]
    public void DefaultJobRetryPolicy_ComputeRetryAt_NoAttribute_UsesOptions()
    {
        var options = new NexJobOptions { RetryDelayFactory = att => TimeSpan.FromMinutes(att) };
        var sut = new DefaultJobRetryPolicy(options);
        var job = new JobRecord { Attempts = 1, MaxAttempts = 3, JobType = typeof(TestJob).AssemblyQualifiedName };

        var result = sut.ComputeRetryAt(job, new Exception());

        result.Should().NotBeNull();
        result!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(1), TimeSpan.FromSeconds(5));
    }

    // ─── DefaultDeadLetterDispatcher ─────────────────────────────────────────

    /// <summary>Tests that DefaultDeadLetterDispatcher swallows handler exceptions to protect dispatcher.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task DefaultDeadLetterDispatcher_DispatchAsync_SwallowsExceptions()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var scope = new Mock<IServiceScope>();
        var services = new ServiceCollection();

        var handlerMock = new Mock<IDeadLetterHandler<TestJob>>();
        handlerMock.Setup(x => x.HandleAsync(It.IsAny<JobRecord>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Handler Failure"));

        services.AddSingleton(handlerMock.Object);
        var sp = services.BuildServiceProvider();
        scope.Setup(x => x.ServiceProvider).Returns(sp);
        scopeFactory.Setup(x => x.CreateScope()).Returns(scope.Object);

        var sut = new DefaultDeadLetterDispatcher(scopeFactory.Object, NullLogger<DefaultDeadLetterDispatcher>.Instance);
        var job = new JobRecord { Id = JobId.New(), JobType = typeof(TestJob).AssemblyQualifiedName! };

        Func<Task> act = () => sut.DispatchAsync(job, new Exception("Original"));

        await act.Should().NotThrowAsync("Dispatcher must be protected from handler failures.");
    }

    // ─── JobDispatcherService ────────────────────────────────────────────────

    /// <summary>Tests that JobDispatcherService handles storage failures gracefully.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task JobDispatcherService_HandlesStorageFailure_WithoutCrashing()
    {
        var storage = new Mock<IJobStorage>();
        storage.Setup(x => x.FetchNextAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Storage Down"));

        var runtimeStore = new Mock<IRuntimeSettingsStore>();
        runtimeStore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new RuntimeSettings());

        var options = new NexJobOptions { Workers = 1, PollingInterval = TimeSpan.FromMilliseconds(10) };

        // JobExecutor is sealed/internal, we use a real one with mocks for its dependencies
        var executor = new JobExecutor(
            storage.Object,
            new Mock<IJobInvokerFactory>().Object,
            new Mock<IJobRetryPolicy>().Object,
            new Mock<IDeadLetterDispatcher>().Object,
            new ThrottleRegistry(),
            options,
            Enumerable.Empty<IJobExecutionFilter>(),
            NullLogger<JobExecutor>.Instance);

        var sut = new JobDispatcherService(
            storage.Object,
            executor,
            runtimeStore.Object,
            options,
            new JobWakeUpChannel(),
            NullLogger<JobDispatcherService>.Instance);

        using var cts = new CancellationTokenSource();
        _ = sut.StartAsync(cts.Token);

        await Task.Delay(50);
        await sut.StopAsync(CancellationToken.None);

        storage.Verify(x => x.FetchNextAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ─── support types ───────────────────────────────────────────────────────

    /// <summary>Test input.</summary>
    public sealed class TestInput
    {
        /// <summary>Gets or sets value.</summary>
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>Test job.</summary>
    public sealed class TestJob : IJob
    {
        /// <inheritdoc/>
        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>Test job with input.</summary>
    public sealed class TestJobWithInput : IJob<TestInput>
    {
        /// <inheritdoc/>
        public Task ExecuteAsync(TestInput input, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
