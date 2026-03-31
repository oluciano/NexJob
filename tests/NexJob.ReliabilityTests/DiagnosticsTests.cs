using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob;
using Xunit;
using Xunit.Abstractions;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Reliability tests for logging and diagnostics.
/// Tests that system produces actionable and complete logs
/// for job execution, failures, and recovery scenarios.
///
/// Trait: Reliability
/// </summary>
[Trait("Category", "Reliability")]
public sealed class DiagnosticsTests : ReliabilityTestBase
{
    private readonly ITestOutputHelper _output;

    public DiagnosticsTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task JobExecutionLogsContainIdentifiers()
    {
        ResetTestState();

        var logs = new List<string>();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(new TestLogProvider(logs));
            })
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = 1;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(100);
                });
                services.AddTransient<DiagnosticJob>();
            })
            .Build();

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var input = new DiagnosticJobInput("test message");
        await scheduler.EnqueueAsync<DiagnosticJob, DiagnosticJobInput>(input);

        // Wait for execution
        await Task.Delay(2000);

        // Logs should contain job information
        var allLogs = string.Join("\n", logs);
        _output.WriteLine("Collected logs:");
        _output.WriteLine(allLogs);

        // Should contain job type and message
        allLogs.Should().Contain("DiagnosticJob", "logs should mention job type");
        allLogs.Should().Contain("test message", "logs should contain job input");

        await host.StopAsync();
    }

    [Fact]
    public async Task FailedJobLogsIncludeExceptionDetails()
    {
        ResetTestState();

        var logs = new List<string>();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(new TestLogProvider(logs));
            })
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = 1;
                    opt.MaxAttempts = 2;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(100);
                    opt.RetryDelayFactory = _ => TimeSpan.FromMilliseconds(200);
                });
                services.AddTransient<AlwaysFailJob>();
            })
            .Build();

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<AlwaysFailJob>();

        // Wait for failure attempts
        await Task.Delay(2500);

        var allLogs = string.Join("\n", logs);
        _output.WriteLine("Failed job logs:");
        _output.WriteLine(allLogs);

        // Should contain error message
        allLogs.Should().Contain("intentionally failed", "logs should contain exception message");

        await host.StopAsync();
    }

    [Fact]
    public async Task DeadLetterHandlerExecutionIsLogged()
    {
        ResetTestState();

        var logs = new List<string>();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(new TestLogProvider(logs));
            })
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = 1;
                    opt.MaxAttempts = 1;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(100);
                });
                services.AddTransient<AlwaysFailJob>();
                services.AddTransient<IDeadLetterHandler<AlwaysFailJob>, RecordingDeadLetterHandler<AlwaysFailJob>>();
            })
            .Build();

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<AlwaysFailJob>();

        // Wait for handler
        await Task.Delay(2000);

        var allLogs = string.Join("\n", logs);
        _output.WriteLine("Dead-letter handler logs:");
        _output.WriteLine(allLogs);

        // Should mention handler invocation
        allLogs.Should().Contain("Dead-letter handler invoked", "logs should mention handler invocation");

        await host.StopAsync();
    }

    [Fact]
    public async Task SystemLogsCaptureDispatcherState()
    {
        ResetTestState();

        var logs = new List<string>();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(new TestLogProvider(logs));
                logging.SetMinimumLevel(LogLevel.Debug);
            })
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = 1;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(100);
                });
                services.AddTransient<SuccessJob>();
            })
            .Build();

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<SuccessJob>();

        // Wait for execution
        await Task.Delay(1000);

        var allLogs = string.Join("\n", logs);
        _output.WriteLine("System logs:");
        _output.WriteLine(allLogs);

        // Should have some execution context
        allLogs.Should().NotBeEmpty("system should produce diagnostic logs");

        await host.StopAsync();
    }
}

/// <summary>
/// Test log provider that collects logs into a list.
/// </summary>
internal sealed class TestLogProvider : ILoggerProvider
{
    private readonly List<string> _logs;

    public TestLogProvider(List<string> logs) => _logs = logs;

    public ILogger CreateLogger(string categoryName)
        => new TestLogger(_logs, categoryName);

    public void Dispose()
    {
    }
}

internal sealed class TestLogger : ILogger
{
    private readonly List<string> _logs;
    private readonly string _categoryName;

    public TestLogger(List<string> logs, string categoryName)
    {
        _logs = logs;
        _categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        var logEntry = $"[{logLevel}] {_categoryName}: {message}";

        if (exception != null)
        {
            logEntry += $"\n{exception}";
        }

        _logs.Add(logEntry);
    }
}
