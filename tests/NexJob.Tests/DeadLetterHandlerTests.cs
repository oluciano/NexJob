using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Tests for <see cref="IDeadLetterHandler{TJob}"/> lifecycle and invocation.
/// Verifies that handlers run only on permanent failure, receive correct context,
/// and never crash the dispatcher even on handler exceptions.
/// </summary>
public sealed class DeadLetterHandlerTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    private static IHost BuildHost(
        Action<IServiceCollection> registerJobs,
        int workers = 1)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = workers;
                    opt.MaxAttempts = 2;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(50);
                    opt.RetryDelayFactory = _ => TimeSpan.FromMilliseconds(100); // Instant retry for tests
                });
                registerJobs(services);
            })
            .Build();
    }

    // ─── handler invocation on permanent failure ──────────────────────────────

    [Fact]
    public async Task Handler_InvokedOnPermanentFailure()
    {
        var handlerCalled = new TaskCompletionSource<bool>();
        var handler = new MockHandler(handlerCalled);

        using var host = BuildHost(s =>
        {
            s.AddTransient(_ => new AlwaysFailJob());
            s.AddTransient<IDeadLetterHandler<AlwaysFailJob>>(_ => handler);
        });
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<AlwaysFailJob>();

        // Wait for handler to be invoked (with increased timeout for second attempt)
        try
        {
            await handlerCalled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            handlerCalled.Task.IsCompletedSuccessfully.Should().BeTrue("handler should be invoked after all retries exhausted");
        }
        catch (TimeoutException)
        {
            // If handler didn't get invoked, check job status
            var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
            var job = await storage.GetJobByIdAsync(jobId);
            throw new TimeoutException($"Handler not invoked; job status is {job?.Status}");
        }

        await host.StopAsync();
    }

    [Fact]
    public async Task Handler_ReceivesCorrectJobRecord()
    {
        JobRecord? receivedJob = null;
        var handlerCalled = new TaskCompletionSource<bool>();
        var handler = new RecordingHandler(job =>
        {
            receivedJob = job;
            handlerCalled.SetResult(true);
        });

        using var host = BuildHost(s =>
        {
            s.AddTransient(_ => new AlwaysFailJob());
            s.AddTransient<IDeadLetterHandler<AlwaysFailJob>>(_ => handler);
        });
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<AlwaysFailJob>();

        await handlerCalled.Task.WaitAsync(TimeSpan.FromSeconds(3));

        receivedJob.Should().NotBeNull();
        receivedJob!.Id.Should().Be(jobId);
        receivedJob.Status.Should().Be(JobStatus.Failed);
        receivedJob.Attempts.Should().Be(2, "should have exhausted max attempts");

        await host.StopAsync();
    }

    [Fact]
    public async Task Handler_ReceivesLastException()
    {
        Exception? receivedException = null;
        var handlerCalled = new TaskCompletionSource<bool>();
        var handler = new ExceptionRecordingHandler(ex =>
        {
            receivedException = ex;
            handlerCalled.SetResult(true);
        });

        using var host = BuildHost(s =>
        {
            s.AddTransient(_ => new FailWithMessageJob("test error"));
            s.AddTransient<IDeadLetterHandler<FailWithMessageJob>>(_ => handler);
        });
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<FailWithMessageJob>();

        await handlerCalled.Task.WaitAsync(TimeSpan.FromSeconds(3));

        receivedException.Should().NotBeNull();
        receivedException!.Message.Should().Contain("test error");

        await host.StopAsync();
    }

    // ─── handler NOT invoked in non-failure cases ─────────────────────────────

    [Fact]
    public async Task Handler_NotInvokedOnSuccess()
    {
        var handlerCalled = false;

        using var host = BuildHost(s =>
        {
            s.AddTransient(_ => new SuccessJob());
            s.AddTransient<IDeadLetterHandler<SuccessJob>>(_ => new SuccessJobHandler(() => handlerCalled = true));
        });
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<SuccessJob>();

        // Give plenty of time for job to execute
        await Task.Delay(500);

        handlerCalled.Should().BeFalse("handler should not be invoked on successful job");

        await host.StopAsync();
    }

    [Fact]
    public async Task Handler_NotInvokedOnRetryableFailure()
    {
        var handlerCalled = false;

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = 1;
                    opt.MaxAttempts = 5; // Many retries
                    opt.PollingInterval = TimeSpan.FromMilliseconds(50);
                });
                services.AddTransient(_ => new AlwaysFailJob());
                services.AddTransient<IDeadLetterHandler<AlwaysFailJob>>(_ => new AlwaysFailJobHandler(() => handlerCalled = true));
            })
            .Build();
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<AlwaysFailJob>();

        // Wait a bit but not for all retries to complete
        await Task.Delay(200);

        handlerCalled.Should().BeFalse("handler should not be invoked while job has remaining retries");

        await host.StopAsync();
    }

    // ─── handler safety and error handling ─────────────────────────────────────

    [Fact]
    public async Task HandlerThrowingDoesNotCrashDispatcher()
    {
        var handlerException = new InvalidOperationException("handler error");

        using var host = BuildHost(s =>
        {
            s.AddTransient(_ => new AlwaysFailJob());
            s.AddTransient<IDeadLetterHandler<AlwaysFailJob>>(_ => new ThrowingHandler(handlerException));
        });
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<AlwaysFailJob>();

        // Give time for execution and handler invocation
        await Task.Delay(500);

        // Verify job reached failed state despite handler exception
        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        var job = await storage.GetJobByIdAsync(jobId);
        job!.Status.Should().Be(JobStatus.Failed, "job should be failed even if handler threw");

        // Dispatcher should still be healthy
        await host.StopAsync();
        // If we get here without an exception, the dispatcher stayed healthy
    }

    [Fact]
    public async Task NoHandlerRegisteredDoesNotThrow()
    {
        using var host = BuildHost(s =>
        {
            s.AddTransient(_ => new AlwaysFailJob());
            // Deliberately NOT registering a handler
        });
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<AlwaysFailJob>();

        // Should not throw even with no handler
        await Task.Delay(500);

        // Verify job was still persisted as failed
        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        var job = await storage.GetJobByIdAsync(jobId);
        job!.Status.Should().Be(JobStatus.Failed);

        await host.StopAsync();
    }

    // ─── type support ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handler_WorksForSimpleIJob()
    {
        var handlerCalled = new TaskCompletionSource<bool>();
        var handler = new SimpleJobHandler(handlerCalled);

        using var host = BuildHost(s =>
        {
            s.AddTransient(_ => new SimpleFailJob());
            s.AddTransient<IDeadLetterHandler<SimpleFailJob>>(_ => handler);
        });
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<SimpleFailJob>();

        var invoked = await handlerCalled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        invoked.Should().BeTrue("handler should work for IJob");

        await host.StopAsync();
    }

    [Fact]
    public async Task Handler_WorksForIJobWithInput()
    {
        var handlerCalled = new TaskCompletionSource<bool>();
        var handler = new InputJobHandler(handlerCalled);

        using var host = BuildHost(s =>
        {
            s.AddTransient(_ => new InputFailJob());
            s.AddTransient<IDeadLetterHandler<InputFailJob>>(_ => handler);
        });
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<InputFailJob, string>("test-input");

        var invoked = await handlerCalled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        invoked.Should().BeTrue("handler should work for IJob<T>");

        await host.StopAsync();
    }

    // ─── helper jobs ──────────────────────────────────────────────────────────

    private sealed class SuccessJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class AlwaysFailJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("job failed"));
    }

    private sealed class SimpleFailJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("simple job failed"));
    }

    private sealed class FailWithMessageJob : IJob
    {
        private readonly string _message;

        public FailWithMessageJob(string message) => _message = message;

        public Task ExecuteAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException(_message));
    }

    private sealed class InputFailJob : IJob<string>
    {
        public Task ExecuteAsync(string input, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("input job failed"));
    }

    // ─── handler implementations ──────────────────────────────────────────────

    private sealed class MockHandler : IDeadLetterHandler<AlwaysFailJob>
    {
        private readonly TaskCompletionSource<bool> _tcs;

        public MockHandler(TaskCompletionSource<bool> tcs) => _tcs = tcs;

        public Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken)
        {
            _tcs.TrySetResult(true);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHandler : IDeadLetterHandler<AlwaysFailJob>
    {
        private readonly Action<JobRecord> _recordJob;

        public RecordingHandler(Action<JobRecord> recordJob) => _recordJob = recordJob;

        public Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken)
        {
            _recordJob(failedJob);
            return Task.CompletedTask;
        }
    }

    private sealed class ExceptionRecordingHandler : IDeadLetterHandler<FailWithMessageJob>
    {
        private readonly Action<Exception> _recordException;

        public ExceptionRecordingHandler(Action<Exception> recordException) => _recordException = recordException;

        public Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken)
        {
            _recordException(lastException);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : IDeadLetterHandler<AlwaysFailJob>
    {
        private readonly Exception _toThrow;

        public ThrowingHandler(Exception toThrow) => _toThrow = toThrow;

        public Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken) =>
            Task.FromException(_toThrow);
    }

    private sealed class SimpleJobHandler : IDeadLetterHandler<SimpleFailJob>
    {
        private readonly TaskCompletionSource<bool> _tcs;

        public SimpleJobHandler(TaskCompletionSource<bool> tcs) => _tcs = tcs;

        public Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken)
        {
            _tcs.TrySetResult(true);
            return Task.CompletedTask;
        }
    }

    private sealed class InputJobHandler : IDeadLetterHandler<InputFailJob>
    {
        private readonly TaskCompletionSource<bool> _tcs;

        public InputJobHandler(TaskCompletionSource<bool> tcs) => _tcs = tcs;

        public Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken)
        {
            _tcs.TrySetResult(true);
            return Task.CompletedTask;
        }
    }

    private sealed class SuccessJobHandler : IDeadLetterHandler<SuccessJob>
    {
        private readonly Action _onHandle;

        public SuccessJobHandler(Action onHandle) => _onHandle = onHandle;

        public Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken)
        {
            _onHandle();
            return Task.CompletedTask;
        }
    }

    private sealed class AlwaysFailJobHandler : IDeadLetterHandler<AlwaysFailJob>
    {
        private readonly Action _onHandle;

        public AlwaysFailJobHandler(Action onHandle) => _onHandle = onHandle;

        public Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken)
        {
            _onHandle();
            return Task.CompletedTask;
        }
    }
}
