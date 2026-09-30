using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Postgres;
using Xunit;

namespace NexJob.IntegrationTests.EndToEnd;

/// <summary>
/// Regression for issue #237: jobs whose input serializes to <c>{}</c> must execute on PostgreSQL.
/// </summary>
public sealed class PostgresEmptyInputEndToEndTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public PostgresEmptyInputEndToEndTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Job_with_empty_record_input_executes_on_postgres()
    {
        var executed = new TaskCompletionSource<bool>();
        using var host = BuildHost(executed);
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<EmptyInputJob, EmptyInput>(new EmptyInput());

        (await executed.Task.WaitAsync(TimeSpan.FromSeconds(15))).Should().BeTrue();
        await host.StopAsync();
    }

    [Fact]
    public async Task Job_without_input_executes_on_postgres()
    {
        var executed = new TaskCompletionSource<bool>();
        using var host = BuildHost(executed);
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<NoInputJob>();

        (await executed.Task.WaitAsync(TimeSpan.FromSeconds(15))).Should().BeTrue();
        await host.StopAsync();
    }

    [Fact]
    public async Task Job_with_empty_input_reaches_its_body_even_when_the_body_throws_on_postgres()
    {
        var executed = new TaskCompletionSource<bool>();
        using var host = BuildHost(executed);
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<ThrowingEmptyInputJob, EmptyInput>(new EmptyInput());

        // Reaching the body proves the empty input was deserialized; the failure then comes from the job itself.
        (await ThrowingEmptyInputJob.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15))).Should().BeTrue();
        await host.StopAsync();
    }

    private IHost BuildHost(TaskCompletionSource<bool> executed) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJobPostgres(_fixture.Container.GetConnectionString());
                services.AddNexJob(opt =>
                {
                    opt.Workers = 1;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(50);
                    opt.MaxAttempts = 1;
                });
                services.AddTransient(_ => new EmptyInputJob(executed));
                services.AddTransient(_ => new NoInputJob(executed));
                services.AddTransient<ThrowingEmptyInputJob>();
            })
            .Build();
}

/// <summary>Input record with no properties; serializes to <c>{}</c>.</summary>
public sealed record EmptyInput;

/// <summary>Job with an empty-record input.</summary>
public sealed class EmptyInputJob(TaskCompletionSource<bool> signal) : IJob<EmptyInput>
{
    /// <inheritdoc/>
    public Task ExecuteAsync(EmptyInput input, CancellationToken cancellationToken)
    {
        signal.TrySetResult(true);
        return Task.CompletedTask;
    }
}

/// <summary>Job that takes no input at all.</summary>
public sealed class NoInputJob(TaskCompletionSource<bool> signal) : IJob
{
    /// <inheritdoc/>
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        signal.TrySetResult(true);
        return Task.CompletedTask;
    }
}

/// <summary>Job with an empty input whose body throws, proving the body was reached.</summary>
public sealed class ThrowingEmptyInputJob : IJob<EmptyInput>
{
    /// <summary>Completed once the job body has been entered.</summary>
    public static readonly TaskCompletionSource<bool> Reached = new();

    /// <inheritdoc/>
    public Task ExecuteAsync(EmptyInput input, CancellationToken cancellationToken)
    {
        Reached.TrySetResult(true);
        throw new InvalidOperationException("expected failure from the job body");
    }
}
