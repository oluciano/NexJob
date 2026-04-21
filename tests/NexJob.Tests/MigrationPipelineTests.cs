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

    // ─── multi-step and failure branches ──────────────────────────────────────

    [Fact]
    public void Migrate_WithMultiStepChain_UpgradesThroughAllSteps()
    {
        var pipeline = BuildPipeline(s =>
        {
            s.AddSingleton<IJobMigration<V1Payload, V2Payload>>(new V1ToV2Migration());
            s.AddSingleton<IJobMigration<V2Payload, V3Payload>>(new V2ToV3Migration());
            s.AddSingleton(new MigrationDescriptor(typeof(V1Payload), typeof(V2Payload)));
            s.AddSingleton(new MigrationDescriptor(typeof(V2Payload), typeof(V3Payload)));
        });

        const string json = "{\"Value\":10, \"OldName\":\"step1\"}";

        var result = pipeline.Migrate(json, storedVersion: 1, currentVersion: 3, typeof(V3Payload));

        result.Should().Contain("FinalName");
        result.Should().Contain("step1_v2_v3");
        result.Should().Contain("\"Value\":10");
    }

    [Fact]
    public void Migrate_WithGapInChain_ThrowsInvalidOperationException()
    {
        var pipeline = BuildPipeline(s =>
        {
            // Missing V1->V2, but have V2->V3
            s.AddSingleton<IJobMigration<V2Payload, V3Payload>>(new V2ToV3Migration());
            s.AddSingleton(new MigrationDescriptor(typeof(V2Payload), typeof(V3Payload)));
        });

        const string json = "{\"Value\":10}";

        var act = () => pipeline.Migrate(json, storedVersion: 1, currentVersion: 3, typeof(V3Payload));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Missing migration descriptor*");
    }

    [Fact]
    public void Migrate_WithMigrationNotRegisteredInDI_ThrowsInvalidOperationException()
    {
        var pipeline = BuildPipeline(s =>
        {
            // Descriptor exists but service NOT registered in DI
            s.AddSingleton(new MigrationDescriptor(typeof(V1Payload), typeof(V2Payload)));
        });

        const string json = "{\"Value\":10}";

        var act = () => pipeline.Migrate(json, storedVersion: 1, currentVersion: 2, typeof(V2Payload));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not registered in the DI container*");
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

public sealed class V3Payload
{
    public int Value { get; set; }

    public string FinalName { get; set; } = string.Empty;
}

public sealed class V1ToV2Migration : IJobMigration<V1Payload, V2Payload>
{
    public V2Payload Migrate(V1Payload old) =>
        new V2Payload { Value = old.Value, NewName = old.OldName };
}

public sealed class V2ToV3Migration : IJobMigration<V2Payload, V3Payload>
{
    public V3Payload Migrate(V2Payload old) =>
        new V3Payload { Value = old.Value, FinalName = old.NewName + "_v2_v3" };
}
