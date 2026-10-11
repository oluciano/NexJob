using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using NexJob.Internal;
using NexJob.Postgres;
using NexJob.SqlServer;
using NexJob.Storage;
using Xunit;

namespace NexJob.Tests;

public sealed class ReadReplicaDITests
{
    [Fact]
    public void UseDashboardReadReplica_Postgres_RegistersSeparateIDashboardStorage()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(new NexJobOptions());

        // Act
        services.AddNexJobPostgres("Host=primary;Database=nexjob");
        NexJobPostgresExtensions.UseDashboardReadReplica(
            new NexJobBuilder(services),
            "Host=replica;Database=nexjob");

        // Assert
        var dashboardDescriptor = services.Last(d => d.ServiceType == typeof(IDashboardStorage));
        var jobStorageDescriptor = services.Last(d => d.ServiceType == typeof(IJobStorage));

        dashboardDescriptor.ImplementationFactory.Should().NotBeNull();
        jobStorageDescriptor.ImplementationFactory.Should().NotBeNull();
    }

    [Fact]
    public void UseDashboardReadReplica_SqlServer_RegistersSeparateIDashboardStorage()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(new NexJobOptions());

        // Act
        services.AddNexJobSqlServer("Server=primary;Database=nexjob");
        NexJobSqlServerExtensions.UseDashboardReadReplica(
            new NexJobBuilder(services),
            "Server=replica;Database=nexjob");

        // Assert
        var dashboardDescriptor = services.Last(d => d.ServiceType == typeof(IDashboardStorage));
        var jobStorageDescriptor = services.Last(d => d.ServiceType == typeof(IJobStorage));

        dashboardDescriptor.ImplementationFactory.Should().NotBeNull();
        jobStorageDescriptor.ImplementationFactory.Should().NotBeNull();
    }

    [Fact]
    public async Task ControlService_WithAReadReplica_RequeuesAndDeletesOnThePrimary()
    {
        // N1 (Positive): operator actions are writes, so they never reach the replica.
        var replica = new Mock<IDashboardStorage>();
        await using var provider = BuildWithReplica(replica.Object, out var primary);
        var control = provider.GetRequiredService<IJobControlService>();
        var id = JobId.New();

        await control.RequeueJobAsync(id);
        await control.DeleteJobAsync(id);

        replica.Verify(r => r.RequeueJobAsync(It.IsAny<JobId>(), It.IsAny<CancellationToken>()), Times.Never);
        replica.Verify(r => r.DeleteJobAsync(It.IsAny<JobId>(), It.IsAny<CancellationToken>()), Times.Never);
        (await primary.GetJobByIdAsync(id)).Should().BeNull();
    }

    [Fact]
    public async Task ControlService_WithAReadReplica_ActsOnAJobThatOnlyThePrimaryHolds()
    {
        // N2 (Negative for the replica): a job the replica has not received yet is still deleted.
        var replica = new Mock<IDashboardStorage>();
        await using var provider = BuildWithReplica(replica.Object, out var primary);
        var job = new JobRecord
        {
            Id = JobId.New(),
            JobType = typeof(ReadReplicaDITests).AssemblyQualifiedName!,
            Queue = "default",
            Priority = JobPriority.Normal,
            Status = JobStatus.Enqueued,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await primary.EnqueueAsync(job);

        await provider.GetRequiredService<IJobControlService>().DeleteJobAsync(job.Id);

        (await primary.GetJobByIdAsync(job.Id)).Should().BeNull();
    }

    [Fact]
    public async Task ControlService_WithAReadReplica_IgnoresAnUnknownId()
    {
        // N3 (Invalid input)
        await using var provider = BuildWithReplica(new Mock<IDashboardStorage>().Object, out _);
        var control = provider.GetRequiredService<IJobControlService>();

        var act = () => control.RequeueJobAsync(new JobId(Guid.NewGuid()));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OrphanWatcher_WithAReadReplica_ReadsTheFailedJobFromThePrimary()
    {
        // The replica may lag and not hold the job yet: the dead-letter handler must still be called.
        var replica = new Mock<IDashboardStorage>();
        var dispatcher = new Mock<IDeadLetterDispatcher>();
        var called = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<JobRecord>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()))
            .Callback(() => called.TrySetResult())
            .Returns(Task.CompletedTask);
        await using var provider = BuildWithReplica(replica.Object, out var primary, dispatcher.Object);
        var job = new JobRecord
        {
            Id = JobId.New(),
            JobType = typeof(ReadReplicaDITests).AssemblyQualifiedName!,
            Queue = "default",
            Priority = JobPriority.Normal,
            Status = JobStatus.Enqueued,
            CreatedAt = DateTimeOffset.UtcNow,
            MaxAttempts = 1,
        };
        await primary.EnqueueAsync(job);
        (await primary.FetchNextAsync(["default"])).Should().NotBeNull();
        await Task.Delay(150); // older than the heartbeat timeout: nothing else can make it an orphan

        var watcher = provider.GetServices<IHostedService>().OfType<OrphanedJobWatcherService>().Single();
        await watcher.StartAsync(CancellationToken.None);
        var finished = await Task.WhenAny(called.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        await watcher.StopAsync(CancellationToken.None);

        finished.Should().BeSameAs(called.Task);
    }

    private static ServiceProvider BuildWithReplica(
        IDashboardStorage replica, out InMemoryStorageProvider primary, IDeadLetterDispatcher? dispatcher = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexJob(o => o.HeartbeatTimeout = TimeSpan.FromMilliseconds(100));
        services.AddSingleton(replica); // what UseDashboardReadReplica does: replaces only IDashboardStorage
        services.AddSingleton<IDashboardStorage>(replica);
        if (dispatcher is not null)
        {
            services.Replace(ServiceDescriptor.Singleton(dispatcher));
        }

        var provider = services.BuildServiceProvider();
        primary = provider.GetRequiredService<InMemoryStorageProvider>();
        return provider;
    }
}
