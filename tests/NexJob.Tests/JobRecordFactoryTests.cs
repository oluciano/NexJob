using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// Hardening unit tests for <see cref="JobRecordFactory"/>.
/// Targets 100% branch coverage for record construction logic.
/// </summary>
public sealed class JobRecordFactoryTests
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

    /// <summary>N1/N2 (#377): the implicit queue takes the prefix, a named queue does not.</summary>
    /// <param name="queue">The requested queue.</param>
    /// <param name="expected">The stored queue.</param>
    [Theory]
    [InlineData(null, "billing.default")]
    [InlineData("emails", "emails")]
    public void Build_WithQueuePrefix_PrefixesOnlyTheImplicitDefault(string? queue, string expected)
    {
        var options = new NexJobOptions { QueuePrefix = "billing" };

        JobRecordFactory.Build<TestJob>(options, queue).Queue.Should().Be(expected);
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

    /// <summary>N1 (#470): the typed Build stores the version of the job class, not the default 1.</summary>
    [Fact]
    public void BuildGeneric_WithInput_StoresTheSchemaVersionOfTheJobClass()
    {
        var job = JobRecordFactory.Build<VersionedJob, TestInput>(new TestInput(), _options);

        job.SchemaVersion.Should().Be(2);
    }

    /// <summary>N1 (#470): the no-input Build stores the version of the job class.</summary>
    [Fact]
    public void BuildGeneric_NoInput_StoresTheSchemaVersionOfTheJobClass()
    {
        JobRecordFactory.Build<VersionedNoInputJob>(_options).SchemaVersion.Should().Be(3);
    }

    /// <summary>N1 (#470): the string-typed Build (triggers) stores the version of the job class.</summary>
    [Fact]
    public void BuildFromTypeNames_StoresTheSchemaVersionOfTheJobClass()
    {
        var job = JobRecordFactory.Build(
            typeof(VersionedJob).AssemblyQualifiedName!,
            typeof(TestInput).AssemblyQualifiedName!,
            "{}",
            _options);

        job.SchemaVersion.Should().Be(2);
    }

    /// <summary>N2 (#470, guard): a job class without the attribute keeps storing version 1.</summary>
    [Fact]
    public void Build_JobWithoutSchemaVersionAttribute_StoresVersionOne()
    {
        JobRecordFactory.Build<TestJobWithInput, TestInput>(new TestInput(), _options).SchemaVersion.Should().Be(1);
    }

    /// <summary>N3 (#470, guard): a job type name that cannot be loaded stores 1 and does not throw.</summary>
    [Theory]
    [InlineData("Not.A.Real.Type, NoSuchAssembly")]
    [InlineData("")]
    public void BuildFromTypeNames_UnresolvableJobType_StoresVersionOne(string jobType)
    {
        var act = () => JobRecordFactory.Build(jobType, "System.String", "\"x\"", _options);

        act.Should().NotThrow().Subject.SchemaVersion.Should().Be(1);
    }

    /// <summary>N1/N2/N3: Tags null-coalescing on generic Build preserves provided tags or defaults to empty list.</summary>
    [Fact]
    public void BuildGeneric_TagsNullOrProvided_HandledCorrectly()
    {
        var providedTags = new[] { "tag1", "tag2" };
        var jobWithTags = JobRecordFactory.Build<TestJob>(_options, tags: providedTags);
        var jobWithoutTags = JobRecordFactory.Build<TestJob>(_options, tags: null);

        jobWithTags.Tags.Should().Equal("tag1", "tag2");
        jobWithoutTags.Tags.Should().NotBeNull().And.BeEmpty();
    }

    /// <summary>N1/N2/N3: Tags null-coalescing on string-based Build preserves provided tags or defaults to empty list.</summary>
    [Fact]
    public void BuildDynamic_TagsNullOrProvided_HandledCorrectly()
    {
        var providedTags = new[] { "urgent", "batch" };
        var jobWithTags = JobRecordFactory.Build("CustomJob", "CustomInput", "{}", _options, tags: providedTags);
        var jobWithoutTags = JobRecordFactory.Build("CustomJob", "CustomInput", "{}", _options, tags: null);

        jobWithTags.Tags.Should().Equal("urgent", "batch");
        jobWithoutTags.Tags.Should().NotBeNull().And.BeEmpty();
    }

    /// <summary>N1/N2: RetryAttribute on job class is resolved when explicitMaxAttempts is null.</summary>
    [Fact]
    public void Build_WithRetryAttribute_ResolvesAttributeAttempts()
    {
        var job = JobRecordFactory.Build<JobWithRetryAttribute>(_options);
        var dynamicJob = JobRecordFactory.Build(
            typeof(JobWithRetryAttribute).AssemblyQualifiedName!,
            typeof(NoInput).AssemblyQualifiedName!,
            "{}",
            _options);

        job.MaxAttempts.Should().Be(7);
        dynamicJob.MaxAttempts.Should().Be(7);
    }

    /// <summary>Support job with RetryAttribute.</summary>
    [Retry(7)]
    public sealed class JobWithRetryAttribute : IJob
    {
        /// <inheritdoc/>
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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

    /// <summary>Support job at schema version 2.</summary>
    [SchemaVersion(2)]
    public sealed class VersionedJob : IJob<TestInput>
    {
        /// <inheritdoc/>
        public Task ExecuteAsync(TestInput input, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>Support job without input at schema version 3.</summary>
    [SchemaVersion(3)]
    public sealed class VersionedNoInputJob : IJob
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
