using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using NexJob.Exceptions;
using NexJob.Internal;
using NexJob.Storage;
using NexJob.Tests;
using RabbitMQ.Client;
using Xunit;

namespace NexJob.Trigger.RabbitMQ.IntegrationTests;

/// <summary>
/// Integration tests for the RabbitMQ trigger.
/// </summary>
[Collection("RabbitMQ")]
public sealed class RabbitMqTriggerTests : IClassFixture<RabbitMqTriggerFixture>
{
    private readonly RabbitMqTriggerFixture _fixture;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMqTriggerTests"/> class.
    /// </summary>
    /// <param name="fixture">The RabbitMQ test fixture.</param>
    public RabbitMqTriggerTests(RabbitMqTriggerFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Verifies that a message published to RabbitMQ is correctly enqueued as a job and acknowledged.
    /// </summary>
    [Fact]
    public async Task HappyPath_MessageEnqueuedAndAcked()
    {
        // Arrange
        var services = new ServiceCollection();
        var sink = new LevelLogSink();
        services.AddLogging(b => b.AddProvider(sink));
        services.AddNexJob();
        services.AddNexJobRabbitMqTrigger(options =>
        {
            options.HostName = _fixture.HostName;
            options.Port = _fixture.Port;
            options.UserName = RabbitMqTriggerFixture.UserName;
            options.Password = RabbitMqTriggerFixture.Password;
            options.QueueName = "happy-path-queue";
        });

        var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<IScheduler>();
        var trigger = provider.GetRequiredService<IHostedService>();

        // Ensure queue exists
        var factory = new ConnectionFactory
        {
            HostName = _fixture.HostName,
            Port = _fixture.Port,
            UserName = RabbitMqTriggerFixture.UserName,
            Password = RabbitMqTriggerFixture.Password,
        };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        channel.QueueDeclare("happy-path-queue", durable: false, exclusive: false, autoDelete: false);

        // Publish message
        var props = channel.CreateBasicProperties();
        props.Headers = new Dictionary<string, object>
        {
            ["nexjob.job_type"] = typeof(TestJob).AssemblyQualifiedName!,
        };
        props.CorrelationId = "id-123";

        // Behavior changed in v5.6: the idempotency key is the MessageId only, not the CorrelationId (#266)
        props.MessageId = "id-123";
        channel.BasicPublish(string.Empty, "happy-path-queue", props, Encoding.UTF8.GetBytes("{\"Input\":\"Value\"}"));

        // Act
        await trigger.StartAsync(CancellationToken.None);

        // Wait for processing
        JobRecord? job = null;
        await TestWait.UntilAsync(
            async () =>
            {
                var jobs = await scheduler.GetJobsByTagAsync("trigger:rabbitmq");
                job = jobs.FirstOrDefault(j => j.IdempotencyKey == "id-123");
                return job is not null;
            },
            because: "the message to become a job");

        // The ack is sent after the enqueue and the trigger logs right after it: stopping before that would
        // requeue the message and the check below would see it again.
        await TestWait.UntilAsync(
            () => Task.FromResult(sink.Entries.Any(e => e.Message.Contains("enqueued as NexJob job", StringComparison.Ordinal))),
            because: "the message to be acknowledged");

        await trigger.StopAsync(CancellationToken.None);

        // Assert
        job.Should().NotBeNull();
        job!.JobType.Should().Be(typeof(TestJob).AssemblyQualifiedName);

        // Check if message was acked (should be removed from queue)
        var result = channel.BasicGet("happy-path-queue", autoAck: true);
        result.Should().BeNull();
    }

    /// <summary>
    /// Verifies that if enqueuing fails, the message is nacked (removed from queue in this test since no DLX).
    /// </summary>
    [Fact]
    public async Task EnqueueFailure_MessageNacked()
    {
        // Arrange
        var services = new ServiceCollection();
        var sink = new LevelLogSink();
        services.AddLogging(b => b.AddProvider(sink));
        services.AddNexJob();

        // Setup failing storage. The scheduler enqueues through IJobStorage, so that is what must fail: a mock
        // registered as IStorageProvider is never used and the enqueue silently succeeded (the test passed without a nack).
        // FormatException is a permanent failure, which is the nack(requeue: false) path this test is about.
        var mockStorage = new Mock<IJobStorage>();
        mockStorage.Setup(s => s.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FormatException("Simulated permanent failure"));

        services.AddSingleton(mockStorage.Object);

        services.AddNexJobRabbitMqTrigger(options =>
        {
            options.HostName = _fixture.HostName;
            options.Port = _fixture.Port;
            options.UserName = RabbitMqTriggerFixture.UserName;
            options.Password = RabbitMqTriggerFixture.Password;
            options.QueueName = "fail-queue";
        });

        var provider = services.BuildServiceProvider();
        var trigger = provider.GetRequiredService<IHostedService>();

        // Ensure queue exists
        var factory = new ConnectionFactory
        {
            HostName = _fixture.HostName,
            Port = _fixture.Port,
            UserName = RabbitMqTriggerFixture.UserName,
            Password = RabbitMqTriggerFixture.Password,
        };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        channel.QueueDeclare("fail-queue", durable: false, exclusive: false, autoDelete: false);

        // Publish message
        var props = channel.CreateBasicProperties();
        props.Headers = new Dictionary<string, object>
        {
            ["nexjob.job_type"] = typeof(TestJob).AssemblyQualifiedName!,
        };
        channel.BasicPublish(string.Empty, "fail-queue", props, Encoding.UTF8.GetBytes("{}"));

        // Act
        await trigger.StartAsync(CancellationToken.None);
        await TestWait.UntilAsync(
            () => Task.FromResult(sink.Entries.Any(e => e.Message.Contains("can never be enqueued", StringComparison.Ordinal))),
            because: "the trigger to reject the message as a permanent failure");
        await trigger.StopAsync(CancellationToken.None);

        // Assert
        // In RabbitMQ trigger, permanent failure calls BasicNack(requeue: false).
        var result = channel.BasicGet("fail-queue", autoAck: true);
        result.Should().BeNull();
    }

    /// <summary>
    /// Verifies that a transient failure (storage unavailable) never loses the message: the trigger nacks it with
    /// requeue and it is back in the queue.
    /// </summary>
    [Fact]
    public async Task TransientEnqueueFailure_MessageIsRequeued()
    {
        // Arrange
        var services = new ServiceCollection();
        var sink = new LevelLogSink();
        services.AddLogging(b => b.AddProvider(sink));
        services.AddNexJob();

        var mockStorage = new Mock<IJobStorage>();
        mockStorage.Setup(s => s.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated storage outage"));
        services.AddSingleton(mockStorage.Object);

        services.AddNexJobRabbitMqTrigger(options =>
        {
            options.HostName = _fixture.HostName;
            options.Port = _fixture.Port;
            options.UserName = RabbitMqTriggerFixture.UserName;
            options.Password = RabbitMqTriggerFixture.Password;
            options.QueueName = "transient-queue";
        });

        var provider = services.BuildServiceProvider();
        var trigger = provider.GetRequiredService<IHostedService>();

        var factory = new ConnectionFactory
        {
            HostName = _fixture.HostName,
            Port = _fixture.Port,
            UserName = RabbitMqTriggerFixture.UserName,
            Password = RabbitMqTriggerFixture.Password,
        };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        channel.QueueDeclare("transient-queue", durable: false, exclusive: false, autoDelete: false);

        var props = channel.CreateBasicProperties();
        props.Headers = new Dictionary<string, object>
        {
            ["nexjob.job_type"] = typeof(TestJob).AssemblyQualifiedName!,
        };
        channel.BasicPublish(string.Empty, "transient-queue", props, Encoding.UTF8.GetBytes("{}"));

        // Act
        await trigger.StartAsync(CancellationToken.None);
        await TestWait.UntilAsync(
            () => Task.FromResult(sink.Entries.Any(e => e.Message.Contains("Failed to enqueue RabbitMQ message", StringComparison.Ordinal))),
            because: "the trigger to hit the storage outage");
        await trigger.StopAsync(CancellationToken.None);

        // Assert: the stop interrupts the pause before the nack and the message goes back to the queue.
        BasicGetResult? result = null;
        await TestWait.UntilAsync(
            () =>
            {
                result = channel.BasicGet("transient-queue", autoAck: true);
                return Task.FromResult(result is not null);
            },
            because: "the message to be back in the queue");
        result.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that traceparent header is correctly propagated to the JobRecord.
    /// </summary>
    [Fact]
    public async Task TracePropagation_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexJob();
        services.AddNexJobRabbitMqTrigger(options =>
        {
            options.HostName = _fixture.HostName;
            options.Port = _fixture.Port;
            options.UserName = RabbitMqTriggerFixture.UserName;
            options.Password = RabbitMqTriggerFixture.Password;
            options.QueueName = "trace-queue";
        });

        var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<IScheduler>();
        var trigger = provider.GetRequiredService<IHostedService>();

        var factory = new ConnectionFactory
        {
            HostName = _fixture.HostName,
            Port = _fixture.Port,
            UserName = RabbitMqTriggerFixture.UserName,
            Password = RabbitMqTriggerFixture.Password,
        };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        channel.QueueDeclare("trace-queue", durable: false, exclusive: false, autoDelete: false);

        var traceId = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        var props = channel.CreateBasicProperties();
        props.Headers = new Dictionary<string, object>
        {
            ["nexjob.job_type"] = typeof(TestJob).AssemblyQualifiedName!,
            ["traceparent"] = traceId,
        };
        channel.BasicPublish(string.Empty, "trace-queue", props, Encoding.UTF8.GetBytes("{}"));

        // Act
        await trigger.StartAsync(CancellationToken.None);

        JobRecord? job = null;
        await TestWait.UntilAsync(
            async () =>
            {
                var jobs = await scheduler.GetJobsByTagAsync("trigger:rabbitmq");
                job = jobs.FirstOrDefault();
                return job is not null;
            },
            because: "the message to become a job");

        await trigger.StopAsync(CancellationToken.None);

        // Assert
        job.Should().NotBeNull();
        job!.TraceParent.Should().Be(traceId);
    }

    /// <summary>
    /// Verifies the page claim that a message that can never become a job goes to the dead-letter exchange of its queue.
    /// </summary>
    [Fact]
    public async Task NoJobType_WithDeadLetterExchange_MessageIsRoutedToTheDeadLetterQueue()
    {
        using var connection = NewConnectionFactory().CreateConnection();
        using var channel = connection.CreateModel();
        channel.ExchangeDeclare("dlx-no-job-type", ExchangeType.Direct);
        channel.QueueDeclare("dlx-no-job-type-dead", durable: false, exclusive: false, autoDelete: false);
        channel.QueueBind("dlx-no-job-type-dead", "dlx-no-job-type", "dead");
        channel.QueueDeclare("dlx-no-job-type-source", durable: false, exclusive: false, autoDelete: false, arguments: new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = "dlx-no-job-type",
            ["x-dead-letter-routing-key"] = "dead",
        });
        var props = channel.CreateBasicProperties();
        props.MessageId = "dlx-1";
        channel.BasicPublish(string.Empty, "dlx-no-job-type-source", props, Encoding.UTF8.GetBytes("no job type here"));
        var trigger = BuildTriggerHost("dlx-no-job-type-source", services => { });

        await trigger.StartAsync(CancellationToken.None);
        BasicGetResult? dead = null;
        await TestWait.UntilAsync(
            () =>
            {
                dead = channel.BasicGet("dlx-no-job-type-dead", autoAck: true);
                return Task.FromResult(dead is not null);
            },
            because: "the broker to route the rejected message to the dead-letter queue");
        await trigger.StopAsync(CancellationToken.None);

        Encoding.UTF8.GetString(dead!.Body.ToArray()).Should().Be("no job type here");
        channel.BasicGet("dlx-no-job-type-source", autoAck: true).Should().BeNull("it left the source queue");
    }

    /// <summary>
    /// Verifies prefetch above 1: the broker hands the trigger that many unacknowledged messages, the trigger still
    /// enqueues them one after the other, and all of them become jobs once the first enqueue is released.
    /// </summary>
    [Fact]
    public async Task PrefetchAboveOne_BrokerHoldsThatManyUnacked_AndEveryMessageBecomesAJob()
    {
        const string queue = "prefetch-five";
        using var connection = NewConnectionFactory().CreateConnection();
        using var channel = connection.CreateModel();
        channel.QueueDeclare(queue, durable: false, exclusive: false, autoDelete: false);
        for (var i = 0; i < 8; i++)
        {
            var props = channel.CreateBasicProperties();
            props.MessageId = $"prefetch-{i}";
            props.Headers = new Dictionary<string, object> { ["nexjob.job_type"] = typeof(TestJob).AssemblyQualifiedName! };
            channel.BasicPublish(string.Empty, queue, props, Encoding.UTF8.GetBytes("{}"));
        }

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = 0;
        var maxInFlight = 0;
        var started = 0;
        var storage = new Mock<IJobStorage>();
        storage.Setup(s => s.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .Returns(async (JobRecord job, DuplicatePolicy _, CancellationToken _) =>
            {
                Interlocked.Increment(ref started);
                var now = Interlocked.Increment(ref inFlight);
                InterlockedMax(ref maxInFlight, now);
                await gate.Task;
                Interlocked.Decrement(ref inFlight);
                return new EnqueueResult(job.Id, false);
            });
        var trigger = BuildTriggerHost(queue, services => services.AddSingleton(storage.Object), prefetch: 5);

        await trigger.StartAsync(CancellationToken.None);
        await TestWait.UntilAsync(() => Task.FromResult(started >= 1), because: "the first enqueue to start");
        var unacked = 0;
        await TestWait.UntilAsync(
            async () =>
            {
                unacked = await UnackedMessagesAsync(queue);
                return unacked >= 5;
            },
            timeout: TimeSpan.FromSeconds(40),
            because: "the management API to report the prefetch window (it refreshes every few seconds)");
        gate.SetResult();
        await TestWait.UntilAsync(() => Task.FromResult(started >= 8), because: "all eight messages to reach the enqueue");
        await trigger.StopAsync(CancellationToken.None);

        unacked.Should().Be(5, "the broker never sends more than the prefetch count");
        maxInFlight.Should().Be(1, "the trigger enqueues one message at a time, whatever the prefetch");
    }

    /// <summary>
    /// Verifies ExhaustedJobsRoutingKey: a job that exhausts its retries is copied to that routing key, with the error header.
    /// </summary>
    [Fact]
    public async Task ExhaustedJob_IsForwardedToTheRoutingKey_WithTheErrorHeader()
    {
        using var connection = NewConnectionFactory().CreateConnection();
        using var channel = connection.CreateModel();
        channel.QueueDeclare("exhausted-source", durable: false, exclusive: false, autoDelete: false);
        channel.QueueDeclare("exhausted-copy", durable: false, exclusive: false, autoDelete: false);
        var props = channel.CreateBasicProperties();
        props.MessageId = "exhausted-1";
        props.Headers = new Dictionary<string, object> { ["nexjob.job_type"] = typeof(AlwaysFailsJob).AssemblyQualifiedName! };
        channel.BasicPublish(string.Empty, "exhausted-source", props, Encoding.UTF8.GetBytes("original body"));

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(options =>
                {
                    options.Workers = 2;
                    options.MaxAttempts = 1;
                    options.PollingInterval = TimeSpan.FromMilliseconds(100);
                });
                services.AddTransient<AlwaysFailsJob>();
                services.AddNexJobRabbitMqTrigger(options =>
                {
                    ConfigureConnection(options);
                    options.QueueName = "exhausted-source";
                    options.ExhaustedJobsRoutingKey = "exhausted-copy";
                    options.ExhaustedJobsIncludeErrorHeader = true;
                });
                services.AddRabbitMqProducer(options =>
                {
                    options.HostName = _fixture.HostName;
                    options.Port = _fixture.Port;
                    options.UserName = RabbitMqTriggerFixture.UserName;
                    options.Password = RabbitMqTriggerFixture.Password;
                });
            })
            .Build();
        await host.StartAsync();

        BasicGetResult? copy = null;
        await TestWait.UntilAsync(
            () =>
            {
                copy = channel.BasicGet("exhausted-copy", autoAck: true);
                return Task.FromResult(copy is not null);
            },
            timeout: TimeSpan.FromSeconds(40),
            because: "the exhausted job to be copied to the routing key");
        await host.StopAsync();

        Encoding.UTF8.GetString(copy!.Body.ToArray()).Should().Be("original body");
        copy.BasicProperties.Headers.Should().ContainKey("nexjob.error");
        Encoding.UTF8.GetString((byte[])copy.BasicProperties.Headers["nexjob.error"]).Should().Contain("always fails");
    }

    /// <summary>
    /// Verifies that a blank routing key means no forwarding: the exhausted job produces no copy (invalid input).
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExhaustedJob_WithBlankRoutingKey_ProducesNoCopy(string? routingKey)
    {
        var queue = $"blank-key-{Guid.NewGuid():N}";
        using var connection = NewConnectionFactory().CreateConnection();
        using var channel = connection.CreateModel();
        channel.QueueDeclare(queue, durable: false, exclusive: false, autoDelete: false);
        var props = channel.CreateBasicProperties();
        props.MessageId = queue;
        props.Headers = new Dictionary<string, object> { ["nexjob.job_type"] = typeof(AlwaysFailsJob).AssemblyQualifiedName! };
        channel.BasicPublish(string.Empty, queue, props, Encoding.UTF8.GetBytes("body"));

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(options =>
                {
                    options.Workers = 2;
                    options.MaxAttempts = 1;
                    options.PollingInterval = TimeSpan.FromMilliseconds(100);
                });
                services.AddTransient<AlwaysFailsJob>();
                services.AddNexJobRabbitMqTrigger(options =>
                {
                    ConfigureConnection(options);
                    options.QueueName = queue;
                    options.ExhaustedJobsRoutingKey = routingKey;
                });
            })
            .Build();
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        JobRecord? failed = null;
        await TestWait.UntilAsync(
            async () =>
            {
                failed = (await scheduler.GetJobsByTagAsync("trigger:rabbitmq")).FirstOrDefault(j => j.Status == JobStatus.Failed);
                return failed is not null;
            },
            because: "the job to exhaust its attempts");
        await host.StopAsync();

        failed!.Status.Should().Be(JobStatus.Failed);
    }

    private ConnectionFactory NewConnectionFactory() => new()
    {
        HostName = _fixture.HostName,
        Port = _fixture.Port,
        UserName = RabbitMqTriggerFixture.UserName,
        Password = RabbitMqTriggerFixture.Password,
    };

    private void ConfigureConnection(NexJob.Trigger.RabbitMQ.RabbitMqTriggerOptions options)
    {
        options.HostName = _fixture.HostName;
        options.Port = _fixture.Port;
        options.UserName = RabbitMqTriggerFixture.UserName;
        options.Password = RabbitMqTriggerFixture.Password;
    }

    private IHostedService BuildTriggerHost(string queue, Action<IServiceCollection> configure, ushort prefetch = 1)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure(services);
        services.AddNexJob();
        services.AddNexJobRabbitMqTrigger(options =>
        {
            ConfigureConnection(options);
            options.QueueName = queue;
            options.PrefetchCount = prefetch;
        });
        return services.BuildServiceProvider().GetRequiredService<IHostedService>();
    }

    private async Task<int> UnackedMessagesAsync(string queue)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{RabbitMqTriggerFixture.UserName}:{RabbitMqTriggerFixture.Password}")));
        var json = await http.GetStringAsync(new Uri($"http://{_fixture.HostName}:{_fixture.ManagementPort}/api/queues/%2F/{queue}"));
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("messages_unacknowledged", out var value) ? value.GetInt32() : 0;
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current)
            {
                break;
            }
        }
    }

    private sealed class AlwaysFailsJob : IJob<string>
    {
        public Task ExecuteAsync(string input, CancellationToken cancellationToken) => throw new InvalidOperationException("always fails");
    }

    private sealed class TestJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
