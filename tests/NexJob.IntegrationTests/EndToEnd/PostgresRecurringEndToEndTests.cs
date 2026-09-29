using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Postgres;
using Xunit;

namespace NexJob.IntegrationTests.EndToEnd;

/// <summary>
/// Regression for issue #234: a default (<c>SkipIfRunning</c>) recurring job must fire again after its
/// previous run finished, on PostgreSQL.
/// </summary>
public sealed class PostgresRecurringEndToEndTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public PostgresRecurringEndToEndTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Recurring_job_fires_again_after_the_previous_run_completed_on_postgres()
    {
        var counter = new RecurringTickCounter();
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJobPostgres(_fixture.Container.GetConnectionString());
                services.AddNexJob(opt =>
                {
                    opt.Workers = 1;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(100);
                    opt.AddRecurringJob<RecurringTickJob>("tick-234", "* * * * * *");
                });
                services.AddSingleton(counter);
                services.AddTransient<RecurringTickJob>();
            })
            .Build();

        await host.StartAsync();

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (counter.Value < 3 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
        }

        await host.StopAsync();

        counter.Value.Should().BeGreaterThanOrEqualTo(3, "an every-second recurring job must keep firing after each run completed");
    }
}

/// <summary>Counts executions of <see cref="RecurringTickJob"/>.</summary>
public sealed class RecurringTickCounter
{
    private int _value;

    /// <summary>Gets the number of executions so far.</summary>
    public int Value => Volatile.Read(ref _value);

    /// <summary>Records one execution.</summary>
    public void Increment() => Interlocked.Increment(ref _value);
}

/// <summary>No-input recurring job used by the end-to-end test.</summary>
public sealed class RecurringTickJob(RecurringTickCounter counter) : IJob
{
    /// <inheritdoc/>
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        counter.Increment();
        return Task.CompletedTask;
    }
}
