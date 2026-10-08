using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexJob.SqlServer;
using NexJob.Storage;
using Xunit;
using Xunit.Abstractions;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Recovery after a real process crash. A NexJob node runs in its own process (<c>NexJob.ReliabilityTests.Worker</c>), its
/// job announces that it started and hangs, and the process is killed with <c>Process.Kill</c>. A second host in the test
/// has to recover the job. NexJob holds no database lock or open transaction while a job runs, so what the kill leaves is
/// the job in <c>Processing</c> with a heartbeat that stops moving; the value of this test is that nothing here is simulated.
/// SQL Server only: the other providers share the same code path.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class SqlServerCrashTests
    : DistributedReliabilityTestBase,
      IClassFixture<SqlServerReliabilityFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly SqlServerReliabilityFixture _fixture;
    private readonly ITestOutputHelper _output;

    public SqlServerCrashTests(SqlServerReliabilityFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task JobRunningInAKilledProcess_IsRecoveredByAnotherHost_AndRunsOnce()
    {
        var queue = $"crash-{Guid.NewGuid():N}";
        var marker = Path.Combine(Path.GetTempPath(), $"nexjob-crash-{Guid.NewGuid():N}.marker");
        var log = new ExecutionLog();

        JobId jobId;
        using (var seed = BuildHost(Storage(), s => Register(s, log), workers: 1, queues: [queue]))
        {
            jobId = await seed.Services.GetRequiredService<IScheduler>().EnqueueAsync<CrashTargetJob>(queue: queue);
        }

        using var worker = StartWorker(queue, marker);
        try
        {
            // Behavior changed in v5.10: the file appears before the worker has written the job id into it; wait for the content (#371).
            (await WaitUntil(() => MarkerHasContentAsync(marker), Timeout))
                .Should().BeTrue($"the worker process should start the job (exited={worker.HasExited})");
            (await File.ReadAllTextAsync(marker)).Should().Be(jobId.Value.ToString(), "the worker took the job we enqueued");

            using var observer = BuildHost(Storage(), s => Register(s, log), workers: 1, queues: ["nobody-serves-this"]);
            var dashboard = observer.Services.GetRequiredService<IDashboardStorage>();
            (await dashboard.GetJobByIdAsync(jobId))!.Status.Should().Be(JobStatus.Processing);

            worker.Kill(entireProcessTree: true);
            await worker.WaitForExitAsync();
        }
        finally
        {
            if (!worker.HasExited)
            {
                worker.Kill(entireProcessTree: true);
            }

            File.Delete(marker);
        }

        // The dead process's last heartbeat has to become older than the timeout the recovering host uses (2 s).
        await Task.Delay(TimeSpan.FromSeconds(2.5));

        using var recovering = BuildHost(Storage(), s => Register(s, log), workers: 1, queues: [queue], heartbeatTimeout: TimeSpan.FromSeconds(2));
        await recovering.StartAsync();

        (await WaitForJobStatus(recovering, jobId, JobStatus.Succeeded, Timeout)).Should().NotBeNull("another host recovers the job the dead process left");
        await Task.Delay(TimeSpan.FromSeconds(2.5)); // another watcher cycle

        log.Count($"done:{jobId.Value}").Should().Be(1, "the recovered job runs exactly once");
        var stored = await recovering.Services.GetRequiredService<IDashboardStorage>().GetJobByIdAsync(jobId);
        stored!.Attempts.Should().Be(2, "the attempt of the process that died is not given back");

        await recovering.StopAsync();
    }

    private static async Task<bool> MarkerHasContentAsync(string marker)
    {
        try
        {
            return File.Exists(marker) && (await File.ReadAllTextAsync(marker)).Length > 0;
        }
        catch (IOException)
        {
            return false; // the worker still has the file open for writing
        }
    }

    private static void Register(IServiceCollection services, ExecutionLog log)
    {
        services.AddSingleton(log);
        services.AddTransient<CrashTargetJob>();
    }

    private Action<IServiceCollection> Storage() => s => s.AddNexJobSqlServer(_fixture.ConnectionString);

    private Process StartWorker(string queue, string marker)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name; // .../bin/<Configuration>/net8.0/
        var testsRoot = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Parent!.Parent!.Parent!.FullName;
        var dll = Path.Combine(testsRoot, "NexJob.ReliabilityTests.Worker", "bin", configuration, "net8.0", "NexJob.ReliabilityTests.Worker.dll");
        File.Exists(dll).Should().BeTrue($"the worker has to be built next to the tests: {dll}");

        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(dll);
        start.ArgumentList.Add(_fixture.ConnectionString);
        start.ArgumentList.Add(queue);
        start.ArgumentList.Add(marker);

        var process = Process.Start(start)!;
        process.OutputDataReceived += (_, e) => Report("worker", e.Data);
        process.ErrorDataReceived += (_, e) => Report("worker!", e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private void Report(string source, string? line)
    {
        if (line is not null)
        {
            try
            {
                _output.WriteLine($"[{source}] {line}");
            }
            catch (InvalidOperationException)
            {
                // the test already finished
            }
        }
    }
}
