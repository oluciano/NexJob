using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.MongoDB;
using NexJob.Postgres;
using NexJob.Redis;
using NexJob.SqlServer;
using NexJob.Storage;
using Xunit;

namespace NexJob.IntegrationTests.EndToEnd;

/// <summary>
/// Issue #476 on a real provider: a job still running when the shutdown drain ends is back in the queue,
/// with its attempt refunded, by the time <c>StopAsync</c> returns, so a process that exits right after loses nothing.
/// </summary>
public abstract class ShutdownRequeueEndToEndTests
{
    /// <summary>N1: the interrupted job is already requeued, attempt refunded, when StopAsync returns.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task InterruptedJob_IsRequeued_WhenStopAsyncReturns()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = BuildHost(started);
        await host.StartAsync();
        var jobId = await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<HonouringJob>();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(15));

        await host.StopAsync();

        var job = await host.Services.GetRequiredService<IStorageProvider>().GetJobByIdAsync(jobId);
        job!.Status.Should().Be(JobStatus.Scheduled);
        job.Attempts.Should().Be(0);
    }

    /// <summary>N2: a job that ignores cancellation does not hold StopAsync beyond the bounded grace.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task JobIgnoringCancellation_DoesNotBlockStopAsyncForever()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = BuildHost(started);
        await host.StartAsync();
        await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<StubbornJob>();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(15));

        var sw = Stopwatch.StartNew();
        await host.StopAsync();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20));
    }

    /// <summary>N3: with no active job, StopAsync adds no wait.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task StopAsync_WithNoActiveJobs_ReturnsPromptly()
    {
        using var host = BuildHost(new TaskCompletionSource<bool>());
        await host.StartAsync();

        var sw = Stopwatch.StartNew();
        await host.StopAsync();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    /// <summary>Registers the provider under test.</summary>
    /// <param name="services">The service collection.</param>
    protected abstract void AddStorage(IServiceCollection services);

    private IHost BuildHost(TaskCompletionSource<bool> started) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                AddStorage(services);
                services.AddNexJob(opt =>
                {
                    opt.PollingInterval = TimeSpan.FromMilliseconds(100);
                    opt.ShutdownTimeout = TimeSpan.FromSeconds(1);
                });
                services.AddTransient(_ => new HonouringJob(started));
                services.AddTransient(_ => new StubbornJob(started));
            })
            .Build();

    private sealed class HonouringJob(TaskCompletionSource<bool> started) : IJob
    {
        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            started.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    private sealed class StubbornJob(TaskCompletionSource<bool> started) : IJob
    {
        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            started.TrySetResult(true);
            await Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None);
        }
    }
}

/// <summary>In-memory variant of <see cref="ShutdownRequeueEndToEndTests"/>.</summary>
public sealed class InMemoryShutdownRequeueEndToEndTests : ShutdownRequeueEndToEndTests
{
    /// <inheritdoc/>
    protected override void AddStorage(IServiceCollection services)
    {
    }
}

/// <summary>PostgreSQL variant of <see cref="ShutdownRequeueEndToEndTests"/>.</summary>
public sealed class PostgresShutdownRequeueEndToEndTests(PostgresFixture fixture)
    : ShutdownRequeueEndToEndTests, IClassFixture<PostgresFixture>
{
    /// <inheritdoc/>
    protected override void AddStorage(IServiceCollection services) =>
        services.AddNexJobPostgres(fixture.Container.GetConnectionString());
}

/// <summary>SQL Server variant of <see cref="ShutdownRequeueEndToEndTests"/>.</summary>
public sealed class SqlServerShutdownRequeueEndToEndTests(SqlServerFixture fixture)
    : ShutdownRequeueEndToEndTests, IClassFixture<SqlServerFixture>
{
    /// <inheritdoc/>
    protected override void AddStorage(IServiceCollection services) =>
        services.AddNexJobSqlServer(fixture.Container.GetConnectionString());
}

/// <summary>MongoDB variant of <see cref="ShutdownRequeueEndToEndTests"/>.</summary>
public sealed class MongoShutdownRequeueEndToEndTests(MongoFixture fixture)
    : ShutdownRequeueEndToEndTests, IClassFixture<MongoFixture>
{
    /// <inheritdoc/>
    protected override void AddStorage(IServiceCollection services) =>
        services.AddNexJobMongoDB(fixture.Container.GetConnectionString(), $"nexjob_shut_{Guid.NewGuid():N}");
}

/// <summary>Redis variant of <see cref="ShutdownRequeueEndToEndTests"/>.</summary>
public sealed class RedisShutdownRequeueEndToEndTests(RedisFixture fixture)
    : ShutdownRequeueEndToEndTests, IClassFixture<RedisFixture>
{
    /// <inheritdoc/>
    protected override void AddStorage(IServiceCollection services)
    {
        using (var mux = StackExchange.Redis.ConnectionMultiplexer.Connect(fixture.Container.GetConnectionString()))
        {
            mux.GetDatabase().Execute("FLUSHDB");
        }

        services.AddNexJobRedis(fixture.Container.GetConnectionString());
    }
}
