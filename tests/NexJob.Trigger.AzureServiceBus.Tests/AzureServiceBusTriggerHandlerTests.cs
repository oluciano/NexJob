using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NexJob.Internal;
using Xunit;

namespace NexJob.Trigger.AzureServiceBus.Tests;

/// <summary>
/// Unit tests for <see cref="AzureServiceBusTriggerHandler"/> covering the 3N Mandatory Matrix:
/// N1: Happy path (enqueues job and completes message).
/// N2: Negative scenarios (enqueue failures dead-letter message).
/// N3: Invalid input and boundary conditions (missing job type, cancellation).
/// </summary>
public sealed class AzureServiceBusTriggerHandlerTests
{
    private readonly MockScheduler _scheduler = new();
    private readonly Mock<ILogger<AzureServiceBusTriggerHandler>> _loggerMock = new();
    private readonly NexJobOptions _nexJobOptions = new() { MaxAttempts = 3 };
    private readonly AzureServiceBusTriggerOptions _triggerOptions = new()
    {
        ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fakekey",
        QueueOrTopicName = "test-queue",
        TargetQueue = "default",
        JobPriority = JobPriority.High,
    };

    private AzureServiceBusTriggerHandler CreateHandler()
    {
        return new AzureServiceBusTriggerHandler(
            Options.Create(_triggerOptions),
            _scheduler,
            _nexJobOptions,
            _loggerMock.Object);
    }

    private static (Mock<ProcessMessageEventArgs> Mock, ServiceBusReceivedMessage Message) CreateMessageArgs(
        string messageId = "msg-001",
        string body = "{\"test\":true}",
        string? jobType = "MyTestJobType",
        string? traceparent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
        CancellationToken cancellationToken = default)
    {
        var properties = new Dictionary<string, object>();
        if (jobType is not null)
        {
            properties["nexjob.job_type"] = jobType;
        }

        if (traceparent is not null)
        {
            properties["traceparent"] = traceparent;
        }

        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString(body),
            messageId: messageId,
            properties: properties);

        var receiverMock = new Mock<ServiceBusReceiver>();
        var mock = new Mock<ProcessMessageEventArgs>(message, receiverMock.Object, cancellationToken);
        mock.Setup(a => a.CompleteMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(a => a.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return (mock, message);
    }

    [Fact]
    public async Task HandleMessageAsync_ValidMessage_EnqueuesJobAndCompletesMessage()
    {
        // Arrange
        var handler = CreateHandler();
        var (argsMock, message) = CreateMessageArgs(
            messageId: "msg-42",
            body: "{\"orderId\":123}",
            jobType: "ProcessOrderJob",
            traceparent: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");

        // Act
        await handler.HandleMessageAsync(argsMock.Object);

        // Assert
        _scheduler.EnqueueCalls.Should().HaveCount(1);
        var enqueued = _scheduler.EnqueueCalls[0];
        enqueued.IdempotencyKey.Should().Be("msg-42");
        enqueued.JobType.Should().Be("ProcessOrderJob");
        enqueued.Queue.Should().Be("default");
        enqueued.Priority.Should().Be(JobPriority.High);
        enqueued.TraceParent.Should().Be("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
        enqueued.Tags.Should().Contain("trigger:azuresb");

        // Verify broker completion was called
        argsMock.Verify(a => a.CompleteMessageAsync(message, It.IsAny<CancellationToken>()), Times.Once);
        argsMock.Verify(a => a.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleMessageAsync_MessageWithoutTraceparent_EnqueuesWithNullTraceParent()
    {
        // Arrange
        var handler = CreateHandler();
        var (argsMock, _) = CreateMessageArgs(traceparent: null);

        // Act
        await handler.HandleMessageAsync(argsMock.Object);

        // Assert
        _scheduler.EnqueueCalls.Should().HaveCount(1);
        _scheduler.EnqueueCalls[0].TraceParent.Should().BeNull();
        argsMock.Verify(a => a.CompleteMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var handler = CreateHandler();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        var act = async () => await handler.StartAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task StopAsync_WhenNotStarted_CompletesWithoutError()
    {
        // Arrange
        var handler = CreateHandler();

        // Act & Assert
        var act = async () => await handler.StopAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task HandleErrorAsync_LogsErrorAndCompletesSuccessfully()
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new InvalidOperationException("Service bus communication error");
        var errorArgs = new ProcessErrorEventArgs(exception, ServiceBusErrorSource.Receive, "test-ns", "test-entity", CancellationToken.None);

        // Act
        var act = async () => await handler.HandleErrorAsync(errorArgs);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Options_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new AzureServiceBusTriggerOptions
        {
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/",
            QueueOrTopicName = "test-topic",
            SubscriptionName = "sub-1",
            MaxConcurrentMessages = 5,
            TargetQueue = "custom-queue",
            JobPriority = JobPriority.Critical,
        };

        // Assert
        options.ConnectionString.Should().Be("Endpoint=sb://test.servicebus.windows.net/");
        options.QueueOrTopicName.Should().Be("test-topic");
        options.SubscriptionName.Should().Be("sub-1");
        options.MaxConcurrentMessages.Should().Be(5);
        options.TargetQueue.Should().Be("custom-queue");
        options.JobPriority.Should().Be(JobPriority.Critical);

        var defaults = new AzureServiceBusTriggerOptions();
        defaults.MaxConcurrentMessages.Should().Be(1);
        defaults.TargetQueue.Should().Be("default");
        defaults.JobPriority.Should().Be(JobPriority.Normal);
        defaults.SubscriptionName.Should().BeNull();
    }

    [Fact]
    public void AddNexJobAzureServiceBusTrigger_RegistersHostedServiceAndOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IScheduler>());
        services.AddSingleton(new NexJobOptions());
        services.AddLogging();

        // Act
        services.AddNexJobAzureServiceBusTrigger(options =>
        {
            options.ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake";
            options.QueueOrTopicName = "orders";
            options.TargetQueue = "processing";
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>();
        hostedServices.Should().ContainSingle(s => s is AzureServiceBusTriggerHandler);

        var options = provider.GetRequiredService<IOptions<AzureServiceBusTriggerOptions>>().Value;
        options.QueueOrTopicName.Should().Be("orders");
        options.TargetQueue.Should().Be("processing");
    }

    // ─── N2: NEGATIVE TESTS (Failure Handling) ────────────────────────────────

    [Fact]
    public async Task HandleMessageAsync_EnqueueThrowsException_DeadLettersMessageAndDoesNotComplete()
    {
        // Arrange
        var handler = CreateHandler();
        _scheduler.ShouldFailEnqueue = true;
        _scheduler.CustomException = new TimeoutException("Database connection timeout");

        var (argsMock, message) = CreateMessageArgs(messageId: "msg-err-1");

        // Act
        await handler.HandleMessageAsync(argsMock.Object);

        // Assert
        argsMock.Verify(a => a.CompleteMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        argsMock.Verify(a => a.DeadLetterMessageAsync(message, "EnqueueFailed", "Database connection timeout", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleMessageAsync_DeadLetterThrows_DoesNotCrashCaller()
    {
        // Arrange
        var handler = CreateHandler();
        _scheduler.ShouldFailEnqueue = true;

        var (argsMock, _) = CreateMessageArgs();
        argsMock.Setup(a => a.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ServiceBusException("Failed to dead letter", ServiceBusFailureReason.GeneralError));

        // Act
        var act = async () => await handler.HandleMessageAsync(argsMock.Object);

        // Assert: must swallow dead letter exception and not crash
        await act.Should().NotThrowAsync();
    }

    // ─── N3: INVALID INPUT & BOUNDARY TESTS ──────────────────────────────────

    [Fact]
    public async Task HandleMessageAsync_MissingJobTypeProperty_DeadLettersMessage()
    {
        // Arrange
        var handler = CreateHandler();
        var (argsMock, message) = CreateMessageArgs(jobType: null);

        // Act
        await handler.HandleMessageAsync(argsMock.Object);

        // Assert: missing job type causes exception in ExtractJobType, leading to DeadLetter
        _scheduler.EnqueueCalls.Should().BeEmpty();
        argsMock.Verify(a => a.CompleteMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        argsMock.Verify(a => a.DeadLetterMessageAsync(message, "EnqueueFailed", It.Is<string>(s => s.Contains("nexjob.job_type")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleMessageAsync_CancellationRequested_PropagatesOperationCanceledException()
    {
        // Arrange
        var handler = CreateHandler();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var (argsMock, _) = CreateMessageArgs(cancellationToken: cts.Token);

        // Act & Assert
        var act = async () => await handler.HandleMessageAsync(argsMock.Object);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Neither complete nor dead-letter should be called on cancellation
        argsMock.Verify(a => a.CompleteMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        argsMock.Verify(a => a.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleMessageAsync_EmptyPayload_EnqueuesSuccessfully()
    {
        // Arrange
        var handler = CreateHandler();
        var (argsMock, _) = CreateMessageArgs(body: "{}");

        // Act
        await handler.HandleMessageAsync(argsMock.Object);

        // Assert
        _scheduler.EnqueueCalls.Should().HaveCount(1);
        _scheduler.EnqueueCalls[0].InputJson.Should().Be("{}");
        argsMock.Verify(a => a.CompleteMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleMessageAsync_JobTypeInOptions_FallbackUsed_WhenPropertyMissing()
    {
        // Arrange
        _triggerOptions.JobType = "ConfiguredAsbConsumerJob";
        var handler = CreateHandler();
        var (argsMock, message) = CreateMessageArgs(jobType: null);

        // Act
        await handler.HandleMessageAsync(argsMock.Object);

        // Assert
        _scheduler.EnqueueCalls.Should().HaveCount(1);
        _scheduler.EnqueueCalls[0].JobType.Should().Be("ConfiguredAsbConsumerJob");
        argsMock.Verify(a => a.CompleteMessageAsync(message, It.IsAny<CancellationToken>()), Times.Once);
        argsMock.Verify(a => a.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleMessageAsync_HeaderJobType_TakesPrecedence_OverOptionsJobType()
    {
        // Arrange
        _triggerOptions.JobType = "FallbackOptionsAsbJob";
        var handler = CreateHandler();
        var (argsMock, message) = CreateMessageArgs(jobType: "HeaderPriorityAsbJob");

        // Act
        await handler.HandleMessageAsync(argsMock.Object);

        // Assert
        _scheduler.EnqueueCalls.Should().HaveCount(1);
        _scheduler.EnqueueCalls[0].JobType.Should().Be("HeaderPriorityAsbJob");
        argsMock.Verify(a => a.CompleteMessageAsync(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleMessageAsync_WhitespaceJobTypeInOptions_DeadLettersMessage()
    {
        // Arrange
        _triggerOptions.JobType = "   ";
        var handler = CreateHandler();
        var (argsMock, message) = CreateMessageArgs(jobType: null);

        // Act
        await handler.HandleMessageAsync(argsMock.Object);

        // Assert
        _scheduler.EnqueueCalls.Should().BeEmpty();
        argsMock.Verify(a => a.CompleteMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        argsMock.Verify(a => a.DeadLetterMessageAsync(message, "EnqueueFailed", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void AddNexJobAzureServiceBusTrigger_Generic_RegistersJobAndConfiguresJobType()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddNexJobAzureServiceBusTrigger<TestConsumerAsbJob>(opt =>
        {
            opt.ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fakekey";
            opt.QueueOrTopicName = "test-queue";
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AzureServiceBusTriggerOptions>>().Value;

        // Assert
        options.JobType.Should().Be(typeof(TestConsumerAsbJob).AssemblyQualifiedName);
        services.Any(sd => sd.ServiceType == typeof(TestConsumerAsbJob)).Should().BeTrue();
    }
}

/// <summary>
/// Sample consumer job for Azure Service Bus registration tests.
/// </summary>
public sealed class TestConsumerAsbJob : IJob<string>
{
    public Task ExecuteAsync(string input, CancellationToken cancellationToken) => Task.CompletedTask;
}
