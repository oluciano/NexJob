using System.Diagnostics;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NexJob.Exceptions;
using NexJob.Internal;
using NexJob.Storage;
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
        services.AddLogging();
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
        channel.BasicPublish(string.Empty, "happy-path-queue", props, Encoding.UTF8.GetBytes("{\"Input\":\"Value\"}"));

        // Act
        await trigger.StartAsync(CancellationToken.None);

        // Wait for processing
        JobRecord? job = null;
        for (int i = 0; i < 50; i++)
        {
            var jobs = await scheduler.GetJobsByTagAsync("trigger:rabbitmq");
            job = jobs.FirstOrDefault(j => j.IdempotencyKey == "id-123");
            if (job != null)
            {
                break;
            }

            await Task.Delay(100);
        }

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
        services.AddLogging();
        services.AddNexJob();

        // Setup failing storage
        var mockStorage = new Mock<IStorageProvider>();
        mockStorage.Setup(s => s.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Simulated storage failure"));

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
        await Task.Delay(1000); // Wait for processing attempt
        await trigger.StopAsync(CancellationToken.None);

        // Assert
        // In RabbitMQ trigger, permanent failure calls BasicNack(requeue: false).
        var result = channel.BasicGet("fail-queue", autoAck: true);
        result.Should().BeNull();
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
        for (int i = 0; i < 50; i++)
        {
            var jobs = await scheduler.GetJobsByTagAsync("trigger:rabbitmq");
            job = jobs.FirstOrDefault();
            if (job != null)
            {
                break;
            }

            await Task.Delay(100);
        }

        await trigger.StopAsync(CancellationToken.None);

        // Assert
        job.Should().NotBeNull();
        job!.TraceParent.Should().Be(traceId);
    }

    private sealed class TestJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
