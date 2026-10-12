using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Unit tests for <see cref="IJobExecutionFilter"/> middleware pipeline.
/// Tests cover filter invocation order, context population, exception handling,
/// and fast path when no filters are registered.
/// </summary>
public sealed class JobFilterTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    private static IHost BuildHost(
        Action<IServiceCollection> registerJobs,
        Action<IServiceCollection>? registerFilters = null,
        int workers = 2,
        int maxAttempts = 3)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = workers;
                    opt.MaxAttempts = maxAttempts;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(20);
                });
                registerJobs(services);
                registerFilters?.Invoke(services);
            })
            .Build();
    }

    // ─── test: filter invoked around job execution ─────────────────────────────

    [Fact]
    public async Task Filter_IsInvokedAroundJobExecution()
    {
        var invocations = new List<string>();
        var tcs = new TaskCompletionSource<bool>();

        var filter = new TrackingFilter(invocations);

        using var host = BuildHost(
            s => s.AddTransient(_ => new QuickSuccessJob(tcs)),
            s => s.AddSingleton<IJobExecutionFilter>(filter));
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<QuickSuccessJob, QuickInput>(new());

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);

        invocations.Should().ContainInOrder("before", "after");

        await host.StopAsync();
    }

    // ─── test: multiple filters execute in registration order ──────────────────

    [Fact]
    public async Task MultipleFilters_ExecuteInRegistrationOrder()
    {
        var order = new List<string>();
        var tcs = new TaskCompletionSource<bool>();

        using var host = BuildHost(
            s => s.AddTransient(_ => new QuickSuccessJob(tcs)),
            s =>
            {
                s.AddSingleton<IJobExecutionFilter>(new OrderTrackingFilter(order, "filter1"));
                s.AddSingleton<IJobExecutionFilter>(new OrderTrackingFilter(order, "filter2"));
                s.AddSingleton<IJobExecutionFilter>(new OrderTrackingFilter(order, "filter3"));
            });
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<QuickSuccessJob, QuickInput>(new());

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200);

        // Filters are chained in reverse order: each filter wraps the next
        // Registration: filter1, filter2, filter3
        // Pipeline: filter1 wraps (filter2 wraps (filter3 wraps job))
        // Execution: filter1_before -> filter2_before -> filter3_before -> job -> filter3_after -> filter2_after -> filter1_after
        order.Should().ContainInOrder(
            "filter1_before",
            "filter2_before",
            "filter3_before",
            "filter3_after",
            "filter2_after",
            "filter1_after");

        await host.StopAsync();
    }

    // ─── test: filter receives context and can inspect job outcome ───────────

    [Fact]
    public async Task Filter_ReceivesJobRecord_InContext()
    {
        JobRecord? recordSeen = null;
        var tcs = new TaskCompletionSource<bool>();

        var filter = new ContextInspectFilter(ctx => recordSeen = ctx.Job);

        using var host = BuildHost(
            s => s.AddTransient(_ => new QuickSuccessJob(tcs)),
            s => s.AddSingleton<IJobExecutionFilter>(filter));
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<QuickSuccessJob, QuickInput>(new());

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200);

        recordSeen.Should().NotBeNull();
        recordSeen?.JobType.Should().Contain(nameof(QuickSuccessJob));

        await host.StopAsync();
    }

    // ─── test: filter is notified when job throws ──────────────────────────

    [Fact]
    public async Task Filter_IsInvokedEvenWhenJobThrows()
    {
        bool filterWasInvoked = false;
        var tcs = new TaskCompletionSource<bool>();
        var expectedEx = new InvalidOperationException("test failure");

        var filter = new InvocationTrackingFilter(() => filterWasInvoked = true);

        using var host = BuildHost(
            s => s.AddTransient(_ => new QuickFailureJob(tcs, expectedEx)),
            s => s.AddSingleton<IJobExecutionFilter>(filter),
            workers: 1);
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<QuickFailureJob, QuickInput>(new());

        // Wait for it to fail at least once (first attempt)
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(500);

        filterWasInvoked.Should().BeTrue("filter should be invoked even when job fails");

        await host.StopAsync();
    }

    // ─── test: filter that throws causes job failure (retry) ────────────────

    [Fact]
    public async Task Filter_ThatThrows_CausesJobFailure()
    {
        var tcs = new TaskCompletionSource<bool>();
        bool filterThrew = false;

        var throwingFilter = new ThrowingFilter(() => filterThrew = true);

        using var host = BuildHost(
            s => s.AddTransient(_ => new QuickSuccessJob(tcs)),
            s => s.AddSingleton<IJobExecutionFilter>(throwingFilter),
            maxAttempts: 2,
            workers: 1);
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<QuickSuccessJob, QuickInput>(new());

        // Wait for the job to execute and filter to throw
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200);

        // Filter threw, so job should be marked as failed
        filterThrew.Should().BeTrue();

        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        var metrics = await storage.GetMetricsAsync();
        // Job succeeded at execution but filter exception caused retry scheduling
        metrics.Succeeded.Should().Be(0, "job did not succeed because filter threw");

        await host.StopAsync();
    }

    // ─── test: filter can resolve scoped services ────────────────────────────

    [Fact]
    public async Task Filter_CanResolveScopedServices()
    {
        var tcs = new TaskCompletionSource<bool>();
        var filterInstance = new ScopedServiceAccessFilter();

        using var host = BuildHost(
            s =>
            {
                s.AddScoped<ScopedTestService>();
                s.AddTransient(_ => new QuickSuccessJob(tcs));
            },
            s => s.AddSingleton<IJobExecutionFilter>(filterInstance));
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<QuickSuccessJob, QuickInput>(new());

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);

        filterInstance.ResolvedException.Should().BeNull("filter should resolve the scoped service successfully");

        await host.StopAsync();
    }

    // ─── test: no filters registered — behavior identical to before ──────────

    [Fact]
    public async Task NoFilters_JobExecutesNormally()
    {
        var tcs = new TaskCompletionSource<bool>();

        using var host = BuildHost(s => s.AddTransient(_ => new QuickSuccessJob(tcs)));
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<QuickSuccessJob, QuickInput>(new());

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();

        // Behavior changed in v5.10: wait until the dispatcher stored Succeeded instead of a fixed 50 ms (#371).
        await TestWait.SucceededAsync(storage, 1);
        var metrics = await storage.GetMetricsAsync();
        metrics.Succeeded.Should().Be(1);

        await host.StopAsync();
    }

    // ─── test: filter can short-circuit by not calling next ───────────────────

    [Fact]
    public async Task Filter_CanShortCircuit_ByNotCallingNext()
    {
        var tcs = new TaskCompletionSource<bool>();
        bool jobExecuted = false;

        var job = new ShortCircuitTestJob(() => jobExecuted = true, tcs);

        using var host = BuildHost(
            s => s.AddTransient(_ => job),
            s => s.AddSingleton<IJobExecutionFilter>(new ShortCircuitFilter()));
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<ShortCircuitTestJob, QuickInput>(new());

        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();

        // Behavior changed in v5.10: wait until the dispatcher stored Succeeded instead of a fixed 1000 ms (#371).
        await TestWait.SucceededAsync(storage, 1);

        jobExecuted.Should().BeFalse("filter short-circuited and job should not execute");

        // Job should be marked as succeeded because filter didn't call next
        // but completed successfully (no exception thrown)
        var metrics = await storage.GetMetricsAsync();
        metrics.Succeeded.Should().Be(1);

        await host.StopAsync();
    }

    // ─── filter implementations ────────────────────────────────────────────────

    private sealed class TrackingFilter : IJobExecutionFilter
    {
        private readonly List<string> _invocations;

        public TrackingFilter(List<string> invocations) => _invocations = invocations;

        public async Task OnExecutingAsync(JobExecutingContext context, JobExecutionDelegate next, CancellationToken ct)
        {
            _invocations.Add("before");
            await next(ct).ConfigureAwait(false);
            _invocations.Add("after");
        }
    }

    private sealed class OrderTrackingFilter : IJobExecutionFilter
    {
        private readonly List<string> _order;
        private readonly string _name;

        public OrderTrackingFilter(List<string> order, string name)
        {
            _order = order;
            _name = name;
        }

        public async Task OnExecutingAsync(JobExecutingContext context, JobExecutionDelegate next, CancellationToken ct)
        {
            _order.Add($"{_name}_before");
            await next(ct).ConfigureAwait(false);
            _order.Add($"{_name}_after");
        }
    }

    private sealed class ContextInspectFilter : IJobExecutionFilter
    {
        private readonly Action<JobExecutingContext> _onComplete;

        public ContextInspectFilter(Action<JobExecutingContext> onComplete) => _onComplete = onComplete;

        public async Task OnExecutingAsync(JobExecutingContext context, JobExecutionDelegate next, CancellationToken ct)
        {
            await next(ct).ConfigureAwait(false);
            _onComplete(context);
        }
    }

    private sealed class InvocationTrackingFilter : IJobExecutionFilter
    {
        private readonly Action _onInvoked;

        public InvocationTrackingFilter(Action onInvoked) => _onInvoked = onInvoked;

        public async Task OnExecutingAsync(JobExecutingContext context, JobExecutionDelegate next, CancellationToken ct)
        {
            _onInvoked();
            try
            {
                await next(ct).ConfigureAwait(false);
            }
            catch
            {
                // swallow so test doesn't see the exception
            }
        }
    }

    private sealed class ThrowingFilter : IJobExecutionFilter
    {
        private readonly Action? _onThrow;

        public ThrowingFilter(Action? onThrow = null) => _onThrow = onThrow;

        public async Task OnExecutingAsync(JobExecutingContext context, JobExecutionDelegate next, CancellationToken ct)
        {
            await next(ct).ConfigureAwait(false);
            _onThrow?.Invoke();
            throw new InvalidOperationException("Filter intentionally throwing");
        }
    }

    private sealed class ScopedServiceAccessFilter : IJobExecutionFilter
    {
        public Exception? ResolvedException { get; private set; }

        public async Task OnExecutingAsync(JobExecutingContext context, JobExecutionDelegate next, CancellationToken ct)
        {
            try
            {
                _ = context.Services.GetRequiredService<ScopedTestService>();
                await next(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ResolvedException = ex;
                throw;
            }
        }
    }

    private sealed class ShortCircuitFilter : IJobExecutionFilter
    {
        public Task OnExecutingAsync(JobExecutingContext context, JobExecutionDelegate next, CancellationToken ct)
        {
            // Don't call next - short-circuit the pipeline
            return Task.CompletedTask;
        }
    }

    // ─── issue #475: the outcome is readable inside the filter ───────────────

    /// <summary>N1: a filter reads Succeeded = true and no Exception right after next returns.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Filter_ReadsSucceeded_AfterNext_WhenJobSucceeds()
    {
        var seen = new TaskCompletionSource<(bool Succeeded, Exception? Exception)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = BuildHost(
            s => s.AddTransient(_ => new QuickSuccessJob(new TaskCompletionSource<bool>())),
            s => s.AddSingleton<IJobExecutionFilter>(new OutcomeFilter(seen)));
        await host.StartAsync();

        await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<QuickSuccessJob, QuickInput>(new());

        var outcome = await seen.Task.WaitAsync(TimeSpan.FromSeconds(10));
        outcome.Succeeded.Should().BeTrue();
        outcome.Exception.Should().BeNull();
        await host.StopAsync();
    }

    /// <summary>N2: when the job throws, the filter reads the exception and Succeeded = false in its catch, and the job still fails normally.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Filter_ReadsException_InCatch_WhenJobThrows_AndJobStillFails()
    {
        var seen = new TaskCompletionSource<(bool Succeeded, Exception? Exception)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = BuildHost(
            s => s.AddTransient<ThrowingJob>(),
            s => s.AddSingleton<IJobExecutionFilter>(new OutcomeFilter(seen)),
            maxAttempts: 1);
        await host.StartAsync();

        var jobId = await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<ThrowingJob, QuickInput>(new());

        var outcome = await seen.Task.WaitAsync(TimeSpan.FromSeconds(10));
        outcome.Succeeded.Should().BeFalse();
        outcome.Exception.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("filter-outcome-boom");

        var storage = host.Services.GetRequiredService<IStorageProvider>();
        JobStatus? status = null;
        for (var i = 0; i < 200 && status != JobStatus.Failed; i++)
        {
            status = (await storage.GetJobByIdAsync(jobId))?.Status;
            await Task.Delay(25);
        }

        status.Should().Be(JobStatus.Failed, "rethrowing from the filter keeps the normal retry and dead-letter flow");
        await host.StopAsync();
    }

    /// <summary>N3: before calling next the outcome is still unset, and an outer filter sees the outcome of the inner filter and the job.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Filter_OutcomeIsUnsetBeforeNext_AndOuterFilterSeesIt()
    {
        var inner = new TaskCompletionSource<(bool Succeeded, Exception? Exception)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var outer = new TaskCompletionSource<(bool Succeeded, Exception? Exception)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var before = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = BuildHost(
            s => s.AddTransient(_ => new QuickSuccessJob(new TaskCompletionSource<bool>())),
            s =>
            {
                s.AddSingleton<IJobExecutionFilter>(new OutcomeFilter(outer, before));
                s.AddSingleton<IJobExecutionFilter>(new OutcomeFilter(inner));
            });
        await host.StartAsync();

        await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<QuickSuccessJob, QuickInput>(new());

        (await before.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeFalse("nothing has run before next");
        (await inner.Task.WaitAsync(TimeSpan.FromSeconds(10))).Succeeded.Should().BeTrue();
        (await outer.Task.WaitAsync(TimeSpan.FromSeconds(10))).Succeeded.Should().BeTrue();
        await host.StopAsync();
    }

    // ─── issue #475: a filter follows its registered lifetime ──────────────────

    /// <summary>N1: a Transient filter is built once per job, not once for every job.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task TransientFilter_IsBuiltOncePerJob()
    {
        var built = new ConstructionCounter();
        await RunJobsAsync(3, s => s.AddTransient<IJobExecutionFilter>(_ => new CountingFilter(built)));

        built.Count.Should().Be(3);
    }

    /// <summary>N2: a Singleton filter is still one instance shared by every job.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task SingletonFilter_IsBuiltOnce_ForEveryJob()
    {
        var built = new ConstructionCounter();
        await RunJobsAsync(3, s => s.AddSingleton<IJobExecutionFilter>(_ => new CountingFilter(built)));

        built.Count.Should().Be(1);
    }

    /// <summary>N3: a Scoped filter gets the scope of its own job, so a scoped service is not shared between jobs.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ScopedFilter_GetsOneScopePerJob()
    {
        var built = new ConstructionCounter();
        await RunJobsAsync(3, s => s.AddScoped<IJobExecutionFilter>(_ => new CountingFilter(built)));

        built.Count.Should().Be(3);
    }

    private static async Task RunJobsAsync(int jobs, Action<IServiceCollection> registerFilter)
    {
        var done = new CountdownEvent(jobs);
        using var host = BuildHost(
            s => s.AddTransient(_ => new CountdownJob(done)),
            registerFilter,
            workers: 1);
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();
        for (var i = 0; i < jobs; i++)
        {
            await scheduler.EnqueueAsync<CountdownJob, QuickInput>(new());
        }

        await Task.Run(() => done.Wait(TimeSpan.FromSeconds(15))).WaitAsync(TimeSpan.FromSeconds(20));
        await host.StopAsync();
    }

    private sealed class ConstructionCounter
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Increment() => Interlocked.Increment(ref _count);
    }

    private sealed class CountingFilter : IJobExecutionFilter
    {
        public CountingFilter(ConstructionCounter counter) => counter.Increment();

        public Task OnExecutingAsync(JobExecutingContext context, JobExecutionDelegate next, CancellationToken ct) => next(ct);
    }

    private sealed class OutcomeFilter(
        TaskCompletionSource<(bool Succeeded, Exception? Exception)> after,
        TaskCompletionSource<bool>? before = null) : IJobExecutionFilter
    {
        public async Task OnExecutingAsync(JobExecutingContext context, JobExecutionDelegate next, CancellationToken ct)
        {
            before?.TrySetResult(context.Succeeded);
            try
            {
                await next(ct);
                after.TrySetResult((context.Succeeded, context.Exception));
            }
            catch
            {
                after.TrySetResult((context.Succeeded, context.Exception));
                throw;
            }
        }
    }

    public sealed class ThrowingJob : IJob<QuickInput>
    {
        public Task ExecuteAsync(QuickInput input, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("filter-outcome-boom");
    }

    public sealed class CountdownJob(CountdownEvent done) : IJob<QuickInput>
    {
        public Task ExecuteAsync(QuickInput input, CancellationToken cancellationToken)
        {
            done.Signal();
            return Task.CompletedTask;
        }
    }

    // ─── job implementations ──────────────────────────────────────────────────

    public sealed class QuickSuccessJob : IJob<QuickInput>
    {
        private readonly TaskCompletionSource<bool> _tcs;

        public QuickSuccessJob(TaskCompletionSource<bool> tcs) => _tcs = tcs;

        public Task ExecuteAsync(QuickInput input, CancellationToken cancellationToken)
        {
            _tcs.SetResult(true);
            return Task.CompletedTask;
        }
    }

    public sealed class QuickFailureJob : IJob<QuickInput>
    {
        private readonly TaskCompletionSource<bool> _tcs;
        private readonly Exception _exception;

        public QuickFailureJob(TaskCompletionSource<bool> tcs, Exception exception)
        {
            _tcs = tcs;
            _exception = exception;
        }

        public Task ExecuteAsync(QuickInput input, CancellationToken cancellationToken)
        {
            _tcs.SetResult(false);
            throw _exception;
        }
    }

    public sealed class TrackingAttemptJob : IJob<QuickInput>
    {
        private readonly TaskCompletionSource<bool> _tcs;
        private readonly Action _onAttempt;

        public TrackingAttemptJob(TaskCompletionSource<bool> tcs, Action onAttempt)
        {
            _tcs = tcs;
            _onAttempt = onAttempt;
        }

        public Task ExecuteAsync(QuickInput input, CancellationToken cancellationToken)
        {
            _onAttempt();
            _tcs.SetResult(false);
            throw new InvalidOperationException("Intentional failure");
        }
    }

    public sealed class ShortCircuitTestJob : IJob<QuickInput>
    {
        private readonly Action _onExecute;
        private readonly TaskCompletionSource<bool> _tcs;

        public ShortCircuitTestJob(Action onExecute, TaskCompletionSource<bool> tcs)
        {
            _onExecute = onExecute;
            _tcs = tcs;
        }

        public Task ExecuteAsync(QuickInput input, CancellationToken cancellationToken)
        {
            _onExecute();
            _tcs.SetResult(true);
            return Task.CompletedTask;
        }
    }

    private sealed class ScopedTestService
    {
    }
}
