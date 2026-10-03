using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.RabbitMQ;
using NexJob.Storage;
using Xunit;

namespace NexJob.Trigger.RabbitMQ.Tests;

/// <summary>
/// The built-in forwarder end to end, through the real dispatcher and Outbox: a job created like the trigger creates it
/// exhausts its retries and is copied to the configured exchange and routing key, while it stays Failed in NexJob.
/// </summary>
public sealed class RabbitMqDeadLetterForwarderEndToEndTests
{
    private const string Body = "{\"orderId\":42,\"note\":\"é, \\\"quoted\\\"\"}";

    [Fact]
    public async Task ExhaustedTriggerJob_IsForwardedAsReceived_AndStaysFailed()
    {
        // N1 (Positive)
        var producer = new RecordingProducer();
        using var host = BuildHost(producer, routingKey: "orders.exhausted", includeError: true);
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var jobId = await EnqueueLikeTheTrigger(scheduler);
        await producer.WaitForAsync(1);

        var forwarded = producer.Messages.Should().ContainSingle().Subject;
        forwarded.Exchange.Should().Be("orders.dlx");
        forwarded.RoutingKey.Should().Be("orders.exhausted");
        forwarded.ValueString.Should().Be(Body);
        forwarded.Headers.Should().ContainKey("nexjob.error").WhoseValue.Should().Contain("order failed");
        (await host.Services.GetRequiredService<IDashboardStorage>().GetJobByIdAsync(jobId))!.Status.Should().Be(JobStatus.Failed);

        await host.StopAsync();
    }

    [Fact]
    public async Task JobsThatTheTriggerDidNotCreate_AreNotForwarded()
    {
        // N2 (Negative): same queue but enqueued by hand, and a job on another queue.
        var producer = new RecordingProducer();
        using var host = BuildHost(producer, routingKey: "orders.exhausted");
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var manual = await scheduler.EnqueueAsync<FailingOrderJob, string>(Body, queue: "orders");
        var elsewhere = await scheduler.EnqueueAsync<FailingOrderJob, string>(Body, queue: "payments", tags: ["trigger:rabbitmq"]);
        await WaitUntilFailedAsync(host, manual);
        await WaitUntilFailedAsync(host, elsewhere);
        await Task.Delay(300);

        producer.Messages.Should().BeEmpty();
        await host.StopAsync();
    }

    [Fact]
    public async Task FailingPublish_DoesNotCrashTheHost_AndNothingLoops()
    {
        // N2 (Negative): a broker that is down must not take the dispatcher with it.
        var producer = new RecordingProducer { Fail = true, };
        using var host = BuildHost(producer, routingKey: "orders.exhausted");
        var stopped = false;
        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => stopped = true);
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var jobId = await EnqueueLikeTheTrigger(scheduler);
        await producer.WaitForAsync(1);
        await Task.Delay(500);

        producer.Calls.Should().Be(1, "the failed publish is a dead-lettered Outbox job on another queue and is not forwarded again");
        stopped.Should().BeFalse();
        (await host.Services.GetRequiredService<IDashboardStorage>().GetJobByIdAsync(jobId))!.Status.Should().Be(JobStatus.Failed);
        await host.StopAsync();
    }

    [Fact]
    public async Task ForwardingNotConfigured_PublishesNothing()
    {
        // N3 (Boundary)
        var producer = new RecordingProducer();
        using var host = BuildHost(producer, routingKey: null);
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var jobId = await EnqueueLikeTheTrigger(scheduler);
        await WaitUntilFailedAsync(host, jobId);
        await Task.Delay(300);

        producer.Messages.Should().BeEmpty();
        await host.StopAsync();
    }

    // The trigger stores the body as a JSON string on its target queue, tagged trigger:rabbitmq.
    private static Task<JobId> EnqueueLikeTheTrigger(IScheduler scheduler) =>
        scheduler.EnqueueAsync<FailingOrderJob, string>(Body, queue: "orders", tags: ["trigger:rabbitmq"]);

    private static IHost BuildHost(RecordingProducer producer, string? routingKey, bool includeError = false) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(o =>
                {
                    o.Workers = 2;
                    o.MaxAttempts = 1;
                    o.PollingInterval = TimeSpan.FromMilliseconds(50);
                    o.Queues = ["orders", "payments", "rabbitmq-producer"];
                });
                services.AddRabbitMqProducer(o => o.HostName = "localhost");
                services.AddSingleton<IRabbitMqProducerClient>(producer);
                services.AddTransient<FailingOrderJob>();

                // The forwarder exactly as AddNexJobRabbitMqTrigger registers it, without opening a broker connection.
                services.Configure<RabbitMqTriggerOptions>(o =>
                {
                    o.HostName = "localhost";
                    o.QueueName = "orders-in";
                    o.TargetQueue = "orders";
                    o.ExhaustedJobsExchange = "orders.dlx";
                    o.ExhaustedJobsRoutingKey = routingKey;
                    o.ExhaustedJobsIncludeErrorHeader = includeError;
                });
                services.AddSingleton<IDeadLetterForwarder, RabbitMqDeadLetterForwarder>();
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

    private sealed class FailingOrderJob : IJob<string>
    {
        public Task ExecuteAsync(string input, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("order failed");
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
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (Calls < calls)
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException($"Expected {calls} publish call(s), saw {Calls}.");
                }

                await Task.Delay(25);
            }
        }
    }
}
