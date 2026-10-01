using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.RabbitMQ;
using NexJob.Storage;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// The dead-letter forwarding pattern documented in <c>docs/wiki/06-Retry-And-Dead-Letter.md</c>, run end to end:
/// a job that exhausts its retries is forwarded to a RabbitMQ exchange through the Outbox and stays Failed in NexJob.
/// </summary>
public sealed class DeadLetterForwardingTests
{
    [Fact]
    public async Task ExhaustedJobOnTheTargetQueue_IsForwardedWithTheOriginalBody_AndStaysFailed()
    {
        // N1 (Positive): the original message body arrives verbatim and the job is still Failed in the dashboard.
        var producer = new RecordingProducer();
        using var host = BuildHost(producer);
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();
        const string body = "{\"orderId\":42,\"note\":\"é, \\\"quoted\\\"\"}";

        var jobId = await scheduler.EnqueueAsync<FailingOrderJob, string>(body, queue: "orders");
        await producer.WaitForAsync(1);

        var forwarded = producer.Messages.Should().ContainSingle().Subject;
        forwarded.Exchange.Should().Be("orders.dlx");
        forwarded.RoutingKey.Should().Be("orders.failed");
        forwarded.ValueString.Should().Be(body);
        forwarded.MessageId.Should().Be(jobId.Value.ToString());
        forwarded.Headers!["x-nexjob-error"].Should().Contain("order failed");
        (await host.Services.GetRequiredService<IDashboardStorage>().GetJobByIdAsync(jobId))!.Status.Should().Be(JobStatus.Failed);

        await host.StopAsync();
    }

    [Fact]
    public async Task ExhaustedJobOnAnotherQueue_IsNotForwarded()
    {
        // N2 (Negative): only the trigger's target queue is forwarded.
        var producer = new RecordingProducer();
        using var host = BuildHost(producer);
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var jobId = await scheduler.EnqueueAsync<FailingOrderJob, string>("{}", queue: "payments");
        await WaitUntilFailedAsync(host, jobId);
        await Task.Delay(300);

        producer.Messages.Should().BeEmpty();
        await host.StopAsync();
    }

    [Fact]
    public async Task FailingPublish_DoesNotCrashTheHost_AndOriginalJobStaysFailed()
    {
        // N2: a broker that is down must not take the dispatcher with it, and the Outbox publisher is never forwarded again.
        var producer = new RecordingProducer { Fail = true, };
        using var host = BuildHost(producer);
        var stopped = false;
        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => stopped = true);
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var jobId = await scheduler.EnqueueAsync<FailingOrderJob, string>("{}", queue: "orders");
        await producer.WaitForAsync(1);
        await Task.Delay(500);

        producer.Calls.Should().Be(1, "the failed publish is a dead-lettered Outbox job on another queue: it is not forwarded in a loop");
        stopped.Should().BeFalse();
        (await host.Services.GetRequiredService<IDashboardStorage>().GetJobByIdAsync(jobId))!.Status.Should().Be(JobStatus.Failed);
        await host.StopAsync();
    }

    [Fact]
    public async Task EmptyBody_IsForwardedAsEmpty()
    {
        // N3 (boundary): an empty message is still a message.
        var producer = new RecordingProducer();
        using var host = BuildHost(producer);
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        await scheduler.EnqueueAsync<FailingOrderJob, string>(string.Empty, queue: "orders");
        await producer.WaitForAsync(1);

        producer.Messages.Single().ValueString.Should().BeEmpty();
        await host.StopAsync();
    }

    [Fact]
    public async Task JobWithANonStringInput_IsIgnoredWithoutError()
    {
        // N3 (Invalid Input): a job on the queue that is not a plain message is not forwarded and does not crash anything.
        var producer = new RecordingProducer();
        using var host = BuildHost(producer);
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var jobId = await scheduler.EnqueueAsync<FailingNumberJob, int>(7, queue: "orders");
        await WaitUntilFailedAsync(host, jobId);
        await Task.Delay(300);

        producer.Messages.Should().BeEmpty();
        await host.StopAsync();
    }

    private static IHost BuildHost(RecordingProducer producer) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(o =>
                {
                    o.Workers = 2;
                    o.MaxAttempts = 1;
                    o.PollingInterval = TimeSpan.FromMilliseconds(50);

                    // The dispatcher only polls these queues: the trigger's queue and the Outbox queue.
                    o.Queues = ["orders", "payments", "rabbitmq-producer"];
                });
                services.AddRabbitMqProducer(o => o.HostName = "localhost");
                services.AddSingleton<IRabbitMqProducerClient>(producer);
                services.AddTransient<FailingOrderJob>();
                services.AddTransient<FailingNumberJob>();

                // The pattern from the documentation: one open-generic handler for every job type.
                services.AddTransient(typeof(IDeadLetterHandler<>), typeof(OrdersDeadLetterForwarder<>));
            })
            .Build();

    private static async Task WaitUntilFailedAsync(IHost host, JobId jobId)
    {
        var storage = host.Services.GetRequiredService<IDashboardStorage>();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var job = await storage.GetJobByIdAsync(jobId);
            if (job?.Status == JobStatus.Failed)
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("The job did not reach Failed.");
    }

    // Mirrors the example in docs/wiki/06-Retry-And-Dead-Letter.md, so the documented code is compiled and run.
    private sealed class OrdersDeadLetterForwarder<TJob>(IScheduler scheduler) : IDeadLetterHandler<TJob>
    {
        public async Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken)
        {
            // Only jobs of this trigger's target queue, and only plain messages (a trigger stores the body as a string).
            if (failedJob.Queue != "orders" || failedJob.InputType != typeof(string).AssemblyQualifiedName)
            {
                return;
            }

            // The trigger job stores the message body as a JSON string: recover it verbatim.
            var body = JsonSerializer.Deserialize<string>(failedJob.InputJson)!;

            await scheduler.EnqueueRabbitMqAsync(
                "orders.dlx",
                "orders.failed",
                body,
                headers: new Dictionary<string, string> { ["x-nexjob-error"] = lastException.Message, },
                messageId: failedJob.Id.Value.ToString(),
                cancellationToken: cancellationToken);
        }
    }

    private sealed class FailingOrderJob : IJob<string>
    {
        public Task ExecuteAsync(string input, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("order failed");
    }

    private sealed class FailingNumberJob : IJob<int>
    {
        public Task ExecuteAsync(int input, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("number failed");
    }

    private sealed class RecordingProducer : IRabbitMqProducerClient
    {
        private readonly List<RabbitMqPublishPayload> _messages = [];
        private int _calls;

        public bool Fail { get; init; }

        public int Calls => Volatile.Read(ref _calls);

        public IReadOnlyList<RabbitMqPublishPayload> Messages
        {
            get
            {
                lock (_messages)
                {
                    return [.. _messages];
                }
            }
        }

        public Task PublishAsync(RabbitMqPublishPayload payload, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            if (Fail)
            {
                throw new InvalidOperationException("broker unavailable");
            }

            lock (_messages)
            {
                _messages.Add(payload);
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }

        public async Task WaitForAsync(int calls)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (Calls < calls && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25);
            }
        }
    }
}
