using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.Trigger.AwsSqs.Tests;

/// <summary>
/// Tests for <see cref="AwsSqsTriggerHandler"/>.
/// Verifies message processing, visibility extension, enqueue, deletion, and graceful shutdown.
/// </summary>
public sealed class AwsSqsTriggerTests
{
    // ─── Happy path: message received → job enqueued → message deleted ───────

    [Fact]
    public async Task HappyPath_MessageReceived_JobEnqueuedAndDeleted()
    {
        // Arrange
        var sqsClient = new MockSqsClient();
        var scheduler = new MockScheduler();
        var options = Options.Create(new AwsSqsTriggerOptions
        {
            QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/test-queue",
            JobName = typeof(TestJob).AssemblyQualifiedName!,
            MaxMessages = 1,
            WaitTimeSeconds = 1,
            VisibilityTimeoutSeconds = 5,
            VisibilityExtensionIntervalSeconds = 3,
        });
        var nexJobOptions = new NexJobOptions { MaxAttempts = 3 };
        var logger = new MockLogger<AwsSqsTriggerHandler>();

        var trigger = new AwsSqsTriggerHandler(
            options,
            sqsClient,
            scheduler,
            nexJobOptions,
            logger);

        sqsClient.AddTestMessage(new Message
        {
            MessageId = "test-msg-001",
            Body = "{\"key\":\"value\"}",
            ReceiptHandle = "test-receipt-handle",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>(),
        });

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await trigger.StartAsync(cts.Token);

        await scheduler.WaitForEnqueueAsync(cts.Token);
        await trigger.StopAsync(cts.Token);

        // Assert
        scheduler.EnqueueCalls.Should().HaveCount(1);
        var enqueuedJob = scheduler.EnqueueCalls[0];
        enqueuedJob.IdempotencyKey.Should().Be("test-msg-001");
        enqueuedJob.Queue.Should().Be("default");
        enqueuedJob.TraceParent.Should().BeNull();
        sqsClient.DeleteCalls.Should().HaveCount(1);
        sqsClient.DeleteCalls[0].Should().Be("test-receipt-handle");
    }

    // ─── Enqueue failure: message NOT deleted ────────────────────────────────

    [Fact]
    public async Task EnqueueFailure_MessageNotDeleted()
    {
        var sqsClient = new MockSqsClient();
        var scheduler = new MockScheduler { ShouldFailEnqueue = true };
        var options = Options.Create(new AwsSqsTriggerOptions
        {
            QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/test-queue",
            JobName = typeof(TestJob).AssemblyQualifiedName!,
            MaxMessages = 1,
            WaitTimeSeconds = 1,
            VisibilityTimeoutSeconds = 5,
            VisibilityExtensionIntervalSeconds = 3,
        });
        var nexJobOptions = new NexJobOptions { MaxAttempts = 3 };
        var logger = new MockLogger<AwsSqsTriggerHandler>();

        var trigger = new AwsSqsTriggerHandler(
            options,
            sqsClient,
            scheduler,
            nexJobOptions,
            logger);

        sqsClient.AddTestMessage(new Message
        {
            MessageId = "test-msg-fail",
            Body = "{\"key\":\"value\"}",
            ReceiptHandle = "test-receipt-handle-fail",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>(),
        });

        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await trigger.StartAsync(cts2.Token);

        await scheduler.WaitForEnqueueAttemptAsync(cts2.Token);
        await trigger.StopAsync(cts2.Token);

        scheduler.EnqueueCalls.Should().HaveCount(1);
        sqsClient.DeleteCalls.Should().BeEmpty("enqueue failed — message should not be deleted");
    }

    // ─── Visibility extension: extension called before timeout ───────────────

    [Fact]
    public async Task VisibilityExtension_ExtendedWhileProcessing()
    {
        var sqsClient = new MockSqsClient();
        var scheduler = new MockScheduler { EnqueueDelay = TimeSpan.FromSeconds(4) };
        var options = Options.Create(new AwsSqsTriggerOptions
        {
            QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/test-queue",
            JobName = typeof(TestJob).AssemblyQualifiedName!,
            MaxMessages = 1,
            WaitTimeSeconds = 1,
            VisibilityTimeoutSeconds = 2,
            VisibilityExtensionIntervalSeconds = 1,
        });
        var nexJobOptions = new NexJobOptions { MaxAttempts = 3 };
        var logger = new MockLogger<AwsSqsTriggerHandler>();

        var trigger = new AwsSqsTriggerHandler(
            options,
            sqsClient,
            scheduler,
            nexJobOptions,
            logger);

        sqsClient.AddTestMessage(new Message
        {
            MessageId = "test-msg-visibility",
            Body = "{}",
            ReceiptHandle = "test-receipt-handle-vis",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>(),
        });

        using var cts3 = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await trigger.StartAsync(cts3.Token);

        await scheduler.WaitForEnqueueAsync(cts3.Token);
        await trigger.StopAsync(cts3.Token);

        sqsClient.VisibilityExtensionCalls.Should().BeGreaterThanOrEqualTo(
            1,
            "visibility should be extended at least once while processing");
    }

    // ─── Graceful shutdown: CancellationToken stops the loop ─────────────────

    [Fact]
    public async Task GracefulShutdown_CancellationToken_StopsCleanly()
    {
        var sqsClient = new MockSqsClient { BlockOnReceive = true };
        var scheduler = new MockScheduler();
        var options = Options.Create(new AwsSqsTriggerOptions
        {
            QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/test-queue",
            JobName = typeof(TestJob).AssemblyQualifiedName!,
            MaxMessages = 1,
            WaitTimeSeconds = 20,
            VisibilityTimeoutSeconds = 30,
            VisibilityExtensionIntervalSeconds = 15,
        });
        var nexJobOptions = new NexJobOptions { MaxAttempts = 3 };
        var logger = new MockLogger<AwsSqsTriggerHandler>();

        var trigger = new AwsSqsTriggerHandler(
            options,
            sqsClient,
            scheduler,
            nexJobOptions,
            logger);

        using var cts4 = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await trigger.StartAsync(cts4.Token);

        await Task.Delay(TimeSpan.FromSeconds(2));
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await trigger.StopAsync(stopCts.Token);

        stopCts.Token.IsCancellationRequested.Should().BeFalse("stop should complete before timeout");
    }

    // ─── Trace propagation: traceparent extracted and set on JobRecord ───────

    [Fact]
    public async Task TracePropagation_TraceparentSetOnJobRecord()
    {
        var sqsClient = new MockSqsClient();
        var scheduler = new MockScheduler();
        var options = Options.Create(new AwsSqsTriggerOptions
        {
            QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/test-queue",
            JobName = typeof(TestJob).AssemblyQualifiedName!,
            MaxMessages = 1,
            WaitTimeSeconds = 1,
            VisibilityTimeoutSeconds = 5,
            VisibilityExtensionIntervalSeconds = 3,
        });
        var nexJobOptions = new NexJobOptions { MaxAttempts = 3 };
        var logger = new MockLogger<AwsSqsTriggerHandler>();

        var trigger = new AwsSqsTriggerHandler(
            options,
            sqsClient,
            scheduler,
            nexJobOptions,
            logger);

        sqsClient.AddTestMessage(new Message
        {
            MessageId = "test-msg-trace",
            Body = "{}",
            ReceiptHandle = "test-receipt-handle-trace",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["traceparent"] = new MessageAttributeValue
                {
                    DataType = "String",
                    StringValue = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
                },
            },
        });

        using var cts5 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await trigger.StartAsync(cts5.Token);

        await scheduler.WaitForEnqueueAsync(cts5.Token);
        await trigger.StopAsync(cts5.Token);

        scheduler.EnqueueCalls.Should().HaveCount(1);
        scheduler.EnqueueCalls[0].TraceParent.Should().Be("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
    }

    [Fact]
    public void AddNexJobAwsSqsTrigger_Generic_RegistersJobAndConfiguresJobName()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddNexJobAwsSqsTrigger<TestConsumerSqsJob>(opt =>
        {
            opt.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/test-queue";
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AwsSqsTriggerOptions>>().Value;

        // Assert
        options.JobName.Should().Be(typeof(TestConsumerSqsJob).AssemblyQualifiedName);
        services.Any(sd => sd.ServiceType == typeof(TestConsumerSqsJob)).Should().BeTrue();
    }

    // ─── 3N Testing Matrix: ListenerRegistry ─────────────────────────────────

    [Fact]
    public async Task ListenerRegistry_Lifecycle_TracksStatusProperly_Positive()
    {
        // N1: Positive - registers as Starting, transitions to Listening on StartAsync, and Stopped on StopAsync.
        var registry = new DefaultListenerRegistry();
        var sqsClient = new MockSqsClient();
        var scheduler = new MockScheduler();
        var options = Options.Create(new AwsSqsTriggerOptions
        {
            QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/test-queue",
            JobName = typeof(TestJob).AssemblyQualifiedName!,
            WaitTimeSeconds = 1,
        });
        var nexJobOptions = new NexJobOptions { MaxAttempts = 3 };
        var logger = new MockLogger<AwsSqsTriggerHandler>();

        var trigger = new AwsSqsTriggerHandler(
            options,
            sqsClient,
            scheduler,
            nexJobOptions,
            logger,
            registry);

        var initial = registry.Get($"sqs:{options.Value.QueueUrl}");
        initial.Should().NotBeNull();
        initial!.Status.Should().Be(ListenerStatus.Starting);
        initial.Broker.Should().Be("AwsSqs");
        initial.Endpoint.Should().Be(options.Value.QueueUrl);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await trigger.StartAsync(cts.Token);

        var listening = registry.Get($"sqs:{options.Value.QueueUrl}");
        listening!.Status.Should().Be(ListenerStatus.Listening);

        await trigger.StopAsync(cts.Token);

        var stopped = registry.Get($"sqs:{options.Value.QueueUrl}");
        stopped!.Status.Should().Be(ListenerStatus.Stopped);
    }

    [Fact]
    public async Task ListenerRegistry_Lifecycle_TracksStatusProperly_Negative()
    {
        // N2: Negative - on PollLoop error, updates status to Reconnecting.
        var registry = new DefaultListenerRegistry();
        var sqsClient = new MockSqsClient();
        var scheduler = new MockScheduler();
        var options = Options.Create(new AwsSqsTriggerOptions
        {
            QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/test-queue",
            JobName = typeof(TestJob).AssemblyQualifiedName!,
            WaitTimeSeconds = 1,
        });
        var nexJobOptions = new NexJobOptions { MaxAttempts = 3 };
        var logger = new MockLogger<AwsSqsTriggerHandler>();

        var trigger = new AwsSqsTriggerHandler(
            options,
            sqsClient,
            scheduler,
            nexJobOptions,
            logger,
            registry);

        sqsClient.ThrowOnReceive = new Amazon.SQS.AmazonSQSException("Service unavailable");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await trigger.StartAsync(cts.Token);
        await Task.Delay(100);

        var reconnecting = registry.Get($"sqs:{options.Value.QueueUrl}");
        reconnecting!.Status.Should().Be(ListenerStatus.Reconnecting);

        await trigger.StopAsync(cts.Token);
    }

    [Fact]
    public void ListenerRegistry_NullRegistry_BoundaryHandledGracefully()
    {
        // N3: Invalid input / Boundary - passing null for IListenerRegistry does not throw.
        var sqsClient = new MockSqsClient();
        var scheduler = new MockScheduler();
        var options = Options.Create(new AwsSqsTriggerOptions
        {
            QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/test-queue",
            JobName = typeof(TestJob).AssemblyQualifiedName!,
        });
        var nexJobOptions = new NexJobOptions { MaxAttempts = 3 };
        var logger = new MockLogger<AwsSqsTriggerHandler>();

        var act = () => new AwsSqsTriggerHandler(
            options,
            sqsClient,
            scheduler,
            nexJobOptions,
            logger,
            listenerRegistry: null);

        act.Should().NotThrow();
    }

    // ─── Test job type ───────────────────────────────────────────────────────

    private sealed class TestJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestConsumerSqsJob : IJob<string>
    {
        public Task ExecuteAsync(string input, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
