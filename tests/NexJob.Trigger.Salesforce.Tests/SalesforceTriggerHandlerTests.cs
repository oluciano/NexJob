using System.Runtime.CompilerServices;
using System.Text;
using Eventbus.V1;
using FluentAssertions;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NexJob.Internal;
using Xunit;

namespace NexJob.Trigger.Salesforce.Tests;

public sealed class SalesforceTriggerHandlerTests
{
    private readonly Mock<IScheduler> _schedulerMock = new();
    private readonly Mock<IReplayIdStore> _replayStoreMock = new();
    private readonly Mock<ISalesforcePubSubClient> _pubSubClientMock = new();
    private readonly Mock<ISalesforceSchemaService> _schemaServiceMock = new();
    private readonly NexJobOptions _nexJobOptions = new() { MaxAttempts = 3 };

    private readonly SalesforceTriggerOptions _options = new()
    {
        Topic = "/data/ChangeEvents",
        TargetQueue = "salesforce-events",
        ClientId = "cid",
        ClientSecret = "csecret",
        FallbackPolicy = ReplayFallbackPolicy.FailFast,
    };

    [Fact]
    public async Task HappyPath_EventReceived_EnqueuedAndReplayIdCommitted()
    {
        // Arrange
        byte[] replayId = [0x01, 0x02, 0x03];
        var traceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

        var consumerEvent = new ConsumerEvent
        {
            ReplayId = ByteString.CopyFrom(replayId),
            Event = new ProducerEvent
            {
                Id = "evt-uuid-100",
                SchemaId = "schema-1",
                Payload = ByteString.CopyFrom([0x10, 0x20]),
                Headers =
                {
                    new EventHeader
                    {
                        Key = "traceparent",
                        Value = ByteString.CopyFromUtf8(traceParent),
                    },
                },
            },
        };

        _replayStoreMock.Setup(s => s.GetLastReplayIdAsync(_options.Topic, It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        _pubSubClientMock.Setup(c => c.SubscribeAsync(
                _options.Topic,
                null,
                SalesforceReplayPreset.Latest,
                _options.BatchSize,
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable([consumerEvent]));

        _schemaServiceMock.Setup(s => s.DecodePayloadToJsonAsync("schema-1", It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"AccountId\":\"001TEST\"}");

        JobRecord? capturedJob = null;
        _schedulerMock.Setup(s => s.EnqueueAsync(It.IsAny<JobRecord>(), DuplicatePolicy.AllowAfterFailed, It.IsAny<CancellationToken>()))
            .Callback<JobRecord, DuplicatePolicy, CancellationToken>((j, p, ct) => capturedJob = j)
            .ReturnsAsync(JobId.New());

        var handler = CreateHandler();

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await handler.StartAsync(cts.Token);
        await Task.Delay(200); // Allow event to be processed
        await handler.StopAsync(CancellationToken.None);

        // Assert
        capturedJob.Should().NotBeNull();
        capturedJob!.IdempotencyKey.Should().Be("evt-uuid-100");
        capturedJob.TraceParent.Should().Be(traceParent);
        capturedJob.Queue.Should().Be("salesforce-events");
        capturedJob.InputJson.Should().Be("{\"AccountId\":\"001TEST\"}");
        capturedJob.Tags.Should().Contain("trigger:salesforce");

        // Guarantee 5: ReplayId committed strictly after enqueue
        _replayStoreMock.Verify(s => s.SaveReplayIdAsync(_options.Topic, replayId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WarmRestart_ResumesFromPersistedReplayId()
    {
        // Arrange
        byte[] lastPersistedReplayId = [0xAA, 0xBB];

        _replayStoreMock.Setup(s => s.GetLastReplayIdAsync(_options.Topic, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lastPersistedReplayId);

        _pubSubClientMock.Setup(c => c.SubscribeAsync(
                _options.Topic,
                lastPersistedReplayId,
                SalesforceReplayPreset.Latest,
                _options.BatchSize,
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable<ConsumerEvent>([]));

        var handler = CreateHandler();

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await handler.StartAsync(cts.Token);
        await Task.Delay(100);
        await handler.StopAsync(CancellationToken.None);

        // Assert
        _pubSubClientMock.Verify(c => c.SubscribeAsync(
            _options.Topic,
            lastPersistedReplayId,
            SalesforceReplayPreset.Latest,
            _options.BatchSize,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnqueueFailure_DoesNotAdvanceReplayId()
    {
        // Arrange
        byte[] replayId = [0x05, 0x06];
        var consumerEvent = new ConsumerEvent
        {
            ReplayId = ByteString.CopyFrom(replayId),
            Event = new ProducerEvent
            {
                Id = "evt-failed-1",
                SchemaId = "schema-1",
                Payload = ByteString.CopyFrom([0x01]),
            },
        };

        _replayStoreMock.Setup(s => s.GetLastReplayIdAsync(_options.Topic, It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        _pubSubClientMock.Setup(c => c.SubscribeAsync(
                _options.Topic,
                null,
                SalesforceReplayPreset.Latest,
                _options.BatchSize,
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable([consumerEvent]));

        _schemaServiceMock.Setup(s => s.DecodePayloadToJsonAsync("schema-1", It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{}");

        // Enqueue fails with an exception
        _schedulerMock.Setup(s => s.EnqueueAsync(It.IsAny<JobRecord>(), DuplicatePolicy.AllowAfterFailed, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage unavailable"));

        var handler = CreateHandler();

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await handler.StartAsync(cts.Token);
        await Task.Delay(200);
        await handler.StopAsync(CancellationToken.None);

        // Assert — Guarantee 5: Replay ID must NOT be saved
        _replayStoreMock.Verify(s => s.SaveReplayIdAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnqueueFailure_WithDeadLetterQueueConfigured_EnqueuesToDlq()
    {
        // Arrange
        _options.DeadLetterQueue = "salesforce-dlq";
        byte[] replayId = [0x09];
        var consumerEvent = new ConsumerEvent
        {
            ReplayId = ByteString.CopyFrom(replayId),
            Event = new ProducerEvent
            {
                Id = "evt-dlq-1",
                SchemaId = "schema-1",
                Payload = ByteString.CopyFrom([0x01]),
            },
        };

        _replayStoreMock.Setup(s => s.GetLastReplayIdAsync(_options.Topic, It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        _pubSubClientMock.Setup(c => c.SubscribeAsync(
                _options.Topic,
                null,
                SalesforceReplayPreset.Latest,
                _options.BatchSize,
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable([consumerEvent]));

        _schemaServiceMock.Setup(s => s.DecodePayloadToJsonAsync("schema-1", It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{}");

        JobRecord? dlqJob = null;
        _schedulerMock.Setup(s => s.EnqueueAsync(It.Is<JobRecord>(j => j.Queue == _options.DeadLetterQueue), DuplicatePolicy.AllowAfterFailed, It.IsAny<CancellationToken>()))
            .Callback<JobRecord, DuplicatePolicy, CancellationToken>((j, p, ct) => dlqJob = j)
            .ReturnsAsync(JobId.New());

        _schedulerMock.Setup(s => s.EnqueueAsync(It.Is<JobRecord>(j => j.Queue == _options.TargetQueue), DuplicatePolicy.AllowAfterFailed, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("First enqueue failed"));

        var handler = CreateHandler();

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await handler.StartAsync(cts.Token);
        await Task.Delay(200);
        await handler.StopAsync(CancellationToken.None);

        // Assert
        dlqJob.Should().NotBeNull();
        dlqJob!.Queue.Should().Be("salesforce-dlq");
        dlqJob.Tags.Should().Contain("dead-letter");
        dlqJob.IdempotencyKey.Should().Be("dlq:evt-dlq-1");
    }

    [Fact]
    public async Task ProcessEvent_EnqueueFailsAndDlqThrows_LogsErrorWithoutCrashing()
    {
        // Arrange
        _options.DeadLetterQueue = "salesforce-dlq";
        var consumerEvent = new ConsumerEvent
        {
            ReplayId = ByteString.CopyFrom([0x01, 0x02]),
            Event = new ProducerEvent
            {
                Id = "evt-dlq-err",
                SchemaId = "schema-1",
                Payload = ByteString.CopyFrom([0x01]),
            },
        };

        _replayStoreMock.Setup(s => s.GetLastReplayIdAsync(_options.Topic, It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        _pubSubClientMock.Setup(c => c.SubscribeAsync(
                _options.Topic,
                null,
                SalesforceReplayPreset.Latest,
                _options.BatchSize,
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable([consumerEvent]));

        _schemaServiceMock.Setup(s => s.DecodePayloadToJsonAsync("schema-1", It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{}");

        // Primary enqueue fails
        _schedulerMock.Setup(s => s.EnqueueAsync(It.Is<JobRecord>(j => j.Queue == _options.TargetQueue), DuplicatePolicy.AllowAfterFailed, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("First enqueue failed"));

        // DLQ enqueue also fails
        _schedulerMock.Setup(s => s.EnqueueAsync(It.Is<JobRecord>(j => j.Queue == _options.DeadLetterQueue), DuplicatePolicy.AllowAfterFailed, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DLQ enqueue failed"));

        var handler = CreateHandler();

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await handler.StartAsync(cts.Token);
        await Task.Delay(200);
        await handler.StopAsync(CancellationToken.None);

        // Assert - ReplayId should NOT be committed
        _replayStoreMock.Verify(s => s.SaveReplayIdAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExpiredReplayId_FallbackPolicyFailFast_ThrowsException()
    {
        // Arrange
        _options.FallbackPolicy = ReplayFallbackPolicy.FailFast;
        byte[] staleReplay = [0x99, 0x99];

        _replayStoreMock.Setup(s => s.GetLastReplayIdAsync(_options.Topic, It.IsAny<CancellationToken>()))
            .ReturnsAsync(staleReplay);

        var rpcException = new RpcException(new Status(StatusCode.InvalidArgument, "400: The replayId is too old"));
        _pubSubClientMock.Setup(c => c.SubscribeAsync(
                _options.Topic,
                staleReplay,
                SalesforceReplayPreset.Latest,
                _options.BatchSize,
                It.IsAny<CancellationToken>()))
            .Throws(rpcException);

        var handler = CreateHandler();

        // Act & Assert
        await handler.StartAsync(CancellationToken.None);
        Func<Task> act = async () => await handler.ExecuteTask!;

        await act.Should().ThrowAsync<RpcException>();
    }

    [Fact]
    public async Task ExpiredReplayId_FallbackPolicyResetToLatest_ResetsOffsetAndReconnects()
    {
        // Arrange
        _options.FallbackPolicy = ReplayFallbackPolicy.ResetToLatest;
        byte[] staleReplay = [0x88, 0x88];

        _replayStoreMock.Setup(s => s.GetLastReplayIdAsync(_options.Topic, It.IsAny<CancellationToken>()))
            .ReturnsAsync(staleReplay);

        var rpcException = new RpcException(new Status(StatusCode.InvalidArgument, "400: INVALID_REPLAY_ID"));

        _pubSubClientMock.SetupSequence(c => c.SubscribeAsync(
                _options.Topic,
                It.IsAny<byte[]>(),
                It.IsAny<SalesforceReplayPreset>(),
                _options.BatchSize,
                It.IsAny<CancellationToken>()))
            .Throws(rpcException) // First attempt fails
            .Returns(ToAsyncEnumerable<ConsumerEvent>([])); // Second attempt succeeds with ResetToLatest

        var handler = CreateHandler();

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await handler.StartAsync(cts.Token);
        await Task.Delay(1200); // allow reconnect after 1s delay
        await handler.StopAsync(CancellationToken.None);

        // Assert: Second subscribe call must have null replayId and Latest preset
        _pubSubClientMock.Verify(c => c.SubscribeAsync(
            _options.Topic,
            null,
            SalesforceReplayPreset.Latest,
            _options.BatchSize,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExpiredReplayId_FallbackPolicyResetToEarliest_ResetsOffsetAndReconnects()
    {
        // Arrange
        _options.FallbackPolicy = ReplayFallbackPolicy.ResetToEarliest;
        byte[] staleReplay = [0x77, 0x77];

        _replayStoreMock.Setup(s => s.GetLastReplayIdAsync(_options.Topic, It.IsAny<CancellationToken>()))
            .ReturnsAsync(staleReplay);

        var rpcException = new RpcException(new Status(StatusCode.InvalidArgument, "400: The replayId is too old"));

        _pubSubClientMock.SetupSequence(c => c.SubscribeAsync(
                _options.Topic,
                It.IsAny<byte[]>(),
                It.IsAny<SalesforceReplayPreset>(),
                _options.BatchSize,
                It.IsAny<CancellationToken>()))
            .Throws(rpcException) // First attempt fails
            .Returns(ToAsyncEnumerable<ConsumerEvent>([])); // Second attempt succeeds with ResetToEarliest

        var handler = CreateHandler();

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await handler.StartAsync(cts.Token);
        await Task.Delay(1200);
        await handler.StopAsync(CancellationToken.None);

        // Assert: Second subscribe call must have null replayId and Earliest preset
        _pubSubClientMock.Verify(c => c.SubscribeAsync(
            _options.Topic,
            null,
            SalesforceReplayPreset.Earliest,
            _options.BatchSize,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Constructor_NullParameters_ThrowsArgumentNullException()
    {
        var options = Options.Create(_options);
        var scheduler = _schedulerMock.Object;
        var store = _replayStoreMock.Object;
        var client = _pubSubClientMock.Object;
        var schema = _schemaServiceMock.Object;
        var logger = NullLogger<SalesforceTriggerHandler>.Instance;

        var a1 = () => new SalesforceTriggerHandler(null!, scheduler, store, client, schema, _nexJobOptions, logger);
        var a2 = () => new SalesforceTriggerHandler(options, null!, store, client, schema, _nexJobOptions, logger);
        var a3 = () => new SalesforceTriggerHandler(options, scheduler, null!, client, schema, _nexJobOptions, logger);
        var a4 = () => new SalesforceTriggerHandler(options, scheduler, store, null!, schema, _nexJobOptions, logger);
        var a5 = () => new SalesforceTriggerHandler(options, scheduler, store, client, null!, _nexJobOptions, logger);
        var a6 = () => new SalesforceTriggerHandler(options, scheduler, store, client, schema, null!, logger);
        var a7 = () => new SalesforceTriggerHandler(options, scheduler, store, client, schema, _nexJobOptions, null!);

        a1.Should().Throw<ArgumentNullException>();
        a2.Should().Throw<ArgumentNullException>();
        a3.Should().Throw<ArgumentNullException>();
        a4.Should().Throw<ArgumentNullException>();
        a5.Should().Throw<ArgumentNullException>();
        a6.Should().Throw<ArgumentNullException>();
        a7.Should().Throw<ArgumentNullException>();
    }

    private SalesforceTriggerHandler CreateHandler()
    {
        return new SalesforceTriggerHandler(
            Options.Create(_options),
            _schedulerMock.Object,
            _replayStoreMock.Object,
            _pubSubClientMock.Object,
            _schemaServiceMock.Object,
            _nexJobOptions,
            NullLogger<SalesforceTriggerHandler>.Instance);
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        IEnumerable<T> items,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.Yield();
        }

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            _ = ex;
        }
    }
}
