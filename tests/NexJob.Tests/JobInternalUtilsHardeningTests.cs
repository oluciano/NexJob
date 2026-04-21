using FluentAssertions;
using Moq;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// Hardening unit tests for internal utility classes.
/// Targets 100% coverage for JobTypeResolver and MigrationPipeline.
/// </summary>
public sealed class JobInternalUtilsHardeningTests
{
    // ─── JobTypeResolver ─────────────────────────────────────────────────────

    /// <summary>Tests that known types are correctly resolved.</summary>
    [Fact]
    public void ResolveJobType_KnownType_ReturnsType()
    {
        var typeName = typeof(TestJob).AssemblyQualifiedName ?? string.Empty;
        var result = JobTypeResolver.ResolveJobType(typeName);
        result.Should().Be(typeof(TestJob));
    }

    /// <summary>Tests that invalid type names return null instead of throwing.</summary>
    [Fact]
    public void ResolveJobType_InvalidType_ReturnsNull()
    {
        var result = JobTypeResolver.ResolveJobType("Invalid.Type.Name");
        result.Should().BeNull();
    }

    // ─── MigrationPipeline ───────────────────────────────────────────────────

    // Behavior changed in v4.0: missing migration descriptor now throws instead of silently returning original JSON.
    /// <summary>Tests that migration pipeline throws when no descriptor is registered for a version gap.</summary>
    [Fact]
    public void MigrationPipeline_NoMigrations_ThrowsInvalidOperationException()
    {
        var sp = new Mock<IServiceProvider>();
        var sut = new MigrationPipeline(sp.Object, Enumerable.Empty<MigrationDescriptor>());
        var json = "{\"foo\":\"bar\"}";

        var act = () => sut.Migrate(json, 1, 2, typeof(object));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Missing migration descriptor*");
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
