using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests;

public sealed class MigrationPipelineTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    private static MigrationPipeline BuildPipeline(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        configure?.Invoke(services);
        var sp = services.BuildServiceProvider();
        return new MigrationPipeline(sp, sp.GetServices<MigrationDescriptor>());
    }

    // ─── no migration needed ──────────────────────────────────────────────────

    [Fact]
    public void Migrate_WhenVersionsMatch_ReturnsOriginalJson()
    {
        var pipeline = BuildPipeline();
        const string json = "{\"Value\":42}";

        var result = pipeline.Migrate(json, storedVersion: 1, currentVersion: 1, typeof(V1Payload));

        result.Should().Be(json);
    }

    [Fact]
    public void Migrate_WhenStoredVersionAhead_ReturnsOriginalJson()
    {
        var pipeline = BuildPipeline();
        const string json = "{\"Value\":42}";

        var result = pipeline.Migrate(json, storedVersion: 3, currentVersion: 2, typeof(V2Payload));

        result.Should().Be(json);
    }

    // ─── single migration ─────────────────────────────────────────────────────

    [Fact]
    public void Migrate_WithOneMigration_UpgradesPayload()
    {
        var pipeline = BuildPipeline(s =>
        {
            s.AddSingleton<IJobMigration<V1Payload, V2Payload>>(new V1ToV2Migration());
            s.AddSingleton(new MigrationDescriptor(typeof(V1Payload), typeof(V2Payload)));
        });

        const string json = "{\"OldName\":\"hello\"}";

        var result = pipeline.Migrate(json, storedVersion: 1, currentVersion: 2, typeof(V2Payload));

        result.Should().Contain("NewName");
        result.Should().Contain("hello");
    }

    // ─── no migration registered ──────────────────────────────────────────────

    // Behavior changed in v4.0: missing migration descriptor now throws instead of silently returning original JSON.
    [Fact]
    public void Migrate_WithNoMigrationRegistered_ThrowsInvalidOperationException()
    {
        var pipeline = BuildPipeline();
        const string json = "{\"OldName\":\"hello\"}";

        var act = () => pipeline.Migrate(json, storedVersion: 1, currentVersion: 2, typeof(V2Payload));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Missing migration descriptor*");
    }
}

// ─── Stub types ───────────────────────────────────────────────────────────────

public sealed class V1Payload
{
    public int Value { get; set; }

    public string OldName { get; set; } = string.Empty;
}

public sealed class V2Payload
{
    public int Value { get; set; }

    public string NewName { get; set; } = string.Empty;
}

public sealed class V1ToV2Migration : IJobMigration<V1Payload, V2Payload>
{
    public V2Payload Migrate(V1Payload old) =>
        new V2Payload { Value = old.Value, NewName = old.OldName };
}
