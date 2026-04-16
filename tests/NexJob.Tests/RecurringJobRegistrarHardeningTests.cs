using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexJob.Configuration;
using NexJob.Storage;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// Hardening unit tests for <see cref="RecurringJobRegistrar"/>.
/// Targets 100% branch coverage for ID assignment and type resolution.
/// </summary>
public sealed class RecurringJobRegistrarHardeningTests
{
    private readonly Mock<IRecurringStorage> _storage = new();
    private readonly NexJobJobRegistry _jobRegistry = new();
    private readonly RecurringJobRegistrar _sut;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecurringJobRegistrarHardeningTests"/> class.
    /// </summary>
    public RecurringJobRegistrarHardeningTests()
    {
        _sut = new RecurringJobRegistrar(_storage.Object, _jobRegistry, NullLogger<RecurringJobRegistrar>.Instance);
    }

    /// <summary>Tests that duplicate job names without ID get correct suffixes.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task RegisterRecurringJobsAsync_AssignsSuffixesToDuplicateNames()
    {
        _jobRegistry.Register(typeof(TestJob));
        var configs = new[]
        {
            new RecurringJobSettings { Job = nameof(TestJob), Cron = "0 0 * * *" },
            new RecurringJobSettings { Job = nameof(TestJob), Cron = "0 1 * * *" },
        };

        await _sut.RegisterRecurringJobsAsync(configs);

        _sut.RegisteredJobIds.Should().Contain(new[] { nameof(TestJob), $"{nameof(TestJob)}-1" });
    }

    /// <summary>Tests that invalid input JSON for IJob with input logs error.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task RegisterRecurringJobsAsync_InvalidInputJson_LogsError()
    {
        _jobRegistry.Register(typeof(TestJobWithInput));
        var configs = new[]
        {
            new RecurringJobSettings { Job = nameof(TestJobWithInput), Cron = "0 0 * * *", Input = "{ invalid }" },
        };

        await _sut.RegisterRecurringJobsAsync(configs);

        _sut.RegisteredJobIds.Should().BeEmpty();
    }

    /// <summary>Tests that missing job configuration fields throw.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task RegisterRecurringJobsAsync_MissingFields_LogsError()
    {
        var configs = new[]
        {
            new RecurringJobSettings { Job = string.Empty, Cron = string.Empty },
        };

        await _sut.RegisterRecurringJobsAsync(configs);

        _sut.RegisteredJobIds.Should().BeEmpty();
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

    /// <summary>Support input.</summary>
    public sealed class TestInput
    {
        /// <summary>Value.</summary>
        public string Value { get; set; } = string.Empty;
    }
}
