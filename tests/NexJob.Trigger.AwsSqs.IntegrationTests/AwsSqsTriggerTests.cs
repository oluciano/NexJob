using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NexJob.Internal;
using NexJob.Storage;
using NexJob.Tests;
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
        await TestWait.UntilAsync(
            async () =>
            {
                var jobs = await scheduler.GetJobsByTagAsync("trigger:awssqs");
                job = jobs.FirstOrDefault();
                return job is not null;
            },
            because: "the message to become a job");

        // The message is deleted after the enqueue: stopping first could leave it to reappear.
        await TestWait.UntilAsync(
            async () =>
            {
                var attributes = await sqsClient.GetQueueAttributesAsync(
                    queueUrl,
                    ["ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible"]);
                return attributes.ApproximateNumberOfMessages == 0 && attributes.ApproximateNumberOfMessagesNotVisible == 0;
            },
            because: "the message to be deleted from the queue");

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

        var mockStorage = new Mock<IStorageProvider>();
        mockStorage.Setup(s => s.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Simulated storage failure"));
        services.AddSingleton<IJobStorage>(mockStorage.Object);
        services.AddSingleton<IStorageProvider>(mockStorage.Object);

        services.AddNexJob();

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
        await TestWait.UntilAsync(
            () => Task.FromResult(mockStorage.Invocations.Any(i => i.Method.Name == nameof(IJobStorage.EnqueueAsync))),
            because: "the trigger to attempt the enqueue");
        await trigger.StopAsync(CancellationToken.None);

        // Assert
        // The message is not deleted; it reappears once its visibility timeout (1 s) ends.
        ReceiveMessageResponse? receiveResult = null;
        await TestWait.UntilAsync(
            async () =>
            {
                receiveResult = await sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = queueUrl,
                    WaitTimeSeconds = 1,
                });
                return receiveResult.Messages.Any();
            },
            because: "the undeleted message to become visible again");

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
        await TestWait.UntilAsync(
            async () =>
            {
                var jobs = await scheduler.GetJobsByTagAsync("trigger:awssqs");
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
    /// Verifies the page claim that a failed enqueue sets the visibility timeout to 0: the message comes back at once,
    /// not after the 30 s the queue is configured with.
    /// </summary>
    [Fact]
    public async Task EnqueueFailure_MessageIsRedeliveredAtOnce_NotAfterTheVisibilityTimeout()
    {
        var (sqsClient, queueUrl) = await CreateQueueAsync("redeliver-at-once");
        var storage = FailingStorage(_ => Task.FromException<EnqueueResult>(new InvalidOperationException("storage down")));
        await using var host = BuildTrigger(sqsClient, queueUrl, storage.Object, visibilityTimeoutSeconds: 30);
        await sqsClient.SendMessageAsync(queueUrl, "{}");

        await host.StartAsync();

        await TestWait.UntilAsync(
            () => Task.FromResult(storage.Invocations.Count(i => i.Method.Name == nameof(IJobStorage.EnqueueAsync)) >= 3),
            timeout: TimeSpan.FromSeconds(15),
            because: "the same message to be delivered again and again, well before the 30 s visibility timeout");

        storage.Invocations.Count(i => i.Method.Name == nameof(IJobStorage.EnqueueAsync)).Should().BeGreaterOrEqualTo(3);
    }

    /// <summary>
    /// Verifies the page claim about a poison message: without a redrive policy it is received again in a loop.
    /// </summary>
    [Fact]
    public async Task PoisonMessage_WithoutRedrivePolicy_IsReceivedInALoop()
    {
        var (sqsClient, queueUrl) = await CreateQueueAsync("poison-no-redrive");
        var storage = FailingStorage(_ => Task.FromException<EnqueueResult>(new InvalidOperationException("never works")));
        await using var host = BuildTrigger(sqsClient, queueUrl, storage.Object, visibilityTimeoutSeconds: 30);
        await sqsClient.SendMessageAsync(queueUrl, "{}");

        await host.StartAsync();

        await TestWait.UntilAsync(
            () => Task.FromResult(storage.Invocations.Count(i => i.Method.Name == nameof(IJobStorage.EnqueueAsync)) >= 5),
            timeout: TimeSpan.FromSeconds(20),
            because: "a message that can never be enqueued to keep coming back with no limit");

        storage.Invocations.Count(i => i.Method.Name == nameof(IJobStorage.EnqueueAsync)).Should().BeGreaterOrEqualTo(5);
    }

    /// <summary>
    /// Verifies the page claim that a dead-letter queue with maxReceiveCount stops the loop.
    /// </summary>
    [Fact]
    public async Task PoisonMessage_WithRedrivePolicy_MovesToTheDeadLetterQueueAndStops()
    {
        var (sqsClient, queueUrl) = await CreateQueueAsync("poison-redrive");
        var (_, dlqUrl) = await CreateQueueAsync("poison-redrive-dlq");
        var dlqArn = (await sqsClient.GetQueueAttributesAsync(dlqUrl, ["QueueArn"])).QueueARN;
        await sqsClient.SetQueueAttributesAsync(queueUrl, new Dictionary<string, string>
        {
            ["RedrivePolicy"] = $"{{\"deadLetterTargetArn\":\"{dlqArn}\",\"maxReceiveCount\":\"3\"}}",
        });
        var storage = FailingStorage(_ => Task.FromException<EnqueueResult>(new InvalidOperationException("never works")));
        await using var host = BuildTrigger(sqsClient, queueUrl, storage.Object, visibilityTimeoutSeconds: 30);
        await sqsClient.SendMessageAsync(queueUrl, "{}");

        await host.StartAsync();

        await TestWait.UntilAsync(
            async () => (await sqsClient.GetQueueAttributesAsync(dlqUrl, ["ApproximateNumberOfMessages"])).ApproximateNumberOfMessages == 1,
            timeout: TimeSpan.FromSeconds(30),
            because: "SQS to move the message to the dead-letter queue after maxReceiveCount receives");

        var attempts = storage.Invocations.Count(i => i.Method.Name == nameof(IJobStorage.EnqueueAsync));
        attempts.Should().BeLessOrEqualTo(3, "the redrive policy stops the loop at maxReceiveCount");
    }

    /// <summary>
    /// Verifies the page claim that the trigger renews the visibility while a message is handled: a slow enqueue is not
    /// delivered to a second consumer in the meantime.
    /// </summary>
    [Fact]
    public async Task SlowEnqueue_VisibilityIsRenewed_SoNoSecondConsumerSeesTheMessage()
    {
        var (sqsClient, queueUrl) = await CreateQueueAsync("visibility-renewal");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = FailingStorage(async _ =>
        {
            entered.TrySetResult();
            await release.Task;
            throw new InvalidOperationException("released");
        });
        await using var host = BuildTrigger(sqsClient, queueUrl, storage.Object, visibilityTimeoutSeconds: 3, extensionIntervalSeconds: 1);
        await sqsClient.SendMessageAsync(queueUrl, "{}");

        await host.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Assert that something did NOT happen: wait past the visibility timeout (3 s) with the enqueue still running.
        await Task.Delay(TimeSpan.FromSeconds(6));
        var second = await sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest { QueueUrl = queueUrl, WaitTimeSeconds = 1 });
        release.TrySetResult();

        second.Messages.Should().BeEmpty("the renewal keeps the message hidden while it is being handled");
    }

    /// <summary>
    /// Verifies that a message body that is empty or whitespace is not lost or crashing the loop (invalid input).
    /// </summary>
    [Fact]
    public async Task EmptyBody_IsHandledWithoutStoppingTheTrigger()
    {
        var (sqsClient, queueUrl) = await CreateQueueAsync("empty-body");
        var storage = FailingStorage(_ => Task.FromException<EnqueueResult>(new InvalidOperationException("storage down")));
        await using var host = BuildTrigger(sqsClient, queueUrl, storage.Object, visibilityTimeoutSeconds: 30);
        await sqsClient.SendMessageAsync(queueUrl, " ");

        await host.StartAsync();

        await TestWait.UntilAsync(
            () => Task.FromResult(storage.Invocations.Any(i => i.Method.Name == nameof(IJobStorage.EnqueueAsync))),
            timeout: TimeSpan.FromSeconds(15),
            because: "the trigger to reach the enqueue for a whitespace body");

        storage.Invocations.Count(i => i.Method.Name == nameof(IJobStorage.EnqueueAsync)).Should().BeGreaterOrEqualTo(1);
    }

    private async Task<(AmazonSQSClient Client, string QueueUrl)> CreateQueueAsync(string name)
    {
        var client = new AmazonSQSClient(new BasicAWSCredentials("test", "test"), new AmazonSQSConfig { ServiceURL = _fixture.ConnectionString });
        var created = await client.CreateQueueAsync(name);
        return (client, created.QueueUrl);
    }

    private static Mock<IStorageProvider> FailingStorage(Func<JobRecord, Task<EnqueueResult>> onEnqueue)
    {
        var storage = new Mock<IStorageProvider>();
        storage.Setup(s => s.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .Returns((JobRecord job, DuplicatePolicy _, CancellationToken _) => onEnqueue(job));
        return storage;
    }

    private static TriggerHost BuildTrigger(
        AmazonSQSClient client,
        string queueUrl,
        IStorageProvider storage,
        int visibilityTimeoutSeconds,
        int extensionIntervalSeconds = 15)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IJobStorage>(storage);
        services.AddSingleton(storage);
        services.AddNexJob();
        services.AddNexJobAwsSqsTrigger(options =>
        {
            options.QueueUrl = queueUrl;
            options.JobName = typeof(TestJob).AssemblyQualifiedName!;
            options.VisibilityTimeoutSeconds = visibilityTimeoutSeconds;
            options.VisibilityExtensionIntervalSeconds = extensionIntervalSeconds;
            options.WaitTimeSeconds = 1;
        });
        services.AddSingleton<IAmazonSQS>(client);
        services.AddTransient<ISqsClient>(sp => new SqsClientWrapper(sp.GetRequiredService<IAmazonSQS>()));
        var provider = services.BuildServiceProvider();
        return new TriggerHost(provider, provider.GetRequiredService<IHostedService>());
    }

    private sealed class TriggerHost(ServiceProvider provider, IHostedService trigger) : IAsyncDisposable
    {
        public Task StartAsync() => trigger.StartAsync(CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            await trigger.StopAsync(CancellationToken.None);
            await provider.DisposeAsync();
        }
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
