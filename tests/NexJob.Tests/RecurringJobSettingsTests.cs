using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NexJob.Configuration;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Tests for the new RecurringJobSettings configuration API.
/// Covers job resolution by name, error handling for unknown/ambiguous jobs,
/// ID derivation, input type inference, and configuration binding.
/// </summary>
public sealed class RecurringJobSettingsTests
{
    [Fact]
    public void JobResolvedByName_WhenRegistered()
    {
        // Arrange
        var services = new ServiceCollection();
        var registry = new NexJobJobRegistry();
        registry.Register(typeof(TestJob1));
        services.AddSingleton(registry);

        // Act
        var registrar = new RecurringJobRegistrar(
            new InMemoryStorageProvider(),
            registry,
            NullLogger<RecurringJobRegistrar>.Instance);
        var settings = new RecurringJobSettings
        {
            Job = "TestJob1",
            Cron = "0 0 * * *",
        };

        // Should not throw - job is registered
        var act = async () => await registrar.RegisterRecurringJobsAsync([settings], default);

        // Assert
        act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UnknownJob_DoesNotRegister()
    {
        // Arrange
        var storage = new InMemoryStorageProvider();
        var registry = new NexJobJobRegistry();
        var registrar = new RecurringJobRegistrar(
            storage,
            registry,
            NullLogger<RecurringJobRegistrar>.Instance);
        var settings = new RecurringJobSettings
        {
            Job = "NonExistentJob",
            Cron = "0 0 * * *",
        };

        // Act
        await registrar.RegisterRecurringJobsAsync([settings], default);

        // Assert - job should NOT be registered in storage
        var all = await storage.GetRecurringJobsAsync();
        all.Should().BeEmpty("unknown job should not be registered");
    }

    [Fact]
    public async Task IdOmitted_DerivedFromJob()
    {
        // Arrange
        var storage = new InMemoryStorageProvider();
        var registry = new NexJobJobRegistry();
        registry.Register(typeof(TestJob1));

        var registrar = new RecurringJobRegistrar(
            storage,
            registry,
            NullLogger<RecurringJobRegistrar>.Instance);
        var settings = new RecurringJobSettings
        {
            Job = "TestJob1",
            Id = null,  // Explicitly null
            Cron = "0 0 * * *",
        };

        // Act
        await registrar.RegisterRecurringJobsAsync([settings], default);

        // Assert - recurring job should have been registered with ID derived from job name
        var all = await storage.GetRecurringJobsAsync();
        var registered = all.Should().ContainSingle(r => r.RecurringJobId == "TestJob1").Subject;
        registered.RecurringJobId.Should().Be("TestJob1");
    }

    [Fact]
    public async Task SameJobMultipleTimes_GeneratesUniqueIds()
    {
        // Arrange
        var storage = new InMemoryStorageProvider();
        var registry = new NexJobJobRegistry();
        registry.Register(typeof(TestJob1));

        var registrar = new RecurringJobRegistrar(
            storage,
            registry,
            NullLogger<RecurringJobRegistrar>.Instance);
        var settings = new List<RecurringJobSettings>
        {
            new()
            {
                Job = "TestJob1",
                Cron = "0 0 * * *",
            },
            new()
            {
                Job = "TestJob1",
                Cron = "0 12 * * *",
            },
        };

        // Act
        await registrar.RegisterRecurringJobsAsync(settings, default);

        // Assert - both should register with unique IDs (TestJob1 and TestJob1-1)
        var all = await storage.GetRecurringJobsAsync();
        var jobs = all.Where(r => r.RecurringJobId.StartsWith("TestJob1")).ToList();
        jobs.Should().HaveCount(2);
        jobs.Select(r => r.RecurringJobId).Should().BeEquivalentTo("TestJob1", "TestJob1-1");
    }

    [Fact]
    public async Task InputInferredFromIJobInput_SerializesCorrectly()
    {
        // Arrange
        var storage = new InMemoryStorageProvider();
        var registry = new NexJobJobRegistry();
        registry.Register(typeof(TestJobWithInput));

        var registrar = new RecurringJobRegistrar(
            storage,
            registry,
            NullLogger<RecurringJobRegistrar>.Instance);
        var inputJson = """{ "Value": "test-input" }""";

        var settings = new RecurringJobSettings
        {
            Job = "TestJobWithInput",
            Input = inputJson,
            Cron = "0 0 * * *",
        };

        // Act
        await registrar.RegisterRecurringJobsAsync([settings], default);

        // Assert - input should be inferred and stored correctly
        var all = await storage.GetRecurringJobsAsync();
        var registered = all.Should().ContainSingle(r => r.RecurringJobId == "TestJobWithInput").Subject;
        registered.InputType.Should().Contain(nameof(TestInput));
        registered.InputJson.Should().Contain("test-input");
    }

    [Fact]
    public void AddNexJobJobs_RegistersInJobRegistry()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNexJob(opt => opt.UseInMemory());
        services.AddNexJobJobs(typeof(TestJob1).Assembly);

        // Act
        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<NexJobJobRegistry>();

        // Assert - TestJob1 should be in the registry
        registry.Types.Should().Contain(t => t.Name == "TestJob1");
    }

    [Fact]
    public async Task MissingJob_DoesNotRegister()
    {
        // Arrange
        var storage = new InMemoryStorageProvider();
        var registry = new NexJobJobRegistry();
        var registrar = new RecurringJobRegistrar(
            storage,
            registry,
            NullLogger<RecurringJobRegistrar>.Instance);
        var settings = new RecurringJobSettings
        {
            Job = string.Empty,  // Missing job
            Cron = "0 0 * * *",
        };

        // Act
        await registrar.RegisterRecurringJobsAsync([settings], default);

        // Assert - job should NOT be registered
        var all = await storage.GetRecurringJobsAsync();
        all.Should().BeEmpty("job with missing Job field should not be registered");
    }

    [Fact]
    public async Task AmbiguousJob_DoesNotRegister()
    {
        // Arrange
        var storage = new InMemoryStorageProvider();
        var registry = new NexJobJobRegistry();

        // Register two jobs with same name - use nested classes to simulate ambiguity
        registry.Register(typeof(NamespaceA_DuplicateNameJob));
        registry.Register(typeof(NamespaceB_DuplicateNameJob));

        var registrar = new RecurringJobRegistrar(
            storage,
            registry,
            NullLogger<RecurringJobRegistrar>.Instance);
        var settings = new RecurringJobSettings
        {
            Job = "DuplicateNameJob",
            Cron = "0 0 * * *",
        };

        // Act
        await registrar.RegisterRecurringJobsAsync([settings], default);

        // Assert - job should NOT be registered due to ambiguity
        var all = await storage.GetRecurringJobsAsync();
        all.Should().BeEmpty("ambiguous job should not be registered");
    }

    [Fact]
    public async Task InputAsString_FromConfig_RegistersSuccessfully()
    {
        // Arrange
        var storage = new InMemoryStorageProvider();
        var registry = new NexJobJobRegistry();
        registry.Register(typeof(TestJobWithInput));

        var registrar = new RecurringJobRegistrar(
            storage,
            registry,
            NullLogger<RecurringJobRegistrar>.Instance);
        var inputJson = """{"Value": "test-value"}""";

        var settings = new RecurringJobSettings
        {
            Job = "TestJobWithInput",
            Id = "test-with-input",
            Input = inputJson,
            Cron = "* * * * *",
        };

        // Act - should not throw (was throwing InvalidOperationException from JsonElement.GetRawText)
        await registrar.RegisterRecurringJobsAsync([settings], default);

        // Assert
        var all = await storage.GetRecurringJobsAsync();
        var registered = all.Should().ContainSingle(r => r.RecurringJobId == "test-with-input").Subject;
        registered.InputType.Should().Contain(nameof(TestInput));
        registered.InputJson.Should().Contain("test-value");
    }
}

// ─── test job implementations ────────────────────────────────────────────────

internal sealed class TestJob1 : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

internal sealed record TestInput(string Value);

internal sealed class TestJobWithInput : IJob<TestInput>
{
    public Task ExecuteAsync(TestInput input, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

internal sealed class NamespaceA_DuplicateNameJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

internal sealed class NamespaceB_DuplicateNameJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
