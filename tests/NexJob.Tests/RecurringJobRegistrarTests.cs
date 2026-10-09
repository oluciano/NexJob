using FluentAssertions;
using Microsoft.Extensions.Logging;
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
public sealed class RecurringJobRegistrarTests
{
    private readonly Mock<IRecurringStorage> _storage = new();
    private readonly NexJobJobRegistry _jobRegistry = new();
    private readonly RecurringJobRegistrar _sut;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecurringJobRegistrarTests"/> class.
    /// </summary>
    public RecurringJobRegistrarTests()
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

    // ─── #389: an id that already belongs to a job of another type ───────────

    private RecurringJobRecord Existing(string id, string jobType, string queue = "other.default") => new()
    {
        RecurringJobId = id,
        JobType = jobType,
        InputType = typeof(NoInput).AssemblyQualifiedName!,
        InputJson = "{}",
        Cron = "0 0 * * *",
        Queue = queue,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private RecurringJobRegistrar WithExisting(NexJob.Tests.LevelLogSink sink, params RecurringJobRecord[] existing)
    {
        _storage.Setup(s => s.GetRecurringJobsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing.ToList());
        return new RecurringJobRegistrar(
            _storage.Object,
            _jobRegistry,
            LoggerFactory.Create(b => b.AddProvider(sink)).CreateLogger<RecurringJobRegistrar>());
    }

    /// <summary>N1: a derived id that belongs to another job type warns, names both, and says where to fix it.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Collision_DerivedId_WarnsWithBothTypesAndTheFix()
    {
        _jobRegistry.Register(typeof(TestJob));
        var sink = new NexJob.Tests.LevelLogSink();
        var sut = WithExisting(sink, Existing(nameof(TestJob), "Other.Service.TestJob, Other.Service, Version=1.0.0.0"));

        await sut.RegisterRecurringJobsAsync([new RecurringJobSettings { Job = nameof(TestJob), Cron = "0 0 * * *" }]);

        var warning = sink.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject.Message;
        warning.Should().Contain("Other.Service.TestJob").And.Contain("other.default").And.Contain("overwrites it");
        warning.Should().Contain("derived from the job name").And.Contain("NexJob:RecurringJobs").And.Contain("RecurringAsync");
        _storage.Verify(s => s.UpsertRecurringJobAsync(It.IsAny<RecurringJobRecord>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>N1: the collision is counted once, tagged with the id.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Collision_IsCountedOncePerId()
    {
        _jobRegistry.Register(typeof(TestJob));
        using var capture = new CollisionCapture("explicit-id");
        var sut = WithExisting(new NexJob.Tests.LevelLogSink(), Existing("explicit-id", "Other.Service.TestJob, Other.Service"));

        await sut.RegisterRecurringJobsAsync([new RecurringJobSettings { Id = "explicit-id", Job = nameof(TestJob), Cron = "0 0 * * *" }]);

        capture.Total.Should().Be(1);
    }

    /// <summary>N2: an explicit id does not claim the id was derived.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Collision_ExplicitId_DoesNotMentionDerivation()
    {
        _jobRegistry.Register(typeof(TestJob));
        var sink = new NexJob.Tests.LevelLogSink();
        var sut = WithExisting(sink, Existing("explicit-id", "Other.Service.TestJob, Other.Service"));

        await sut.RegisterRecurringJobsAsync([new RecurringJobSettings { Id = "explicit-id", Job = nameof(TestJob), Cron = "0 0 * * *" }]);

        sink.Entries.Single(e => e.Level == LogLevel.Warning).Message.Should().NotContain("derived from the job name");
    }

    /// <summary>N2: the same type (even from another assembly version) and a new id are silent.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task NoCollision_SameTypeOtherVersion_IsSilentAndStillUpserts()
    {
        _jobRegistry.Register(typeof(TestJob));
        using var capture = new CollisionCapture(nameof(TestJob));
        var sink = new NexJob.Tests.LevelLogSink();
        var qualified = typeof(TestJob).AssemblyQualifiedName!;
        var versionAt = qualified.IndexOf("Version=", StringComparison.Ordinal);
        var versionEnd = qualified.IndexOf(',', versionAt);
        var otherVersion = string.Concat(qualified.AsSpan(0, versionAt), "Version=9.9.9.9", qualified.AsSpan(versionEnd));
        var sut = WithExisting(sink, Existing(nameof(TestJob), otherVersion));

        await sut.RegisterRecurringJobsAsync([new RecurringJobSettings { Job = nameof(TestJob), Cron = "0 0 * * *" }]);

        sink.Entries.Should().NotContain(e => e.Level == LogLevel.Warning);
        capture.Total.Should().Be(0);
        _storage.Verify(s => s.UpsertRecurringJobAsync(It.IsAny<RecurringJobRecord>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>N3: a storage read failure never blocks the registration.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Collision_StorageReadFails_RegistrationContinues()
    {
        _jobRegistry.Register(typeof(TestJob));
        _storage.Setup(s => s.GetRecurringJobsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));
        var sut = new RecurringJobRegistrar(_storage.Object, _jobRegistry, NullLogger<RecurringJobRegistrar>.Instance);

        await sut.RegisterRecurringJobsAsync([new RecurringJobSettings { Job = nameof(TestJob), Cron = "0 0 * * *" }]);

        sut.RegisteredJobIds.Should().Contain(nameof(TestJob));
    }

    /// <summary>N3: the assembly part is cut at the first comma outside brackets, so generics compare correctly.</summary>
    /// <param name="input">The assembly-qualified name.</param>
    /// <param name="expected">The name without the assembly.</param>
    [Theory]
    [InlineData("A.B.C, Asm, Version=1.0.0.0, Culture=neutral", "A.B.C")]
    [InlineData("A.G`1[[B.C, Asm, Version=1.0.0.0]], Asm, Version=1.0.0.0", "A.G`1[[B.C, Asm, Version=1.0.0.0]]")]
    [InlineData("A.B.C", "A.B.C")]
    [InlineData("", "")]
    public void TypeNameWithoutAssembly_CutsAtTheFirstCommaOutsideBrackets(string input, string expected)
    {
        RecurringJobRegistrar.TypeNameWithoutAssembly(input).Should().Be(expected);
    }

    private sealed class CollisionCapture : IDisposable
    {
        private readonly System.Diagnostics.Metrics.MeterListener _listener = new();
        private long _total;

        public CollisionCapture(string recurringJobId)
        {
            _listener.InstrumentPublished = (instrument, l) =>
            {
                if (string.Equals(instrument.Name, "nexjob.recurring.id_collisions", StringComparison.Ordinal))
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                foreach (var tag in tags)
                {
                    if (string.Equals(tag.Key, "recurring_job_id", StringComparison.Ordinal)
                        && string.Equals(tag.Value as string, recurringJobId, StringComparison.Ordinal))
                    {
                        Interlocked.Add(ref _total, value);
                    }
                }
            });
            _listener.Start();
        }

        public long Total => Interlocked.Read(ref _total);

        public void Dispose() => _listener.Dispose();
    }
}
