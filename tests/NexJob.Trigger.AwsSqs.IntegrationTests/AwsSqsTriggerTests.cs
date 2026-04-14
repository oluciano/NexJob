using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NexJob.Internal;
using NexJob.Storage;
using NexJob.Trigger.AwsSqs;
using Xunit;

namespace NexJob.Trigger.AwsSqs.IntegrationTests;

/// <summary>
/// Integration tests for the AWS SQS trigger.
/// </summary>
[Collection("AWS SQS")]
public sealed class AwsSqsTriggerTests : IClassFixture<AwsSqsTriggerFixture>
{
    private readonly AwsSqsTriggerFixture _fixture;

    /// <summary>
    /// Initializes a new instance of the <see cref="AwsSqsTriggerTests"/> class.
    /// </summary>
    /// <param name="fixture">The AWS SQS test fixture.</param>
    public AwsSqsTriggerTests(AwsSqsTriggerFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Verifies that a message published to SQS is correctly enqueued as a job and deleted.
    /// </summary>
    [Fact]
    public async Task HappyPath_MessageEnqueuedAndDeleted()
    {
        // Arrange
        var queueName = "happy-path-queue";
        var credentials = new BasicAWSCredentials("test", "test");
        var config = new AmazonSQSConfig { ServiceURL = _fixture.ConnectionString };
        var sqsClient = new AmazonSQSClient(credentials, config);
        var createResult = await sqsClient.CreateQueueAsync(queueName);
        var queueUrl = createResult.QueueUrl;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexJob();

        services.AddNexJobAwsSqsTrigger(options =>
        {
            options.QueueUrl = queueUrl;
            options.JobName = typeof(TestJob).AssemblyQualifiedName!;
        });

        services.AddSingleton<IAmazonSQS>(sqsClient);
        services.AddTransient<ISqsClient>(sp => new SqsClientWrapper(sp.GetRequiredService<IAmazonSQS>()));

        var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<IScheduler>();
        var trigger = provider.GetRequiredService<IHostedService>();

        var sendMessageRequest = new SendMessageRequest
        {
            QueueUrl = queueUrl,
            MessageBody = "{\"Input\":\"Value\"}",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["nexjob.job_type"] = new() { DataType = "String", StringValue = typeof(TestJob).AssemblyQualifiedName },
            },
        };
        await sqsClient.SendMessageAsync(sendMessageRequest);

        // Act
        await trigger.StartAsync(CancellationToken.None);

        JobRecord? job = null;
        for (int i = 0; i < 50; i++)
        {
            var jobs = await scheduler.GetJobsByTagAsync("trigger:awssqs");
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
        job!.JobType.Should().Be(typeof(TestJob).AssemblyQualifiedName);

        var receiveResult = await sqsClient.ReceiveMessageAsync(queueUrl);
        receiveResult.Messages.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that if enqueuing fails, the message is NOT deleted.
    /// </summary>
    [Fact]
    public async Task EnqueueFailure_MessageNotDeleted()
    {
        // Arrange
        var queueName = "fail-queue";
        var credentials = new BasicAWSCredentials("test", "test");
        var config = new AmazonSQSConfig { ServiceURL = _fixture.ConnectionString };
        var sqsClient = new AmazonSQSClient(credentials, config);
        var createResult = await sqsClient.CreateQueueAsync(queueName);
        var queueUrl = createResult.QueueUrl;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexJob();

        var mockStorage = new Mock<IStorageProvider>();
        mockStorage.Setup(s => s.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Simulated storage failure"));
        services.AddSingleton(mockStorage.Object);

        services.AddNexJobAwsSqsTrigger(options =>
        {
            options.QueueUrl = queueUrl;
            options.JobName = typeof(TestJob).AssemblyQualifiedName!;
            options.VisibilityTimeoutSeconds = 1;
        });

        services.AddSingleton<IAmazonSQS>(sqsClient);
        services.AddTransient<ISqsClient>(sp => new SqsClientWrapper(sp.GetRequiredService<IAmazonSQS>()));

        var provider = services.BuildServiceProvider();
        var trigger = provider.GetRequiredService<IHostedService>();

        await sqsClient.SendMessageAsync(queueUrl, "{}");

        // Act
        await trigger.StartAsync(CancellationToken.None);
        await Task.Delay(2000);
        await trigger.StopAsync(CancellationToken.None);

        // Assert
        // Retry receive because of possible delay in visibility update
        ReceiveMessageResponse? receiveResult = null;
        for (int i = 0; i < 5; i++)
        {
            receiveResult = await sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = queueUrl,
                WaitTimeSeconds = 1,
            });
            if (receiveResult.Messages.Any())
            {
                break;
            }

            await Task.Delay(1000);
        }

        receiveResult!.Messages.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies that traceparent attribute is correctly propagated.
    /// </summary>
    [Fact]
    public async Task TracePropagation_Works()
    {
        // Arrange
        var queueName = "trace-queue";
        var credentials = new BasicAWSCredentials("test", "test");
        var config = new AmazonSQSConfig { ServiceURL = _fixture.ConnectionString };
        var sqsClient = new AmazonSQSClient(credentials, config);
        var createResult = await sqsClient.CreateQueueAsync(queueName);
        var queueUrl = createResult.QueueUrl;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexJob();
        services.AddNexJobAwsSqsTrigger(options =>
        {
            options.QueueUrl = queueUrl;
            options.JobName = typeof(TestJob).AssemblyQualifiedName!;
        });

        services.AddSingleton<IAmazonSQS>(sqsClient);
        services.AddTransient<ISqsClient>(sp => new SqsClientWrapper(sp.GetRequiredService<IAmazonSQS>()));

        var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<IScheduler>();
        var trigger = provider.GetRequiredService<IHostedService>();

        var traceId = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        var sendMessageRequest = new SendMessageRequest
        {
            QueueUrl = queueUrl,
            MessageBody = "{}",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["traceparent"] = new() { DataType = "String", StringValue = traceId },
            },
        };
        await sqsClient.SendMessageAsync(sendMessageRequest);

        // Act
        await trigger.StartAsync(CancellationToken.None);

        JobRecord? job = null;
        for (int i = 0; i < 50; i++)
        {
            var jobs = await scheduler.GetJobsByTagAsync("trigger:awssqs");
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

    private sealed class SqsClientWrapper : ISqsClient
    {
        private readonly IAmazonSQS _sqs;
        public SqsClientWrapper(IAmazonSQS sqs) => _sqs = sqs;

        public Task<ReceiveMessageResponse> ReceiveMessageAsync(ReceiveMessageRequest request, CancellationToken cancellationToken = default)
            => _sqs.ReceiveMessageAsync(request, cancellationToken);

        public Task<DeleteMessageResponse> DeleteMessageAsync(DeleteMessageRequest request, CancellationToken cancellationToken = default)
            => _sqs.DeleteMessageAsync(request, cancellationToken);

        public Task<ChangeMessageVisibilityResponse> ChangeMessageVisibilityAsync(ChangeMessageVisibilityRequest request, CancellationToken cancellationToken = default)
            => _sqs.ChangeMessageVisibilityAsync(request, cancellationToken);
    }
}
