using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NexJob.Storage;
using NexJob.Tests;
using Xunit;

namespace NexJob.Trigger.AzureServiceBus.IntegrationTests;

/// <summary>
/// The Azure Service Bus trigger against the official emulator. The emulator is Microsoft's, but it is not the cloud
/// service: limits and timings of the real one can differ.
/// </summary>
public sealed class AzureServiceBusTriggerIntegrationTests : IClassFixture<ServiceBusEmulatorFixture>
{
    private readonly ServiceBusEmulatorFixture _fixture;

    public AzureServiceBusTriggerIntegrationTests(ServiceBusEmulatorFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Message_BecomesAJob_AndIsCompleted()
    {
        var queue = ServiceBusEmulatorFixture.Queue(0);
        await SendAsync(queue, jobType: typeof(OrderJob).AssemblyQualifiedName, body: "{\"OrderId\":1}");
        await using var provider = BuildTrigger(queue, maxConcurrent: 1, storage: null);
        var trigger = provider.GetRequiredService<IHostedService>();
        var scheduler = provider.GetRequiredService<IScheduler>();

        await trigger.StartAsync(CancellationToken.None);
        await TestWait.UntilAsync(
            async () => (await scheduler.GetJobsByTagAsync("trigger:azuresb")).Count == 1,
            timeout: TimeSpan.FromSeconds(60),
            because: "the message to become a job");
        // The message is completed after the enqueue: stopping first would leave it for another delivery.
        await TestWait.UntilAsync(
            async () => await PeekCountAsync(queue) == 0,
            timeout: TimeSpan.FromSeconds(30),
            because: "the message to be completed after the enqueue");
        await trigger.StopAsync(CancellationToken.None);

        (await PeekCountAsync(queue)).Should().Be(0);
    }

    [Fact]
    public async Task MessageWithoutAJobType_IsDeadLettered_WithTheReasonEnqueueFailed()
    {
        var queue = ServiceBusEmulatorFixture.Queue(1);
        await SendAsync(queue, jobType: null, body: "no job type");
        await using var provider = BuildTrigger(queue, maxConcurrent: 1, storage: null);
        var trigger = provider.GetRequiredService<IHostedService>();

        await trigger.StartAsync(CancellationToken.None);
        var dead = await ReceiveDeadLetterAsync(queue);
        await trigger.StopAsync(CancellationToken.None);

        dead.DeadLetterReason.Should().Be("EnqueueFailed");
        dead.DeadLetterErrorDescription.Should().Contain("nexjob.job_type");
    }

    [Fact]
    public async Task TransientFailure_IsAbandoned_AndServiceBusDeadLettersItAfterMaxDeliveryCount()
    {
        var queue = ServiceBusEmulatorFixture.Queue(2);
        await SendAsync(queue, jobType: typeof(OrderJob).AssemblyQualifiedName, body: "{}");
        var attempts = 0;
        var storage = new Mock<IJobStorage>();
        storage.Setup(s => s.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Interlocked.Increment(ref attempts);
                return Task.FromException<EnqueueResult>(new TimeoutException("storage down"));
            });
        await using var provider = BuildTrigger(queue, maxConcurrent: 1, storage.Object);
        var trigger = provider.GetRequiredService<IHostedService>();

        await trigger.StartAsync(CancellationToken.None);
        var dead = await ReceiveDeadLetterAsync(queue);
        await trigger.StopAsync(CancellationToken.None);

        dead.DeadLetterReason.Should().Be("MaxDeliveryCountExceeded");
        attempts.Should().Be(3, "the entity's MaxDeliveryCount is 3");
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(3, 4)]
    public async Task MaxConcurrentMessages_LimitsHowManyMessagesAreHandledAtOnce(int maxConcurrent, int slot)
    {
        var queue = ServiceBusEmulatorFixture.Queue(slot + 3);
        for (var i = 0; i < 6; i++)
        {
            await SendAsync(queue, jobType: typeof(OrderJob).AssemblyQualifiedName, body: $"{{\"n\":{i}}}");
        }

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = 0;
        var maxInFlight = 0;
        var storage = new Mock<IJobStorage>();
        storage.Setup(s => s.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .Returns(async (JobRecord job, DuplicatePolicy _, CancellationToken _) =>
            {
                var now = Interlocked.Increment(ref inFlight);
                InterlockedMax(ref maxInFlight, now);
                await gate.Task;
                Interlocked.Decrement(ref inFlight);
                return new EnqueueResult(job.Id, false);
            });
        await using var provider = BuildTrigger(queue, maxConcurrent, storage.Object);
        var trigger = provider.GetRequiredService<IHostedService>();

        await trigger.StartAsync(CancellationToken.None);
        await TestWait.UntilAsync(
            () => Task.FromResult(Volatile.Read(ref inFlight) >= 1),
            timeout: TimeSpan.FromSeconds(60),
            because: "the first message to reach the enqueue");
        await Task.Delay(TimeSpan.FromSeconds(3));
        var peak = Volatile.Read(ref maxInFlight);
        gate.SetResult();
        await trigger.StopAsync(CancellationToken.None);

        peak.Should().Be(maxConcurrent);
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

    private ServiceProvider BuildTrigger(string queue, int maxConcurrent, IJobStorage? storage)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (storage is not null)
        {
            services.AddSingleton(storage);
        }

        services.AddNexJob();
        services.AddNexJobAzureServiceBusTrigger(options =>
        {
            options.ConnectionString = _fixture.ConnectionString;
            options.QueueOrTopicName = queue;
            options.MaxConcurrentMessages = maxConcurrent;
        });
        return services.BuildServiceProvider();
    }

    private async Task SendAsync(string queue, string? jobType, string body)
    {
        await using var client = new ServiceBusClient(_fixture.ConnectionString);
        await using var sender = client.CreateSender(queue);
        var message = new ServiceBusMessage(body) { MessageId = Guid.NewGuid().ToString() };
        if (jobType is not null)
        {
            message.ApplicationProperties["nexjob.job_type"] = jobType;
        }

        await sender.SendMessageAsync(message);
    }

    private async Task<int> PeekCountAsync(string queue)
    {
        await using var client = new ServiceBusClient(_fixture.ConnectionString);
        await using var receiver = client.CreateReceiver(queue);
        return (await receiver.PeekMessagesAsync(10)).Count;
    }

    private async Task<ServiceBusReceivedMessage> ReceiveDeadLetterAsync(string queue)
    {
        await using var client = new ServiceBusClient(_fixture.ConnectionString);
        await using var receiver = client.CreateReceiver(queue, new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
        ServiceBusReceivedMessage? message = null;
        await TestWait.UntilAsync(
            async () =>
            {
                message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(2));
                return message is not null;
            },
            timeout: TimeSpan.FromSeconds(120),
            because: "the message to reach the dead-letter queue");
        return message!;
    }

    private sealed class OrderJob : IJob<string>
    {
        public Task ExecuteAsync(string input, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
