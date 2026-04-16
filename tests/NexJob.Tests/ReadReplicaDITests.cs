using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
}
