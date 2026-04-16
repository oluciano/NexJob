using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// Hardening unit tests for <see cref="JobRecordFactory"/>.
/// Targets 100% branch coverage for record construction logic.
/// </summary>
public sealed class JobRecordFactoryHardeningTests
{
    private readonly NexJobOptions _options = new() { MaxAttempts = 5 };

    /// <summary>Tests that generic Build correctly sets types and serializes input.</summary>
    [Fact]
    public void BuildGeneric_WithInput_SetsCorrectProperties()
    {
        var input = new TestInput { Value = "hardened" };
        var job = JobRecordFactory.Build<TestJobWithInput, TestInput>(input, _options);

        job.JobType.Should().Be(typeof(TestJobWithInput).AssemblyQualifiedName);
        job.InputType.Should().Be(typeof(TestInput).AssemblyQualifiedName);
        job.InputJson.Should().Be(JsonSerializer.Serialize(input));
        job.MaxAttempts.Should().Be(5);
    }

    /// <summary>Tests that generic Build for IJob sets NoInput correctly.</summary>
    [Fact]
    public void BuildGeneric_NoInput_SetsNoInputProperties()
    {
        var job = JobRecordFactory.Build<TestJob>(_options);

        job.JobType.Should().Be(typeof(TestJob).AssemblyQualifiedName);
        job.InputType.Should().Be(typeof(NoInput).AssemblyQualifiedName);
        job.InputJson.Should().Be(JsonSerializer.Serialize(NoInput.Instance));
    }

    /// <summary>Tests that TraceParent is captured from Activity.Current when not provided.</summary>
    [Fact]
    public void Build_CapturesTraceParentFromActivity()
    {
        using var activity = new Activity("Hardening").Start();
        activity.SetIdFormat(ActivityIdFormat.W3C);

        var job = JobRecordFactory.Build<TestJob>(_options);

        job.TraceParent.Should().Be(activity.Id);
    }

    /// <summary>Tests that provided TraceParent overrides Activity.Current.</summary>
    [Fact]
    public void Build_ManualTraceParentOverridesActivity()
    {
        using var activity = new Activity("Hardening").Start();
        var manual = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

        var job = JobRecordFactory.Build<TestJob>(_options, traceParent: manual);

        job.TraceParent.Should().Be(manual);
    }

    /// <summary>Tests the dynamic Build method used by trigger packages.</summary>
    [Fact]
    public void BuildDynamic_SetsProvidedStrings()
    {
        var job = JobRecordFactory.Build(
            "CustomJob",
            "CustomInput",
            "{}",
            _options,
            queue: "urgent",
            priority: JobPriority.Critical);

        job.JobType.Should().Be("CustomJob");
        job.InputType.Should().Be("CustomInput");
        job.Queue.Should().Be("urgent");
        job.Priority.Should().Be(JobPriority.Critical);
    }

    /// <summary>Support input.</summary>
    public sealed class TestInput
    {
        /// <summary>Gets or sets value.</summary>
        public string Value { get; set; } = string.Empty;
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

    /// <summary>Support job with input.</summary>
    public sealed class TestJobWithInput : IJob<TestInput>
    {
        /// <inheritdoc/>
        public Task ExecuteAsync(TestInput input, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
