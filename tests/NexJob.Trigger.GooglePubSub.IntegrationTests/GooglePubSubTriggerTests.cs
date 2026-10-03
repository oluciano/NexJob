using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace NexJob.Trigger.GooglePubSub.IntegrationTests;

/// <summary>
/// The Google Pub/Sub trigger against a real emulator: a message becomes a job and is acknowledged, a failed enqueue is
/// nacked and delivered again, and a message without a job type is handled without a hot redelivery loop.
/// </summary>
public sealed class GooglePubSubTriggerTests : IClassFixture<PubSubEmulatorFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly PubSubEmulatorFixture _fixture;

    public GooglePubSubTriggerTests(PubSubEmulatorFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task HappyPath_MessageBecomesAJob_IsAcknowledged_AndKeepsTheTraceParent()
    {
        // N1 (Positive)
        var (topic, subscription) = await _fixture.CreateTopicAndSubscriptionAsync();
        using var host = await StartAsync(subscription, jobType: typeof(TestJob).AssemblyQualifiedName);
        const string traceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

        var messageId = await _fixture.PublishAsync(topic, "hello", new Dictionary<string, string> { ["traceparent"] = traceParent });

        var job = await WaitForJobAsync(host, messageId);
        job.Should().NotBeNull("the message is consumed and enqueued");
        job!.JobType.Should().Be(typeof(TestJob).AssemblyQualifiedName);
        job.TraceParent.Should().Be(traceParent);

        await host.StopAsync();
        (await _fixture.PullPendingCountAsync(subscription)).Should().Be(0, "an enqueued message is acknowledged, not redelivered");
    }

    [Fact]
    public async Task JobTypeFromTheMessageAttribute_IsUsedWhenNoneIsConfigured()
    {
        // N1 (Positive): the nexjob.job_type attribute selects the job.
        var (topic, subscription) = await _fixture.CreateTopicAndSubscriptionAsync();
        using var host = await StartAsync(subscription, jobType: null);

        var messageId = await _fixture.PublishAsync(
            topic,
            "hello",
            new Dictionary<string, string> { ["nexjob.job_type"] = typeof(TestJob).AssemblyQualifiedName! });

        var job = await WaitForJobAsync(host, messageId);
        job.Should().NotBeNull();
        job!.JobType.Should().Be(typeof(TestJob).AssemblyQualifiedName);
    }

    [Fact]
    public async Task EnqueueFailure_IsNacked_AndTheMessageIsDeliveredAgain()
    {
        // N2 (Negative): the first enqueue fails, the message must come back and then be enqueued.
        var (topic, subscription) = await _fixture.CreateTopicAndSubscriptionAsync();
        var calls = 0;
        var scheduler = new Mock<IScheduler>();
        scheduler
            .Setup(s => s.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .Returns<JobRecord, DuplicatePolicy, CancellationToken>((job, _, _) =>
                Interlocked.Increment(ref calls) == 1
                    ? throw new InvalidOperationException("Simulated storage failure")
                    : Task.FromResult(job.Id));

        using var host = await StartAsync(subscription, typeof(TestJob).AssemblyQualifiedName, services => services.AddSingleton(scheduler.Object));
        await _fixture.PublishAsync(topic, "hello");

        var redelivered = await WaitUntilAsync(() => Volatile.Read(ref calls) >= 2);

        redelivered.Should().BeTrue("a nacked message is delivered again");
        await host.StopAsync();
        (await _fixture.PullPendingCountAsync(subscription)).Should().Be(0, "once the retry succeeded the message is acknowledged");
    }

    [Fact]
    public async Task MessageWithoutAJobType_IsNotRedeliveredInAHotLoop()
    {
        // N3 (Invalid input): no nexjob.job_type attribute and no JobType configured.
        var (topic, subscription) = await _fixture.CreateTopicAndSubscriptionAsync();
        var attempts = 0;
        var scheduler = new Mock<IScheduler>();
        scheduler
            .Setup(s => s.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .Returns<JobRecord, DuplicatePolicy, CancellationToken>((job, _, _) =>
            {
                Interlocked.Increment(ref attempts);
                return Task.FromResult(job.Id);
            });
        var warnings = new WarningCounter();

        using var host = await StartAsync(subscription, jobType: null, services =>
        {
            services.AddSingleton(scheduler.Object);
            services.AddSingleton<ILoggerProvider>(warnings);
        });
        await _fixture.PublishAsync(topic, "no type");

        await Task.Delay(TimeSpan.FromSeconds(5));
        await host.StopAsync();

        attempts.Should().Be(0, "a message without a job type never becomes a job");
        warnings.Count.Should().BeLessThan(10, "a message that can never be handled must not be redelivered in a tight loop");
    }

    private async Task<IHost> StartAsync(
        Google.Cloud.PubSub.V1.SubscriptionName subscription,
        string? jobType,
        Action<IServiceCollection>? configure = null)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.ClearProviders())
            .ConfigureServices(services =>
            {
                services.AddNexJob();
                configure?.Invoke(services); // after AddNexJob, so a replacement scheduler wins
                services.AddNexJobGooglePubSubTrigger(options =>
                {
                    options.ProjectId = PubSubEmulatorFixture.ProjectId;
                    options.SubscriptionId = subscription.SubscriptionId;
                    options.EmulatorHost = _fixture.EmulatorHost;
                    options.JobType = jobType;
                });
            })
            .Build();
        await host.StartAsync();
        return host;
    }

    private static async Task<JobRecord?> WaitForJobAsync(IHost host, string messageId)
    {
        var scheduler = host.Services.GetRequiredService<IScheduler>();
        JobRecord? found = null;
        await WaitUntilAsync(async () =>
        {
            found = (await scheduler.GetJobsByTagAsync("trigger:googlepubsub")).FirstOrDefault(j => j.IdempotencyKey == messageId);
            return found is not null;
        });
        return found;
    }

    private static Task<bool> WaitUntilAsync(Func<bool> condition) => WaitUntilAsync(() => Task.FromResult(condition()));

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    private sealed class TestJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class WarningCounter : ILoggerProvider
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public ILogger CreateLogger(string categoryName) => new Counter(this);

        public void Dispose()
        {
        }

        private sealed class Counter : ILogger
        {
            private readonly WarningCounter _owner;

            public Counter(WarningCounter owner) => _owner = owner;

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Warning)
                {
                    Interlocked.Increment(ref _owner._count);
                }
            }
        }
    }
}
