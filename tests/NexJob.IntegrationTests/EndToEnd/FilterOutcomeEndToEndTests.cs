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
/// Issue #475 on a real provider: a filter reads the outcome of the job it wraps, a failing job still goes through
/// the normal failure flow when the filter rethrows, and a Transient filter is built once per job.
/// </summary>
public abstract class FilterOutcomeEndToEndTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    /// <summary>N1: after next, the filter sees Succeeded = true and no exception.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Filter_SeesSuccess_AfterNext()
    {
        var seen = new TaskCompletionSource<(bool Succeeded, Exception? Exception)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = BuildHost(s => s.AddSingleton<IJobExecutionFilter>(new OutcomeFilter(seen)));
        await host.StartAsync();

        await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<OkJob>();

        var outcome = await seen.Task.WaitAsync(Limit);
        outcome.Succeeded.Should().BeTrue();
        outcome.Exception.Should().BeNull();
        await host.StopAsync();
    }

    /// <summary>N2: a failing job shows its exception to the filter, and the stored job still ends Failed.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Filter_SeesException_AndJobStillFails()
    {
        var seen = new TaskCompletionSource<(bool Succeeded, Exception? Exception)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = BuildHost(s => s.AddSingleton<IJobExecutionFilter>(new OutcomeFilter(seen)));
        await host.StartAsync();

        var jobId = await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<BadJob>();

        var outcome = await seen.Task.WaitAsync(Limit);
        outcome.Succeeded.Should().BeFalse();
        outcome.Exception.Should().BeOfType<InvalidOperationException>();

        var storage = host.Services.GetRequiredService<IStorageProvider>();
        JobStatus? status = null;
        for (var i = 0; i < 400 && status != JobStatus.Failed; i++)
        {
            status = (await storage.GetJobByIdAsync(jobId))?.Status;
            await Task.Delay(50);
        }

        status.Should().Be(JobStatus.Failed);
        await host.StopAsync();
    }

    /// <summary>N3: a Transient filter is built once per job, and every one of them sees its own job succeed.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task TransientFilter_IsBuiltOncePerJob()
    {
        var built = 0;
        var done = new CountdownEvent(3);
        using var host = BuildHost(s => s.AddTransient<IJobExecutionFilter>(_ =>
        {
            Interlocked.Increment(ref built);
            return new SignalFilter(done);
        }));
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        for (var i = 0; i < 3; i++)
        {
            await scheduler.EnqueueAsync<OkJob>();
        }

        await Task.Run(() => done.Wait(Limit)).WaitAsync(Limit + TimeSpan.FromSeconds(5));
        Volatile.Read(ref built).Should().Be(3);
        await host.StopAsync();
    }

    /// <summary>Registers the provider under test.</summary>
    /// <param name="services">The service collection.</param>
    protected abstract void AddStorage(IServiceCollection services);

    private IHost BuildHost(Action<IServiceCollection> registerFilter) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                AddStorage(services);
                services.AddNexJob(opt =>
                {
                    opt.PollingInterval = TimeSpan.FromMilliseconds(50);
                    opt.MaxAttempts = 1;
                    opt.Workers = 1;
                });
                services.AddTransient<OkJob>();
                services.AddTransient<BadJob>();
                registerFilter(services);
            })
            .Build();

    private sealed class OkJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class BadJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("boom");
    }

    private sealed class OutcomeFilter(TaskCompletionSource<(bool Succeeded, Exception? Exception)> seen) : IJobExecutionFilter
    {
        public async Task OnExecutingAsync(JobExecutingContext context, JobExecutionDelegate next, CancellationToken ct)
        {
            try
            {
                await next(ct);
                seen.TrySetResult((context.Succeeded, context.Exception));
            }
            catch
            {
                seen.TrySetResult((context.Succeeded, context.Exception));
                throw;
            }
        }
    }

    private sealed class SignalFilter(CountdownEvent done) : IJobExecutionFilter
    {
        public async Task OnExecutingAsync(JobExecutingContext context, JobExecutionDelegate next, CancellationToken ct)
        {
            await next(ct);
            done.Signal();
        }
    }
}

/// <summary>In-memory variant of <see cref="FilterOutcomeEndToEndTests"/>.</summary>
public sealed class InMemoryFilterOutcomeEndToEndTests : FilterOutcomeEndToEndTests
{
    /// <inheritdoc/>
    protected override void AddStorage(IServiceCollection services)
    {
    }
}

/// <summary>PostgreSQL variant of <see cref="FilterOutcomeEndToEndTests"/>.</summary>
public sealed class PostgresFilterOutcomeEndToEndTests(PostgresFixture fixture)
    : FilterOutcomeEndToEndTests, IClassFixture<PostgresFixture>
{
    /// <inheritdoc/>
    protected override void AddStorage(IServiceCollection services) =>
        services.AddNexJobPostgres(fixture.Container.GetConnectionString());
}

/// <summary>SQL Server variant of <see cref="FilterOutcomeEndToEndTests"/>.</summary>
public sealed class SqlServerFilterOutcomeEndToEndTests(SqlServerFixture fixture)
    : FilterOutcomeEndToEndTests, IClassFixture<SqlServerFixture>
{
    /// <inheritdoc/>
    protected override void AddStorage(IServiceCollection services) =>
        services.AddNexJobSqlServer(fixture.Container.GetConnectionString());
}

/// <summary>MongoDB variant of <see cref="FilterOutcomeEndToEndTests"/>.</summary>
public sealed class MongoFilterOutcomeEndToEndTests(MongoFixture fixture)
    : FilterOutcomeEndToEndTests, IClassFixture<MongoFixture>
{
    /// <inheritdoc/>
    protected override void AddStorage(IServiceCollection services) =>
        services.AddNexJobMongoDB(fixture.Container.GetConnectionString(), $"nexjob_filter_{Guid.NewGuid():N}");
}

/// <summary>Redis variant of <see cref="FilterOutcomeEndToEndTests"/>.</summary>
public sealed class RedisFilterOutcomeEndToEndTests(RedisFixture fixture)
    : FilterOutcomeEndToEndTests, IClassFixture<RedisFixture>
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
