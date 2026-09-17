using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.Kafka.IntegrationTests;

/// <summary>
/// Integration tests verifying real end-to-end Kafka consumer/trigger behavior with an Apache Kafka container.
/// </summary>
[Collection("Kafka")]
public sealed class KafkaConsumerIntegrationTests
{
    private readonly KafkaFixture _fixture;

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaConsumerIntegrationTests"/> class.
    /// </summary>
    /// <param name="fixture">Shared Kafka container fixture.</param>
    public KafkaConsumerIntegrationTests(KafkaFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// N1 — Positive: valid message is enqueued as a NexJob job and offset is committed.
    /// </summary>
    [Fact]
    public async Task HappyPath_ValidMessage_EnqueuedAndCommitted()
    {
        var topic = "nexjob-consumer-happy-path";
        await CreateTopicAsync(topic);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexJob();
        services.AddKafkaTrigger(options =>
        {
            options.BootstrapServers = _fixture.BootstrapServers;
            options.Topic = topic;
            options.GroupId = "test-group-happy";
            options.TargetQueue = "default";
        });
        services.AddTransient<TestJob>();

        using var provider = services.BuildServiceProvider();
        var trigger = provider.GetRequiredService<IHostedService>();

        // Publish message with required nexjob.job_type header
        await PublishMessageAsync(topic, "{\"user\":\"integration\"}", typeof(TestJob).AssemblyQualifiedName!);

        await trigger.StartAsync(CancellationToken.None);

        // Wait for processing
        var scheduler = provider.GetRequiredService<IScheduler>();
        JobRecord? job = null;
        for (var i = 0; i < 50; i++)
        {
            var jobs = await scheduler.GetJobsByTagAsync("trigger:kafka");
            job = jobs.FirstOrDefault();
            if (job is not null)
            {
                break;
            }

            await Task.Delay(200);
        }

        await trigger.StopAsync(CancellationToken.None);

        job.Should().NotBeNull("valid message must be enqueued as NexJob job");
        job!.Status.Should().BeOneOf(JobStatus.Enqueued, JobStatus.Processing, JobStatus.Succeeded);
    }

    /// <summary>
    /// N2 — Negative: poison message without nexjob.job_type header is routed to Dead Letter Topic.
    /// </summary>
    [Fact]
    public async Task PoisonMessage_MissingJobTypeHeader_MovedToDlt()
    {
        var topic = "nexjob-consumer-poison";
        var dltTopic = "nexjob-consumer-poison-dlt";
        await CreateTopicAsync(topic);
        await CreateTopicAsync(dltTopic);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexJob();
        services.AddKafkaTrigger(options =>
        {
            options.BootstrapServers = _fixture.BootstrapServers;
            options.Topic = topic;
            options.GroupId = "test-group-poison";
            options.TargetQueue = "default";
            options.DeadLetterTopic = dltTopic;
        });

        using var provider = services.BuildServiceProvider();
        var trigger = provider.GetRequiredService<IHostedService>();

        // Publish message WITHOUT nexjob.job_type header
        await PublishMessageAsync(topic, "{\"data\":\"corrupted\"}", jobType: null);

        await trigger.StartAsync(CancellationToken.None);
        await Task.Delay(3000); // wait for processing + DLT production
        await trigger.StopAsync(CancellationToken.None);

        // Verify message appeared in DLT
        var dltMessage = ConsumeOneMessage(dltTopic, "dlt-verifier-group");
        dltMessage.Should().NotBeNull("poison message without required job type header must be moved to DLT");
    }

    /// <summary>
    /// N1/Trace: W3C traceparent header is propagated to JobRecord.TraceParent.
    /// </summary>
    [Fact]
    public async Task TracePropagation_HeaderPropagatedToJobRecord()
    {
        var topic = "nexjob-consumer-trace";
        await CreateTopicAsync(topic);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexJob();
        services.AddKafkaTrigger(options =>
        {
            options.BootstrapServers = _fixture.BootstrapServers;
            options.Topic = topic;
            options.GroupId = "test-group-trace";
            options.TargetQueue = "default";
        });
        services.AddTransient<TestJob>();

        using var provider = services.BuildServiceProvider();
        var trigger = provider.GetRequiredService<IHostedService>();

        var expectedTraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        await PublishMessageAsync(
            topic,
            "{\"trace\":\"test\"}",
            typeof(TestJob).AssemblyQualifiedName!,
            traceParent: expectedTraceParent);

        await trigger.StartAsync(CancellationToken.None);

        var scheduler = provider.GetRequiredService<IScheduler>();
        JobRecord? job = null;
        for (var i = 0; i < 50; i++)
        {
            var jobs = await scheduler.GetJobsByTagAsync("trigger:kafka");
            job = jobs.FirstOrDefault(j => j.TraceParent == expectedTraceParent);
            if (job is not null)
            {
                break;
            }

            await Task.Delay(200);
        }

        await trigger.StopAsync(CancellationToken.None);

        job.Should().NotBeNull();
        job!.TraceParent.Should().Be(expectedTraceParent);
    }

    private async Task CreateTopicAsync(string topic)
    {
        using var admin = new AdminClientBuilder(
            new AdminClientConfig { BootstrapServers = _fixture.BootstrapServers })
            .Build();

        try
        {
            await admin.CreateTopicsAsync(new[]
            {
                new TopicSpecification
                {
                    Name = topic,
                    NumPartitions = 1,
                    ReplicationFactor = 1,
                },
            });
        }
        catch (CreateTopicsException ex) when (ex.Results[0].Error.Code == ErrorCode.TopicAlreadyExists)
        {
            // Already exists — ignored
        }
    }

    private async Task PublishMessageAsync(string topic, string body, string? jobType, string? traceParent = null)
    {
        using var producer = new ProducerBuilder<string, string>(
            new ProducerConfig { BootstrapServers = _fixture.BootstrapServers })
            .Build();

        var headers = new Headers();
        if (jobType is not null)
        {
            headers.Add("nexjob.job_type", Encoding.UTF8.GetBytes(jobType));
        }

        if (traceParent is not null)
        {
            headers.Add("traceparent", Encoding.UTF8.GetBytes(traceParent));
        }

        var message = new Message<string, string>
        {
            Key = Guid.NewGuid().ToString(),
            Value = body,
            Headers = headers,
        };

        await producer.ProduceAsync(topic, message);
        producer.Flush(TimeSpan.FromSeconds(5));
    }

    private ConsumeResult<string, string>? ConsumeOneMessage(string topic, string groupId)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _fixture.BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();

        consumer.Subscribe(topic);

        var result = consumer.Consume(TimeSpan.FromSeconds(10));
        consumer.Close();
        return result;
    }
}
