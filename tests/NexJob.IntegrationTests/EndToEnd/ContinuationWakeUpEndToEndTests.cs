using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.MongoDB;
using NexJob.Postgres;
using NexJob.Redis;
using NexJob.SqlServer;
using Xunit;

namespace NexJob.IntegrationTests.EndToEnd;

/// <summary>
/// Issue #495 on a real provider: a child promoted by its parent's success runs well before the polling interval,
/// with batch acknowledgment on and off.
/// </summary>
public abstract class ContinuationWakeUpEndToEndTests
{
    /// <summary>N1: the promoted child runs long before the 30 s polling interval.</summary>
    /// <param name="batchAck">Whether batch acknowledgment is enabled.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Child_RunsBeforePollingInterval_AfterParentSucceeds(bool batchAck)
    {
        var childRan = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = BuildHost(batchAck, gate, childRan);
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var parentId = await scheduler.EnqueueAsync<GatedParentJob>();
        await scheduler.ContinueWithAsync<ChildJob>(parentId);
        gate.SetResult(true);

        (await childRan.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
        await host.StopAsync();
    }

    /// <summary>N3: a parent without children succeeds and the next job is still picked up at once.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ParentWithoutChildren_Succeeds_AndNextJobStillRuns()
    {
        var childRan = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.SetResult(true);
        using var host = BuildHost(false, gate, childRan);
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        await scheduler.EnqueueAsync<GatedParentJob>();
        await scheduler.EnqueueAsync<ChildJob>();

        (await childRan.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
        await host.StopAsync();
    }

    /// <summary>Registers the provider under test.</summary>
    /// <param name="services">The service collection.</param>
    protected abstract void AddStorage(IServiceCollection services);

    private IHost BuildHost(bool batchAck, TaskCompletionSource<bool> gate, TaskCompletionSource<bool> childRan) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                AddStorage(services);
                services.AddNexJob(opt =>
                {
                    opt.PollingInterval = TimeSpan.FromSeconds(30);
                    opt.EnableBatchAcknowledgment = batchAck;
                });
                services.AddTransient(_ => new GatedParentJob(gate));
                services.AddTransient(_ => new ChildJob(childRan));
            })
            .Build();

    private sealed class GatedParentJob(TaskCompletionSource<bool> gate) : IJob
    {
        public async Task ExecuteAsync(CancellationToken cancellationToken) => await gate.Task.WaitAsync(cancellationToken);
    }

    private sealed class ChildJob(TaskCompletionSource<bool> ran) : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            ran.TrySetResult(true);
            return Task.CompletedTask;
        }
    }
}

/// <summary>PostgreSQL variant of <see cref="ContinuationWakeUpEndToEndTests"/>.</summary>
public sealed class PostgresContinuationWakeUpEndToEndTests(PostgresFixture fixture)
    : ContinuationWakeUpEndToEndTests, IClassFixture<PostgresFixture>
{
    /// <inheritdoc/>
    protected override void AddStorage(IServiceCollection services) =>
        services.AddNexJobPostgres(fixture.Container.GetConnectionString());
}

/// <summary>SQL Server variant of <see cref="ContinuationWakeUpEndToEndTests"/>.</summary>
public sealed class SqlServerContinuationWakeUpEndToEndTests(SqlServerFixture fixture)
    : ContinuationWakeUpEndToEndTests, IClassFixture<SqlServerFixture>
{
    /// <inheritdoc/>
    protected override void AddStorage(IServiceCollection services) =>
        services.AddNexJobSqlServer(fixture.Container.GetConnectionString());
}

/// <summary>MongoDB variant of <see cref="ContinuationWakeUpEndToEndTests"/>.</summary>
public sealed class MongoContinuationWakeUpEndToEndTests(MongoFixture fixture)
    : ContinuationWakeUpEndToEndTests, IClassFixture<MongoFixture>
{
    /// <inheritdoc/>
    protected override void AddStorage(IServiceCollection services) =>
        services.AddNexJobMongoDB(fixture.Container.GetConnectionString(), $"nexjob_wake_{Guid.NewGuid():N}");
}

/// <summary>Redis variant of <see cref="ContinuationWakeUpEndToEndTests"/>.</summary>
public sealed class RedisContinuationWakeUpEndToEndTests(RedisFixture fixture)
    : ContinuationWakeUpEndToEndTests, IClassFixture<RedisFixture>
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
