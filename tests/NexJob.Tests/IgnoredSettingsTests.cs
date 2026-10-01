using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob.Configuration;
using Xunit;

namespace NexJob.Tests;

public sealed class IgnoredSettingsTests
{
    [Fact]
    public void GetIgnoredSettings_QueueWithWorkers_IsReported()
    {
        // N1 (Positive): per-queue Workers is accepted by the configuration but never applied.
        var options = new NexJobOptions();
        options.ConfigureQueue("bulk", q => q.Workers = 3);

        options.GetIgnoredSettings().Should().Equal("QueueSettings[bulk].Workers");
    }

    [Fact]
    public void GetIgnoredSettings_NonDefaultDefaultQueue_IsReported()
    {
        var options = new NexJobOptions();
        options.ApplySettings(new NexJobSettings { DefaultQueue = "emails", });

        options.GetIgnoredSettings().Should().Equal("DefaultQueue");
    }

    [Fact]
    public async Task Startup_WithIgnoredSetting_LogsWarning()
    {
        // N2 (Negative path made visible): a setting without effect must not be silent.
        var sink = new LevelLogSink();
        using var host = BuildHost(sink, o => o.ConfigureQueue("bulk", q => q.Workers = 3));

        await host.StartAsync();
        await host.StopAsync();

        sink.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning
            && e.Message.Contains("QueueSettings[bulk].Workers", StringComparison.Ordinal)
            && e.Message.Contains("no effect", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Startup_WithoutIgnoredSettings_LogsNoWarning()
    {
        // N3 (boundary): defaults and a queue without Workers are fine.
        var sink = new LevelLogSink();
        using var host = BuildHost(sink, o => o.ConfigureQueue("bulk", q => q.ExecutionWindow = null));

        await host.StartAsync();
        await host.StopAsync();

        sink.Entries.Should().NotContain(e => e.Message.Contains("no effect", StringComparison.Ordinal));
    }

    [Fact]
    public void GetIgnoredSettings_DefaultsAndExplicitDefaultQueue_AreEmpty()
    {
        // N3: explicitly naming the default queue is harmless, so it is not reported.
        var options = new NexJobOptions();
        options.ApplySettings(new NexJobSettings { DefaultQueue = "default", });

        options.GetIgnoredSettings().Should().BeEmpty();
    }

    private static IHost BuildHost(ILoggerProvider sink, Action<NexJobOptions> configure) =>
        Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.AddProvider(sink))
            .ConfigureServices(services => services.AddNexJob(configure))
            .Build();
}
