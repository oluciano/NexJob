using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob;
using NexJob.Internal;
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
        await Task.Delay(50);

        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
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

        await Task.Delay(1000);

        jobExecuted.Should().BeFalse("filter short-circuited and job should not execute");

        // Job should be marked as succeeded because filter didn't call next
        // but completed successfully (no exception thrown)
        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
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
