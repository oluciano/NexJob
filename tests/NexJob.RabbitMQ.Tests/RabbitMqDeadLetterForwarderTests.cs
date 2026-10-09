using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NexJob.RabbitMQ;
using Xunit;

namespace NexJob.Trigger.RabbitMQ.Tests;

public sealed class RabbitMqDeadLetterForwarderTests
{
    private const string Body = "{\"orderId\":42,\"note\":\"é, \\\"quoted\\\"\"}";

    // ─── AppliesTo ────────────────────────────────────────────────────────────

    [Fact]
    public void AppliesTo_JobCreatedByThisTriggerOnTheTargetQueue_IsTrue()
    {
        var (forwarder, _) = Build();

        forwarder.AppliesTo(TriggerJob()).Should().BeTrue();
    }

    [Fact]
    public void AppliesTo_JobOnAnotherQueue_IsFalse()
    {
        var (forwarder, _) = Build();

        forwarder.AppliesTo(TriggerJob(queue: "payments")).Should().BeFalse();
    }

    [Fact]
    public void AppliesTo_ManuallyEnqueuedJobOnTheTargetQueue_IsFalse()
    {
        var (forwarder, _) = Build();

        forwarder.AppliesTo(TriggerJob(tags: [])).Should().BeFalse("it is not a message received from RabbitMQ");
    }

    [Fact]
    public void AppliesTo_TheOutboxProducerJob_IsFalse()
    {
        var (forwarder, _) = Build(targetQueue: "rabbitmq-producer");
        var producerJob = TriggerJob(queue: "rabbitmq-producer", tags: ["producer:rabbitmq"]);

        forwarder.AppliesTo(producerJob).Should().BeFalse("forwarding the publisher would loop");
    }

    [Fact]
    public void AppliesTo_ForwardingNotConfigured_IsFalse()
    {
        var (forwarder, _) = Build(routingKey: null);

        forwarder.AppliesTo(TriggerJob()).Should().BeFalse();
    }

    [Fact]
    public void AppliesTo_JobWithoutAMessageId_StillApplies()
    {
        // N3: RabbitMQ jobs only have an idempotency key when the publisher assigned a MessageId.
        var (forwarder, _) = Build();

        forwarder.AppliesTo(TriggerJob(idempotencyKey: null)).Should().BeTrue();
    }

    // ─── ForwardAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ForwardAsync_PublishesTheOriginalBodyVerbatimToTheConfiguredExchangeAndKey()
    {
        var (forwarder, captured) = Build();
        var job = TriggerJob();

        await forwarder.ForwardAsync(job, new InvalidOperationException("order failed"), CancellationToken.None);

        var payload = captured.Payload.Should().NotBeNull().And.Subject.As<RabbitMqPublishPayload>();
        payload.Exchange.Should().Be("orders.dlx");
        payload.RoutingKey.Should().Be("orders.exhausted");
        payload.ValueString.Should().Be(Body, "a copy of what was received, exactly");
        payload.Headers.Should().BeNullOrEmpty("the error header is off by default");
        captured.IdempotencyKey.Should().Be($"dead-letter-forward:{job.Id.Value}");
    }

    [Fact]
    public async Task ForwardAsync_ErrorHeaderEnabled_AddsTheLastError()
    {
        var (forwarder, captured) = Build(includeError: true);
        var job = TriggerJob(lastErrorMessage: "stored error");

        await forwarder.ForwardAsync(job, new InvalidOperationException("order failed"), CancellationToken.None);

        captured.Payload!.Headers.Should().ContainKey("nexjob.error").WhoseValue.Should().Be("stored error");
    }

    [Fact]
    public async Task ForwardAsync_EmptyBody_IsForwardedAsEmpty()
    {
        var (forwarder, captured) = Build();

        await forwarder.ForwardAsync(TriggerJob(body: string.Empty), new InvalidOperationException("order failed"), CancellationToken.None);

        captured.Payload!.ValueString.Should().BeEmpty();
    }

    [Fact]
    public async Task ForwardAsync_InputThatIsNotAJsonString_FallsBackToTheStoredText()
    {
        var (forwarder, captured) = Build();
        var job = TriggerJob(inputJson: "{\"not\":\"a string\"}");

        await forwarder.ForwardAsync(job, new InvalidOperationException("order failed"), CancellationToken.None);

        captured.Payload!.ValueString.Should().Be("{\"not\":\"a string\"}");
    }

    // ─── Registration and startup validation ──────────────────────────────────

    [Fact]
    public void Registration_ForwardingConfiguredWithoutTheProducer_FailsValidation()
    {
        var services = new ServiceCollection();
        services.AddNexJobRabbitMqTrigger(ConfigureTrigger(routingKey: "orders.exhausted"));
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RabbitMqTriggerOptions>>();

        Func<RabbitMqTriggerOptions> read = () => options.Value;

        read.Should().Throw<OptionsValidationException>().WithMessage("*AddRabbitMqProducer*");
    }

    [Fact]
    public void Registration_ForwardingConfiguredWithTheProducer_PassesValidation()
    {
        var services = new ServiceCollection();
        services.AddRabbitMqProducer(o => o.HostName = "localhost");
        services.AddNexJobRabbitMqTrigger(ConfigureTrigger(routingKey: "orders.exhausted"));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<RabbitMqTriggerOptions>>().Value.ExhaustedJobsRoutingKey.Should().Be("orders.exhausted");
    }

    [Fact]
    public void Registration_ForwardingNotConfigured_DoesNotRequireTheProducer()
    {
        var services = new ServiceCollection();
        services.AddNexJobRabbitMqTrigger(ConfigureTrigger(routingKey: null));
        using var provider = services.BuildServiceProvider();

        Func<RabbitMqTriggerOptions> read = () => provider.GetRequiredService<IOptions<RabbitMqTriggerOptions>>().Value;

        read.Should().NotThrow();
    }

    [Fact]
    public void Registration_AddsTheForwarderToTheDispatcher()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Mock<IScheduler>().Object);
        services.AddSingleton(new NexJobOptions()); // registered by AddNexJob in a real host
        services.AddRabbitMqProducer(o => o.HostName = "localhost");
        services.AddNexJobRabbitMqTrigger(ConfigureTrigger(routingKey: "orders.exhausted"));
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IDeadLetterForwarder>().Should().ContainSingle().Which.Should().BeOfType<RabbitMqDeadLetterForwarder>();
    }

    private static Action<RabbitMqTriggerOptions> ConfigureTrigger(string? routingKey) => o =>
    {
        o.HostName = "localhost";
        o.QueueName = "orders-in";
        o.ExhaustedJobsRoutingKey = routingKey;
    };

    // ─── Helpers ──────────────────────────────────────────────────────────────

    [Fact]
    public void AppliesTo_DefaultTargetQueueWithPrefix_MatchesTheStoredPrefixedQueue()
    {
        // #406: the default target queue is stored as "{prefix}.default"; the written name is not what the job carries.
        var (forwarder, _) = Build(targetQueue: "default", queuePrefix: "shop");

        forwarder.AppliesTo(TriggerJob(queue: "shop.default")).Should().BeTrue();
    }

    [Fact]
    public void AppliesTo_DefaultTargetQueueWithPrefix_StillMatchesTheLegacyDefault()
    {
        // Jobs the trigger created before the upgrade are still in "default".
        var (forwarder, _) = Build(targetQueue: "default", queuePrefix: "shop");

        forwarder.AppliesTo(TriggerJob(queue: "default")).Should().BeTrue();
    }

    [Theory]
    [InlineData("other.default")]
    [InlineData("emails")]
    [InlineData("")]
    public void AppliesTo_DefaultTargetQueueWithPrefix_DoesNotMatchAnotherQueue(string queue)
    {
        var (forwarder, _) = Build(targetQueue: "default", queuePrefix: "shop");

        forwarder.AppliesTo(TriggerJob(queue: queue)).Should().BeFalse();
    }

    [Fact]
    public void AppliesTo_NamedTargetQueue_MatchesOnlyThatQueue()
    {
        var (forwarder, _) = Build(targetQueue: "orders", queuePrefix: "shop");

        forwarder.AppliesTo(TriggerJob(queue: "orders")).Should().BeTrue();
        forwarder.AppliesTo(TriggerJob(queue: "shop.default")).Should().BeFalse();
    }

    private static (RabbitMqDeadLetterForwarder Forwarder, Captured Captured) Build(
        string? routingKey = "orders.exhausted",
        string targetQueue = "orders",
        bool includeError = false,
        string? queuePrefix = null)
    {
        var captured = new Captured();
        var scheduler = new Mock<IScheduler>();
        scheduler
            .Setup(s => s.EnqueueAsync<RabbitMqProducerJob, RabbitMqPublishPayload>(
                It.IsAny<RabbitMqPublishPayload>(),
                It.IsAny<string?>(),
                It.IsAny<JobPriority>(),
                It.IsAny<string?>(),
                It.IsAny<DuplicatePolicy>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<RabbitMqPublishPayload, string?, JobPriority, string?, DuplicatePolicy, IReadOnlyList<string>?, TimeSpan?, CancellationToken>(
                (payload, _, _, idempotencyKey, _, _, _, _) =>
                {
                    captured.Payload = payload;
                    captured.IdempotencyKey = idempotencyKey;
                })
            .ReturnsAsync(JobId.New());

        var options = Options.Create(new RabbitMqTriggerOptions
        {
            HostName = "localhost",
            QueueName = "orders-in",
            TargetQueue = targetQueue,
            ExhaustedJobsExchange = "orders.dlx",
            ExhaustedJobsRoutingKey = routingKey,
            ExhaustedJobsIncludeErrorHeader = includeError,
        });

        return (new RabbitMqDeadLetterForwarder(options, scheduler.Object, queuePrefix is null ? new NexJobOptions() : new NexJobOptions { QueuePrefix = queuePrefix }), captured);
    }

    private static JobRecord TriggerJob(
        string queue = "orders",
        string? idempotencyKey = "message-7",
        string body = Body,
        IReadOnlyList<string>? tags = null,
        string? lastErrorMessage = null,
        string? inputJson = null) => new()
        {
            Id = JobId.New(),
            JobType = "Orders.ProcessOrderJob, Orders",
            InputType = typeof(string).AssemblyQualifiedName!,
            InputJson = inputJson ?? JsonSerializer.Serialize(body),
            Queue = queue,
            IdempotencyKey = idempotencyKey,
            Tags = tags ?? ["trigger:rabbitmq"],
            LastErrorMessage = lastErrorMessage,
            Status = JobStatus.Failed,
            Attempts = 3,
            MaxAttempts = 3,
        };

    private sealed class Captured
    {
        public RabbitMqPublishPayload? Payload { get; set; }

        public string? IdempotencyKey { get; set; }
    }
}
