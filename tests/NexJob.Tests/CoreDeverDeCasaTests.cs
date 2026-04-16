using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NexJob.Storage;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// Mandatory unit tests for NexJob Core components (v1/v2/v3).
/// Part of the "Dever de Casa" initiative to reach 80% coverage.
/// </summary>
public sealed class CoreDeverDeCasaTests
{
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
            JobType = typeof(TestJob).AssemblyQualifiedName!,
            InputType = typeof(NoInput).AssemblyQualifiedName!,
            InputJson = "{}",
        };

        var result = await sut.PrepareAsync(job, CancellationToken.None);

        result.Should().NotBeNull();
        result.JobInstance.Should().BeOfType<TestJob>();
    }

    /// <summary>Tests that JobTypeResolver resolves known types.</summary>
    [Fact]
    public void JobTypeResolver_ResolveJobType_KnownType_ReturnsType()
    {
        var typeName = typeof(TestJob).AssemblyQualifiedName!;
        var result = JobTypeResolver.ResolveJobType(typeName);
        result.Should().Be(typeof(TestJob));
    }

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
