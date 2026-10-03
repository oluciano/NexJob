using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace NexJob.Internal.Tests;

public sealed class DeadLetterForwarderTests
{
    // ─── N1: forwarders run ───────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_ForwarderApplies_ForwardsTheJobOnce()
    {
        var job = MakeJob();
        var exception = new InvalidOperationException("failure");
        var forwarder = new RecordingForwarder();
        var dispatcher = MakeDispatcher(s => s.AddSingleton<IDeadLetterForwarder>(forwarder));

        await dispatcher.DispatchAsync(job, exception);

        forwarder.Forwarded.Should().ContainSingle().Which.Should().BeSameAs(job);
        forwarder.LastException.Should().BeSameAs(exception);
    }

    [Fact]
    public async Task DispatchAsync_JobTypeHasItsOwnHandler_StillForwards()
    {
        var job = MakeJob();
        var handler = new RecordingHandler();
        var forwarder = new RecordingForwarder();
        var dispatcher = MakeDispatcher(s =>
        {
            s.AddSingleton<IDeadLetterHandler<TestJob>>(handler);
            s.AddSingleton<IDeadLetterForwarder>(forwarder);
        });

        await dispatcher.DispatchAsync(job, new InvalidOperationException("failure"));

        handler.Calls.Should().Be(1, "the typed handler keeps working as before");
        forwarder.Forwarded.Should().HaveCount(1, "a handler of the job type must not block the forwarder");
    }

    [Fact]
    public async Task DispatchAsync_SeveralForwarders_CallsEachOfThem()
    {
        var first = new RecordingForwarder();
        var second = new RecordingForwarder();
        var dispatcher = MakeDispatcher(s =>
        {
            s.AddSingleton<IDeadLetterForwarder>(first);
            s.AddSingleton<IDeadLetterForwarder>(second);
        });

        await dispatcher.DispatchAsync(MakeJob(), new InvalidOperationException("failure"));

        first.Forwarded.Should().HaveCount(1);
        second.Forwarded.Should().HaveCount(1);
    }

    // ─── N2: filtering and isolation ──────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_ForwarderDoesNotApply_IsNotCalled()
    {
        var forwarder = new RecordingForwarder { Applies = false };
        var dispatcher = MakeDispatcher(s => s.AddSingleton<IDeadLetterForwarder>(forwarder));

        await dispatcher.DispatchAsync(MakeJob(), new InvalidOperationException("failure"));

        forwarder.Forwarded.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchAsync_ForwarderThrows_DoesNotStopTheOthersOrTheHandler()
    {
        var handler = new RecordingHandler();
        var failing = new RecordingForwarder { ThrowOnForward = true };
        var healthy = new RecordingForwarder();
        var dispatcher = MakeDispatcher(s =>
        {
            s.AddSingleton<IDeadLetterHandler<TestJob>>(handler);
            s.AddSingleton<IDeadLetterForwarder>(failing);
            s.AddSingleton<IDeadLetterForwarder>(healthy);
        });

        Func<Task> act = () => dispatcher.DispatchAsync(MakeJob(), new InvalidOperationException("failure"));

        await act.Should().NotThrowAsync("forwarder errors are swallowed and logged");
        handler.Calls.Should().Be(1);
        healthy.Forwarded.Should().HaveCount(1);
    }

    // ─── N3: boundaries ───────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_NoForwarderRegistered_IsNoOp()
    {
        var dispatcher = MakeDispatcher(_ => { });

        Func<Task> act = () => dispatcher.DispatchAsync(MakeJob(), new InvalidOperationException("failure"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DispatchAsync_JobTypeCannotBeResolved_StillForwards()
    {
        var forwarder = new RecordingForwarder();
        var dispatcher = MakeDispatcher(s => s.AddSingleton<IDeadLetterForwarder>(forwarder));
        var job = MakeJob("Missing.Namespace.GoneJob, Missing.Assembly");

        await dispatcher.DispatchAsync(job, new InvalidOperationException("failure"));

        forwarder.Forwarded.Should().HaveCount(1, "forwarding copies the message and does not need the job type");
    }

    [Fact]
    public async Task DispatchAsync_AppliesToThrows_IsIsolatedFromTheOtherForwarders()
    {
        var broken = new RecordingForwarder { ThrowOnAppliesTo = true };
        var healthy = new RecordingForwarder();
        var dispatcher = MakeDispatcher(s =>
        {
            s.AddSingleton<IDeadLetterForwarder>(broken);
            s.AddSingleton<IDeadLetterForwarder>(healthy);
        });

        Func<Task> act = () => dispatcher.DispatchAsync(MakeJob(), new InvalidOperationException("failure"));

        await act.Should().NotThrowAsync();
        healthy.Forwarded.Should().HaveCount(1);
    }

    // ─── Metrics ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_CountsForwardedAndFailedForwards_PerForwarder()
    {
        var measurements = new List<(string Instrument, string? Forwarder, long Value)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name is "nexjob.dead_letter.forwarded" or "nexjob.dead_letter.forward_failed")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string? forwarder = null;
            foreach (var tag in tags)
            {
                if (string.Equals(tag.Key, "nexjob.forwarder", StringComparison.Ordinal))
                {
                    forwarder = tag.Value as string;
                }
            }

            lock (measurements)
            {
                measurements.Add((instrument.Name, forwarder, value));
            }
        });
        listener.Start();

        var dispatcher = MakeDispatcher(s =>
        {
            s.AddSingleton<IDeadLetterForwarder>(new CountedOkForwarder());
            s.AddSingleton<IDeadLetterForwarder>(new CountedFailingForwarder());
        });

        await dispatcher.DispatchAsync(MakeJob(), new InvalidOperationException("failure"));

        lock (measurements)
        {
            measurements.Should().Contain(("nexjob.dead_letter.forwarded", nameof(CountedOkForwarder), 1L));
            measurements.Should().Contain(("nexjob.dead_letter.forward_failed", nameof(CountedFailingForwarder), 1L));
            measurements.Should().NotContain(m => m.Instrument == "nexjob.dead_letter.forwarded" && m.Forwarder == nameof(CountedFailingForwarder));
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static DefaultDeadLetterDispatcher MakeDispatcher(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        var provider = services.BuildServiceProvider();
        return new DefaultDeadLetterDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ILogger<DefaultDeadLetterDispatcher>>());
    }

    private static JobRecord MakeJob(string? jobType = null) => new()
    {
        Id = JobId.New(),
        JobType = jobType ?? typeof(TestJob).AssemblyQualifiedName!,
        InputType = typeof(NoInput).AssemblyQualifiedName!,
        InputJson = "{\"body\":\"as received\"}",
        Queue = "default",
        Attempts = 3,
        MaxAttempts = 3,
        CreatedAt = DateTimeOffset.UtcNow,
        Status = JobStatus.Failed,
    };

    private sealed class TestJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingHandler : IDeadLetterHandler<TestJob>
    {
        public int Calls { get; private set; }

        public Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingForwarder : IDeadLetterForwarder
    {
        public List<JobRecord> Forwarded { get; } = [];

        public Exception? LastException { get; private set; }

        public bool Applies { get; init; } = true;

        public bool ThrowOnForward { get; init; }

        public bool ThrowOnAppliesTo { get; init; }

        public bool AppliesTo(JobRecord failedJob) =>
            ThrowOnAppliesTo ? throw new InvalidOperationException("AppliesTo failed") : Applies;

        public Task ForwardAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken)
        {
            if (ThrowOnForward)
            {
                throw new InvalidOperationException("forward failed");
            }

            Forwarded.Add(failedJob);
            LastException = lastException;
            return Task.CompletedTask;
        }
    }

    private sealed class CountedOkForwarder : IDeadLetterForwarder
    {
        public bool AppliesTo(JobRecord failedJob) => true;

        public Task ForwardAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CountedFailingForwarder : IDeadLetterForwarder
    {
        public bool AppliesTo(JobRecord failedJob) => true;

        public Task ForwardAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("forward failed");
    }
}
