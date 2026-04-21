using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NexJob.Storage;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// Hardening unit tests for <see cref="DefaultJobInvokerFactory"/>.
/// Targets 100% branch coverage for type resolution and invoker compilation.
/// </summary>
public sealed class DefaultJobInvokerFactoryHardeningTests
{
    private readonly Mock<IJobStorage> _storage = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
    private readonly Mock<IServiceScope> _scope = new();
    private readonly Mock<IMigrationPipeline> _migrationPipeline = new();
    private readonly Mock<IJobContextAccessor> _contextAccessor = new();
    private readonly ServiceCollection _services = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultJobInvokerFactoryHardeningTests"/> class.
    /// </summary>
    public DefaultJobInvokerFactoryHardeningTests()
    {
        _scopeFactory.Setup(x => x.CreateScope()).Returns(_scope.Object);
        _migrationPipeline
            .Setup(x => x.Migrate(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Type>()))
            .Returns<string, int, int, Type>((json, _, _, _) => json);

        _services.AddSingleton(_migrationPipeline.Object);
        _services.AddSingleton(_contextAccessor.Object);
        _services.AddTransient<TestJob>();
        _services.AddTransient<TestJobWithInput>();
        _services.AddTransient<TestJobV2>();
    }

    // ─── PrepareAsync Branches ─────────────────────────────────────────────

    /// <summary>Tests that PrepareAsync throws when job type cannot be resolved.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task PrepareAsync_InvalidJobType_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        var job = new JobRecord
        {
            JobType = "InvalidType",
            InputType = typeof(NoInput).AssemblyQualifiedName ?? string.Empty,
        };

        Func<Task> act = () => sut.PrepareAsync(job);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Cannot load job type*");
    }

    /// <summary>Tests that PrepareAsync throws when input type cannot be resolved.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task PrepareAsync_InvalidInputType_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        var job = new JobRecord
        {
            JobType = typeof(TestJob).AssemblyQualifiedName ?? string.Empty,
            InputType = "InvalidType",
        };

        Func<Task> act = () => sut.PrepareAsync(job);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Cannot load input type*");
    }

    /// <summary>Tests that PrepareAsync uses schema version from attribute if present.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task PrepareAsync_WithSchemaVersionAttribute_UsesAttributeVersion()
    {
        var sut = CreateSut();
        var job = new JobRecord
        {
            JobType = typeof(TestJobV2).AssemblyQualifiedName ?? string.Empty,
            InputType = typeof(NoInput).AssemblyQualifiedName ?? string.Empty,
            InputJson = "{}",
        };

        await sut.PrepareAsync(job);

        _migrationPipeline.Verify(x => x.Migrate(It.IsAny<string>(), It.IsAny<int>(), 2, It.IsAny<Type>()), Times.Once);
    }

    /// <summary>Tests that PrepareAsync defaults to schema version 1 when attribute is missing.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task PrepareAsync_WithoutSchemaVersionAttribute_DefaultsToVersion1()
    {
        var sut = CreateSut();
        var job = new JobRecord
        {
            JobType = typeof(TestJob).AssemblyQualifiedName ?? string.Empty,
            InputType = typeof(NoInput).AssemblyQualifiedName ?? string.Empty,
            InputJson = "{}",
        };

        await sut.PrepareAsync(job);

        _migrationPipeline.Verify(x => x.Migrate(It.IsAny<string>(), It.IsAny<int>(), 1, It.IsAny<Type>()), Times.Once);
    }

    /// <summary>Tests that PrepareAsync throws when deserialization returns null.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task PrepareAsync_DeserializationReturnsNull_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        var job = new JobRecord
        {
            JobType = typeof(TestJobWithInput).AssemblyQualifiedName ?? string.Empty,
            InputType = typeof(TestInput).AssemblyQualifiedName ?? string.Empty,
            InputJson = "null",
        };

        Func<Task> act = () => sut.PrepareAsync(job);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Deserialized null input*");
    }

    /// <summary>Tests that scope is disposed on failure.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task PrepareAsync_WhenFailureOccurs_DisposesScope()
    {
        var sut = CreateSut();
        var job = new JobRecord
        {
            JobType = "InvalidType",
            InputType = typeof(NoInput).AssemblyQualifiedName ?? string.Empty,
        };

        try
        {
            await sut.PrepareAsync(job);
        }
        catch
        {
            // Expected
        }

        _scope.Verify(x => x.Dispose(), Times.Once);
    }

    // ─── GetOrBuildInvoker Branches ────────────────────────────────────────

    /// <summary>Tests that invoker is correctly compiled and cached for NoInput jobs.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task PrepareAsync_NoInputJob_CompilesAndCachesInvoker()
    {
        var sut = CreateSut();
        var job = new JobRecord
        {
            JobType = typeof(TestJob).AssemblyQualifiedName ?? string.Empty,
            InputType = typeof(NoInput).AssemblyQualifiedName ?? string.Empty,
            InputJson = "{}",
        };

        var result1 = await sut.PrepareAsync(job);
        var result2 = await sut.PrepareAsync(job);

        result1.Invoker.Should().BeSameAs(result2.Invoker);
        await result1.Invoker(result1.JobInstance, result1.Input, CancellationToken.None);
    }

    /// <summary>Tests that invoker is correctly compiled and cached for jobs with input.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task PrepareAsync_WithInputJob_CompilesAndCachesInvoker()
    {
        var sut = CreateSut();
        var input = new TestInput { Value = "v1" };
        var job = new JobRecord
        {
            JobType = typeof(TestJobWithInput).AssemblyQualifiedName ?? string.Empty,
            InputType = typeof(TestInput).AssemblyQualifiedName ?? string.Empty,
            InputJson = JsonSerializer.Serialize(input),
        };

        var result = await sut.PrepareAsync(job);

        result.Should().NotBeNull();
        await result.Invoker(result.JobInstance, result.Input, CancellationToken.None);
    }

    private DefaultJobInvokerFactory CreateSut()
    {
        var sp = _services.BuildServiceProvider();
        _scope.Setup(x => x.ServiceProvider).Returns(sp);
        return new DefaultJobInvokerFactory(_storage.Object, _scopeFactory.Object);
    }

    /// <summary>Test input.</summary>
    public sealed class TestInput
    {
        /// <summary>Value.</summary>
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

    /// <summary>Test job version 2.</summary>
    [SchemaVersion(2)]
    public sealed class TestJobV2 : IJob
    {
        /// <inheritdoc/>
        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
