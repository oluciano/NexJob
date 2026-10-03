using System.Text;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Kafka;
using NexJob.Storage;
using Xunit;

namespace NexJob.Trigger.Kafka.Tests;

/// <summary>
/// The built-in forwarder end to end: a message consumed by the trigger becomes a job that exhausts its retries and is
/// copied to the configured topic through the Outbox, while the job stays Failed in NexJob.
/// </summary>
public sealed class KafkaDeadLetterForwarderEndToEndTests
{
    private const string Body = "{\"orderId\":42,\"note\":\"é, \\\"quoted\\\"\"}";

    [Fact]
    public async Task ExhaustedTriggerJob_IsForwardedAsReceived_AndStaysFailed()
    {
        // N1 (Positive)
        var producer = new RecordingProducer();
        using var host = BuildHost(producer, exhaustedTopic: "orders.exhausted", includeError: true);
        await host.StartAsync();

        await producer.WaitForAsync(1);

        var forwarded = producer.Messages.Should().ContainSingle().Subject;
        forwarded.Topic.Should().Be("orders.exhausted");
        Encoding.UTF8.GetString(forwarded.Message.Value).Should().Be(Body);
        Encoding.UTF8.GetString(forwarded.Message.Headers.GetLastBytes("nexjob.error")).Should().Contain("order failed");

        var storage = host.Services.GetRequiredService<IDashboardStorage>();
        var failed = await storage.GetJobsAsync(new JobFilter { Status = JobStatus.Failed }, page: 1, pageSize: 10);
        failed.Items.Should().Contain(j => string.Equals(j.IdempotencyKey, "kafka:orders-in:0:7", StringComparison.Ordinal));

        await host.StopAsync();
    }

    [Fact]
    public async Task FailingPublish_DoesNotCrashTheHost_AndTheOriginalJobStaysFailed()
    {
        // N2 (Negative): a broker that is down must not take the dispatcher with it, and nothing loops.
        var producer = new RecordingProducer { Fail = true, };
        using var host = BuildHost(producer, exhaustedTopic: "orders.exhausted");
        var stopped = false;
        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => stopped = true);
        await host.StartAsync();

        await producer.WaitForAsync(1);
        await Task.Delay(500);

        producer.Calls.Should().Be(1, "the failed publish is a dead-lettered Outbox job on another queue and is not forwarded again");
        stopped.Should().BeFalse();
        await host.StopAsync();
    }

    [Fact]
    public async Task ForwardingNotConfigured_PublishesNothing()
    {
        // N3 (Boundary): without ExhaustedJobsTopic the trigger behaves exactly as before.
        var producer = new RecordingProducer();
        using var host = BuildHost(producer, exhaustedTopic: null);
        await host.StartAsync();

        await Task.Delay(1500);

        producer.Messages.Should().BeEmpty();
        await host.StopAsync();
    }

    private static IHost BuildHost(RecordingProducer producer, string? exhaustedTopic, bool includeError = false) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(o =>
                {
                    o.Workers = 2;
                    o.MaxAttempts = 1;
                    o.PollingInterval = TimeSpan.FromMilliseconds(50);
                    o.Queues = ["orders", "kafka-producer"];
                });
                services.AddKafkaProducer(o => o.BootstrapServers = "localhost:9092");
                services.AddSingleton<IKafkaProducerClient>(producer);
                services.AddNexJobKafkaTrigger<FailingOrderJob>(o =>
                {
                    o.BootstrapServers = "localhost:9092";
                    o.Topic = "orders-in";
                    o.GroupId = "g";
                    o.TargetQueue = "orders";
                    o.ExhaustedJobsTopic = exhaustedTopic;
                    o.ExhaustedJobsIncludeErrorHeader = includeError;
                });

                // Replaces the real consumer: one message at offset 7, then nothing.
                services.AddSingleton<IKafkaConsumer>(new OneMessageConsumer());
            })
            .Build();

    private sealed class FailingOrderJob : IJob<string>
    {
        public Task ExecuteAsync(string input, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("order failed");
    }

    private sealed class OneMessageConsumer : IKafkaConsumer
    {
        private int _delivered;

        public void Subscribe(string topic)
        {
        }

        public ConsumeResult<string, string>? Consume(TimeSpan timeout)
        {
            if (Interlocked.Exchange(ref _delivered, 1) == 0)
            {
                return new ConsumeResult<string, string>
                {
                    Topic = "orders-in",
                    Partition = new Partition(0),
                    Offset = new Offset(7),
                    Message = new Message<string, string> { Value = Body, Headers = new Headers(), },
                };
            }

            Thread.Sleep(20);
            return null;
        }

        public void Commit(ConsumeResult<string, string> result)
        {
        }

        public void Close()
        {
        }

        public Task ProduceToDeadLetterAsync(string topic, ConsumeResult<string, string> result, Exception exception, CancellationToken ct) =>
            Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingProducer : IKafkaProducerClient
    {
        private readonly List<(string Topic, Message<string, byte[]> Message)> _messages = [];
        private readonly SemaphoreSlim _signal = new(0);
        private int _calls;

        public bool Fail { get; init; }

        public int Calls => Volatile.Read(ref _calls);

        public IReadOnlyList<(string Topic, Message<string, byte[]> Message)> Messages
        {
            get
            {
                lock (_messages)
                {
                    return [.. _messages];
                }
            }
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

                await _signal.WaitAsync(TimeSpan.FromMilliseconds(100));
            }
        }

        public Task<DeliveryResult<string, byte[]>> ProduceAsync(string topic, Message<string, byte[]> message, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            _signal.Release();
            if (Fail)
            {
                throw new KafkaException(new Error(ErrorCode.BrokerNotAvailable));
            }

            lock (_messages)
            {
                _messages.Add((topic, message));
            }

            return Task.FromResult(new DeliveryResult<string, byte[]> { Topic = topic, Message = message, });
        }

        public void Flush(TimeSpan timeout)
        {
        }

        public void Dispose() => _signal.Dispose();
    }
}
