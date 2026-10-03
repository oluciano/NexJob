using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NexJob.Kafka;
using Xunit;

namespace NexJob.Trigger.Kafka.Tests;

public sealed class KafkaDeadLetterForwarderTests
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
    public void AppliesTo_JobCreatedByAnotherTopic_IsFalse()
    {
        var (forwarder, _) = Build();

        forwarder.AppliesTo(TriggerJob(idempotencyKey: "kafka:other-topic:0:5")).Should().BeFalse();
    }

    [Fact]
    public void AppliesTo_ManuallyEnqueuedJobOnTheTargetQueue_IsFalse()
    {
        var (forwarder, _) = Build();

        forwarder.AppliesTo(TriggerJob(idempotencyKey: null)).Should().BeFalse("it is not a message received from Kafka");
    }

    [Fact]
    public void AppliesTo_TheOutboxProducerJob_IsFalse()
    {
        var (forwarder, _) = Build(targetQueue: "kafka-producer");
        var producerJob = TriggerJob(queue: "kafka-producer", idempotencyKey: null, tags: ["producer:kafka"]);

        forwarder.AppliesTo(producerJob).Should().BeFalse("forwarding the publisher would loop");
    }

    [Fact]
    public void AppliesTo_ForwardingNotConfigured_IsFalse()
    {
        var (forwarder, _) = Build(exhaustedTopic: null);

        forwarder.AppliesTo(TriggerJob()).Should().BeFalse();
    }

    // ─── ForwardAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ForwardAsync_PublishesTheOriginalBodyVerbatimToTheConfiguredTopic()
    {
        var (forwarder, captured) = Build();
        var job = TriggerJob();

        await forwarder.ForwardAsync(job, new InvalidOperationException("order failed"), CancellationToken.None);

        var payload = captured.Payload.Should().NotBeNull().And.Subject.As<KafkaPublishPayload>();
        payload.Topic.Should().Be("orders.exhausted");
        payload.ValueString.Should().Be(Body, "a copy of what was received, exactly");
        payload.Key.Should().BeNull("the original key is not stored");
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
        var job = TriggerJob(body: string.Empty);

        await forwarder.ForwardAsync(job, new InvalidOperationException("order failed"), CancellationToken.None);

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
        services.AddNexJobKafkaTrigger(ConfigureTrigger(exhaustedTopic: "orders.exhausted"));
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KafkaTriggerOptions>>();

        Func<KafkaTriggerOptions> read = () => options.Value;

        read.Should().Throw<OptionsValidationException>().WithMessage("*AddKafkaProducer*");
    }

    [Fact]
    public void Registration_ForwardingConfiguredWithTheProducer_PassesValidation()
    {
        var services = new ServiceCollection();
        services.AddKafkaProducer(o => o.BootstrapServers = "localhost:9092");
        services.AddNexJobKafkaTrigger(ConfigureTrigger(exhaustedTopic: "orders.exhausted"));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<KafkaTriggerOptions>>().Value.ExhaustedJobsTopic.Should().Be("orders.exhausted");
    }

    [Fact]
    public void Registration_ForwardingNotConfigured_DoesNotRequireTheProducer()
    {
        var services = new ServiceCollection();
        services.AddNexJobKafkaTrigger(ConfigureTrigger(exhaustedTopic: null));
        using var provider = services.BuildServiceProvider();

        Func<KafkaTriggerOptions> read = () => provider.GetRequiredService<IOptions<KafkaTriggerOptions>>().Value;

        read.Should().NotThrow();
    }

    [Fact]
    public void Registration_AddsTheForwarderToTheDispatcher()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Mock<IScheduler>().Object);
        services.AddNexJobKafkaTrigger(ConfigureTrigger(exhaustedTopic: "orders.exhausted"));
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IDeadLetterForwarder>().Should().ContainSingle().Which.Should().BeOfType<KafkaDeadLetterForwarder>();
    }

    private static Action<KafkaTriggerOptions> ConfigureTrigger(string? exhaustedTopic) => o =>
    {
        o.BootstrapServers = "localhost:9092";
        o.Topic = "orders-in";
        o.GroupId = "g";
        o.ExhaustedJobsTopic = exhaustedTopic;
    };

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static (KafkaDeadLetterForwarder Forwarder, Captured Captured) Build(
        string? exhaustedTopic = "orders.exhausted",
        string targetQueue = "orders",
        bool includeError = false)
    {
        var captured = new Captured();
        var scheduler = new Mock<IScheduler>();
        scheduler
            .Setup(s => s.EnqueueAsync<KafkaProducerJob, KafkaPublishPayload>(
                It.IsAny<KafkaPublishPayload>(),
                It.IsAny<string?>(),
                It.IsAny<JobPriority>(),
                It.IsAny<string?>(),
                It.IsAny<DuplicatePolicy>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<KafkaPublishPayload, string?, JobPriority, string?, DuplicatePolicy, IReadOnlyList<string>?, TimeSpan?, CancellationToken>(
                (payload, _, _, idempotencyKey, _, _, _, _) =>
                {
                    captured.Payload = payload;
                    captured.IdempotencyKey = idempotencyKey;
                })
            .ReturnsAsync(JobId.New());

        var options = Options.Create(new KafkaTriggerOptions
        {
            BootstrapServers = "localhost:9092",
            Topic = "orders-in",
            GroupId = "g",
            TargetQueue = targetQueue,
            ExhaustedJobsTopic = exhaustedTopic,
            ExhaustedJobsIncludeErrorHeader = includeError,
        });

        return (new KafkaDeadLetterForwarder(options, scheduler.Object), captured);
    }

    private static JobRecord TriggerJob(
        string queue = "orders",
        string? idempotencyKey = "kafka:orders-in:0:7",
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
        Tags = tags ?? ["trigger:kafka"],
        LastErrorMessage = lastErrorMessage,
        Status = JobStatus.Failed,
        Attempts = 3,
        MaxAttempts = 3,
    };

    private sealed class Captured
    {
        public KafkaPublishPayload? Payload { get; set; }

        public string? IdempotencyKey { get; set; }
    }
}
