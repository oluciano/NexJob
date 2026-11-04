using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace NexJob.Trigger.SalesforceStreaming.Tests;

public sealed class SalesforceStreamingTriggerHandlerTests
{
    private const string PoisonedChannel = "/data/Order__ChangeEvent";
    private const long PoisonedReplayId = 1001;

    private readonly Mock<IScheduler> _mockScheduler = new();
    private readonly Mock<ISalesforceStreamingAuthService> _mockAuthService = new();
    private readonly Mock<ISalesforceBayeuxClient> _mockBayeuxClient = new();
    private readonly InMemoryStreamingReplayIdStore _store = new();
    private readonly NexJobOptions _nexJobOptions = new();

    [Fact]
    public async Task ExecuteAsync_HappyPath_ReceivesEventEnqueuesAndCommitsReplayId()
    {
        // Arrange
        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = "/data/Order__ChangeEvent",
            TargetQueue = "salesforce-queue",
            Authentication = new SalesforceStreamingAuthOptions
            {
                AuthType = SalesforceStreamingAuthType.SessionId,
                InstanceUrl = "https://test.salesforce.com",
                AccessToken = "tok123",
            },
        };

        _mockAuthService.Setup(a => a.GetTokenAsync(It.IsAny<SalesforceStreamingAuthOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesforceStreamingTokenResult("tok123", "https://test.salesforce.com"));

        _mockBayeuxClient.Setup(b => b.HandshakeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("client-123");

        _mockBayeuxClient.Setup(b => b.SubscribeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                "/data/Order__ChangeEvent",
                (long)SalesforceStreamingReplayPreset.Latest,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        using var payloadDoc = JsonDocument.Parse("{\"Order\": \"100\"}");
        var eventInput = new SalesforceStreamingEventInput(
            ReplayId: 9988,
            Channel: "/data/Order__ChangeEvent",
            Payload: payloadDoc.RootElement.Clone(),
            CreatedDate: DateTimeOffset.UtcNow,
            EventId: "evt-001");

        using var cts = new CancellationTokenSource();
        var connectCount = 0;

        _mockBayeuxClient.Setup(b => b.ConnectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                connectCount++;
                if (connectCount == 1)
                {
                    return Task.FromResult<IReadOnlyList<SalesforceStreamingEventInput>>(new[] { eventInput, });
                }

                cts.Cancel();
                return Task.FromResult<IReadOnlyList<SalesforceStreamingEventInput>>([]);
            });

        var handler = new SalesforceStreamingTriggerHandler(
            _mockScheduler.Object,
            _mockAuthService.Object,
            _mockBayeuxClient.Object,
            Microsoft.Extensions.Options.Options.Create(options),
            _nexJobOptions,
            NullLogger<SalesforceStreamingTriggerHandler>.Instance,
            _store);

        // Act
        await handler.StartAsync(cts.Token);
        await Task.Delay(100);
        await handler.StopAsync(CancellationToken.None);

        // Assert
        _mockScheduler.Verify(
            s => s.EnqueueAsync(
                It.Is<JobRecord>(j => j.Queue == "salesforce-queue" && j.IdempotencyKey == "/data/Order__ChangeEvent:evt-001"),
                DuplicatePolicy.AllowAfterFailed,
                It.IsAny<CancellationToken>()),
            Times.Once);

        var savedReplayId = await _store.GetReplayIdAsync("/data/Order__ChangeEvent");
        savedReplayId.Should().Be(9988);
    }

    [Fact]
    public async Task ExecuteAsync_ResumesFromStoredReplayId()
    {
        // Arrange
        await _store.SaveReplayIdAsync("/data/Order__ChangeEvent", 12345);

        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = "/data/Order__ChangeEvent",
            Authentication = new SalesforceStreamingAuthOptions
            {
                AuthType = SalesforceStreamingAuthType.SessionId,
                InstanceUrl = "https://test.salesforce.com",
                AccessToken = "tok123",
            },
        };

        _mockAuthService.Setup(a => a.GetTokenAsync(It.IsAny<SalesforceStreamingAuthOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesforceStreamingTokenResult("tok123", "https://test.salesforce.com"));

        _mockBayeuxClient.Setup(b => b.HandshakeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("client-123");

        using var cts = new CancellationTokenSource();

        _mockBayeuxClient.Setup(b => b.SubscribeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                "/data/Order__ChangeEvent",
                12345, // Must resume from 12345!
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .Callback(() => cts.Cancel());

        var handler = new SalesforceStreamingTriggerHandler(
            _mockScheduler.Object,
            _mockAuthService.Object,
            _mockBayeuxClient.Object,
            Microsoft.Extensions.Options.Options.Create(options),
            _nexJobOptions,
            NullLogger<SalesforceStreamingTriggerHandler>.Instance,
            _store);

        // Act
        await handler.StartAsync(cts.Token);
        await Task.Delay(100);
        await handler.StopAsync(CancellationToken.None);

        // Assert
        _mockBayeuxClient.Verify(
            b => b.SubscribeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                "/data/Order__ChangeEvent",
                12345,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_EnqueueFails_RoutesToDeadLetterAndCommitsReplayId()
    {
        // Arrange
        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = "/data/Order__ChangeEvent",
            TargetQueue = "salesforce-queue",
            DeadLetterQueue = "salesforce-dlq",
            Authentication = new SalesforceStreamingAuthOptions
            {
                AuthType = SalesforceStreamingAuthType.SessionId,
                InstanceUrl = "https://test.salesforce.com",
                AccessToken = "tok123",
            },
        };

        _mockAuthService.Setup(a => a.GetTokenAsync(It.IsAny<SalesforceStreamingAuthOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesforceStreamingTokenResult("tok123", "https://test.salesforce.com"));

        _mockBayeuxClient.Setup(b => b.HandshakeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("client-123");

        using var payloadDoc = JsonDocument.Parse("{}");
        var eventInput = new SalesforceStreamingEventInput(
            ReplayId: 443322,
            Channel: "/data/Order__ChangeEvent",
            Payload: payloadDoc.RootElement.Clone(),
            CreatedDate: DateTimeOffset.UtcNow);

        using var cts = new CancellationTokenSource();

        _mockBayeuxClient.Setup(b => b.ConnectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { eventInput, })
            .Callback(() => cts.Cancel());

        // First call to TargetQueue throws!
        _mockScheduler.Setup(s => s.EnqueueAsync(
                It.Is<JobRecord>(j => j.Queue == "salesforce-queue"),
                It.IsAny<DuplicatePolicy>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage unavailable"));

        var handler = new SalesforceStreamingTriggerHandler(
            _mockScheduler.Object,
            _mockAuthService.Object,
            _mockBayeuxClient.Object,
            Microsoft.Extensions.Options.Options.Create(options),
            _nexJobOptions,
            NullLogger<SalesforceStreamingTriggerHandler>.Instance,
            _store);

        // Act
        await handler.StartAsync(cts.Token);
        await Task.Delay(100);
        await handler.StopAsync(CancellationToken.None);

        // Assert - Routed to DLQ!
        _mockScheduler.Verify(
            s => s.EnqueueAsync(
                It.Is<JobRecord>(j => j.Queue == "salesforce-dlq"),
                DuplicatePolicy.AllowAfterFailed,
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Behavior changed in v6.8: a shutdown no longer cancels the Replay ID commit of an event already in the DLQ (#655).
        var storedId = await _store.GetReplayIdAsync("/data/Order__ChangeEvent");
        storedId.Should().Be(443322);
    }

    [Fact]
    public async Task ExecuteAsync_FirstEventFailsWithoutDlq_SecondEventMustNotCommitReplayIdAndSkipFirst()
    {
        // Arrange
        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = "/data/Order__ChangeEvent",
            TargetQueue = "salesforce-queue",
            DeadLetterQueue = null, // No DLQ
            ReconnectDelay = TimeSpan.FromMilliseconds(10),
            Authentication = new SalesforceStreamingAuthOptions
            {
                AuthType = SalesforceStreamingAuthType.SessionId,
                InstanceUrl = "https://test.salesforce.com",
                AccessToken = "tok123",
            },
        };

        _mockAuthService.Setup(a => a.GetTokenAsync(It.IsAny<SalesforceStreamingAuthOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesforceStreamingTokenResult("tok123", "https://test.salesforce.com"));

        _mockBayeuxClient.Setup(b => b.HandshakeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("client-123");

        using var doc1 = JsonDocument.Parse("{}");
        var event1 = new SalesforceStreamingEventInput(
            ReplayId: 1001,
            Channel: "/data/Order__ChangeEvent",
            Payload: doc1.RootElement.Clone(),
            CreatedDate: DateTimeOffset.UtcNow);

        using var doc2 = JsonDocument.Parse("{}");
        var event2 = new SalesforceStreamingEventInput(
            ReplayId: 1002,
            Channel: "/data/Order__ChangeEvent",
            Payload: doc2.RootElement.Clone(),
            CreatedDate: DateTimeOffset.UtcNow);

        using var cts = new CancellationTokenSource();

        var event1Attempted = new TaskCompletionSource<bool>();
        _mockScheduler.Setup(s => s.EnqueueAsync(
                It.Is<JobRecord>(j => j.IdempotencyKey == "/data/Order__ChangeEvent:1001"),
                It.IsAny<DuplicatePolicy>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => event1Attempted.TrySetResult(true))
            .ThrowsAsync(new InvalidOperationException("Storage unavailable on event 1"));

        var event2Enqueued = false;
        _mockScheduler.Setup(s => s.EnqueueAsync(
                It.Is<JobRecord>(j => j.IdempotencyKey == "/data/Order__ChangeEvent:1002"),
                It.IsAny<DuplicatePolicy>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => event2Enqueued = true)
            .ReturnsAsync(JobId.New());

        var reconnected = new TaskCompletionSource<bool>();
        var connectCount1 = 0;
        _mockBayeuxClient.Setup(b => b.ConnectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                connectCount1++;
                if (connectCount1 == 1)
                {
                    return Task.FromResult<IReadOnlyList<SalesforceStreamingEventInput>>(new[] { event1, event2, });
                }

                reconnected.TrySetResult(true);
                cts.Cancel();
                return Task.FromResult<IReadOnlyList<SalesforceStreamingEventInput>>([]);
            });

        var handler = new SalesforceStreamingTriggerHandler(
            _mockScheduler.Object,
            _mockAuthService.Object,
            _mockBayeuxClient.Object,
            Microsoft.Extensions.Options.Options.Create(options),
            _nexJobOptions,
            NullLogger<SalesforceStreamingTriggerHandler>.Instance,
            _store);

        // Act
        await handler.StartAsync(cts.Token);
        await event1Attempted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        // Wait for the reconnect: the batch was aborted after event 1 and the session started again.
        await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await handler.StopAsync(CancellationToken.None);

        // Assert: Event 2 must NOT be processed or commit its replay ID!
        event2Enqueued.Should().BeFalse("Event 2 must not be enqueued while event 1 failed and was not dead-lettered");
        var storedId = await _store.GetReplayIdAsync("/data/Order__ChangeEvent");
        storedId.Should().BeNull("Event 2 must not commit replay ID if event 1 was dropped, to prevent skipping event 1");
    }

    [Fact]
    public async Task ExecuteAsync_FirstEventFailsWithDlq_SecondEventProcessesAndCommitsReplayId()
    {
        // Arrange
        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = "/data/Order__ChangeEvent",
            TargetQueue = "salesforce-queue",
            DeadLetterQueue = "salesforce-dlq",
            Authentication = new SalesforceStreamingAuthOptions
            {
                AuthType = SalesforceStreamingAuthType.SessionId,
                InstanceUrl = "https://test.salesforce.com",
                AccessToken = "tok123",
            },
        };

        _mockAuthService.Setup(a => a.GetTokenAsync(It.IsAny<SalesforceStreamingAuthOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesforceStreamingTokenResult("tok123", "https://test.salesforce.com"));

        _mockBayeuxClient.Setup(b => b.HandshakeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("client-123");

        using var doc1 = JsonDocument.Parse("{}");
        var event1 = new SalesforceStreamingEventInput(
            ReplayId: 1001,
            Channel: "/data/Order__ChangeEvent",
            Payload: doc1.RootElement.Clone(),
            CreatedDate: DateTimeOffset.UtcNow);

        using var doc2 = JsonDocument.Parse("{}");
        var event2 = new SalesforceStreamingEventInput(
            ReplayId: 1002,
            Channel: "/data/Order__ChangeEvent",
            Payload: doc2.RootElement.Clone(),
            CreatedDate: DateTimeOffset.UtcNow);

        using var cts = new CancellationTokenSource();

        var event2Enqueued = new TaskCompletionSource<bool>();
        _mockScheduler.Setup(s => s.EnqueueAsync(
                It.Is<JobRecord>(j => j.Queue == "salesforce-queue" && j.IdempotencyKey == "/data/Order__ChangeEvent:1001"),
                It.IsAny<DuplicatePolicy>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage unavailable on event 1 primary"));

        _mockScheduler.Setup(s => s.EnqueueAsync(
                It.Is<JobRecord>(j => j.Queue == "salesforce-dlq"),
                DuplicatePolicy.AllowAfterFailed,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(JobId.New());

        _mockScheduler.Setup(s => s.EnqueueAsync(
                It.Is<JobRecord>(j => j.Queue == "salesforce-queue" && j.IdempotencyKey == "/data/Order__ChangeEvent:1002"),
                It.IsAny<DuplicatePolicy>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => event2Enqueued.TrySetResult(true))
            .ReturnsAsync(JobId.New());

        var connectCount1 = 0;
        _mockBayeuxClient.Setup(b => b.ConnectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                connectCount1++;
                if (connectCount1 == 1)
                {
                    return Task.FromResult<IReadOnlyList<SalesforceStreamingEventInput>>(new[] { event1, event2, });
                }

                cts.Cancel();
                return Task.FromResult<IReadOnlyList<SalesforceStreamingEventInput>>([]);
            });

        var handler = new SalesforceStreamingTriggerHandler(
            _mockScheduler.Object,
            _mockAuthService.Object,
            _mockBayeuxClient.Object,
            Microsoft.Extensions.Options.Options.Create(options),
            _nexJobOptions,
            NullLogger<SalesforceStreamingTriggerHandler>.Instance,
            _store);

        // Act
        await handler.StartAsync(cts.Token);
        await event2Enqueued.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await handler.StopAsync(CancellationToken.None);

        // Assert: Replay ID must be 1002 because event 1 was DLQ'd and event 2 enqueued!
        var storedId = await _store.GetReplayIdAsync("/data/Order__ChangeEvent");
        storedId.Should().Be(1002);
    }

    [Fact]
    public async Task ExecuteAsync_SessionExpired_InvalidatesTokenAndRehandshakes()
    {
        // Arrange
        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = "/data/Order__ChangeEvent",
            ReconnectDelay = TimeSpan.FromMilliseconds(10),
            MaxReconnectDelay = TimeSpan.FromMilliseconds(50),
            Authentication = new SalesforceStreamingAuthOptions
            {
                AuthType = SalesforceStreamingAuthType.SessionId,
                InstanceUrl = "https://test.salesforce.com",
                AccessToken = "tok123",
            },
        };

        _mockAuthService.Setup(a => a.GetTokenAsync(It.IsAny<SalesforceStreamingAuthOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesforceStreamingTokenResult("tok123", "https://test.salesforce.com"));

        _mockBayeuxClient.Setup(b => b.HandshakeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("client-123");

        using var cts = new CancellationTokenSource();
        var connectAttempt = 0;

        _mockBayeuxClient.Setup(b => b.ConnectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                connectAttempt++;
                if (connectAttempt == 1)
                {
                    throw new SalesforceBayeuxException("Session dead", "403::Unknown client", shouldRehandshake: true);
                }

                cts.Cancel();
                return Task.FromResult<IReadOnlyList<SalesforceStreamingEventInput>>([]);
            });

        var handler = new SalesforceStreamingTriggerHandler(
            _mockScheduler.Object,
            _mockAuthService.Object,
            _mockBayeuxClient.Object,
            Microsoft.Extensions.Options.Options.Create(options),
            _nexJobOptions,
            NullLogger<SalesforceStreamingTriggerHandler>.Instance,
            _store);

        // Act
        await handler.StartAsync(cts.Token);
        await Task.Delay(150);
        await handler.StopAsync(CancellationToken.None);

        // Assert
        _mockAuthService.Verify(a => a.InvalidateToken(), Times.Once);
        _mockBayeuxClient.Verify(b => b.HandshakeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    // ─── 3N Testing Matrix: ListenerRegistry ─────────────────────────────────

    [Fact]
    public async Task ListenerRegistry_Lifecycle_TracksStatusProperly_Positive()
    {
        // N1: Positive - registers as Starting, updates to Listening on successful handshake/subscribe, and Stopped on stop.
        var registry = new DefaultListenerRegistry();
        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = "/data/Order__ChangeEvent",
            TargetQueue = "salesforce-queue",
            Authentication = new SalesforceStreamingAuthOptions
            {
                AuthType = SalesforceStreamingAuthType.SessionId,
                InstanceUrl = "https://test.salesforce.com",
                AccessToken = "tok123",
            },
        };

        _mockAuthService.Setup(a => a.GetTokenAsync(It.IsAny<SalesforceStreamingAuthOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesforceStreamingTokenResult("tok123", "https://test.salesforce.com"));

        _mockBayeuxClient.Setup(b => b.HandshakeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("client-123");

        _mockBayeuxClient.Setup(b => b.SubscribeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                options.Channel,
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockBayeuxClient.Setup(b => b.ConnectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(100);
                return (IReadOnlyList<SalesforceStreamingEventInput>)Array.Empty<SalesforceStreamingEventInput>();
            });

        var handler = new SalesforceStreamingTriggerHandler(
            _mockScheduler.Object,
            _mockAuthService.Object,
            _mockBayeuxClient.Object,
            Microsoft.Extensions.Options.Options.Create(options),
            _nexJobOptions,
            NullLogger<SalesforceStreamingTriggerHandler>.Instance,
            _store,
            registry);

        var initial = registry.Get($"salesforce-streaming:{options.Channel}");
        initial.Should().NotBeNull();
        initial!.Status.Should().Be(ListenerStatus.Starting);
        initial.Broker.Should().Be("Salesforce (Streaming)");

        await handler.StartAsync(CancellationToken.None);
        await Task.Delay(50);

        var listening = registry.Get($"salesforce-streaming:{options.Channel}");
        listening!.Status.Should().Be(ListenerStatus.Listening);

        await handler.StopAsync(CancellationToken.None);

        var stopped = registry.Get($"salesforce-streaming:{options.Channel}");
        stopped!.Status.Should().Be(ListenerStatus.Stopped);
    }

    [Fact]
    public async Task ListenerRegistry_Lifecycle_TracksStatusProperly_Negative()
    {
        // N2: Negative - on handshake failure and retry, updates status to Reconnecting.
        var registry = new DefaultListenerRegistry();
        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = "/data/Order__ChangeEvent",
            TargetQueue = "salesforce-queue",
            ReconnectDelay = TimeSpan.FromMilliseconds(50),
            Authentication = new SalesforceStreamingAuthOptions
            {
                AuthType = SalesforceStreamingAuthType.SessionId,
                InstanceUrl = "https://test.salesforce.com",
                AccessToken = "tok123",
            },
        };

        _mockAuthService.Setup(a => a.GetTokenAsync(It.IsAny<SalesforceStreamingAuthOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesforceStreamingTokenResult("tok123", "https://test.salesforce.com"));

        _mockBayeuxClient.Setup(b => b.HandshakeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("Handshake error"));

        var handler = new SalesforceStreamingTriggerHandler(
            _mockScheduler.Object,
            _mockAuthService.Object,
            _mockBayeuxClient.Object,
            Microsoft.Extensions.Options.Options.Create(options),
            _nexJobOptions,
            NullLogger<SalesforceStreamingTriggerHandler>.Instance,
            _store,
            registry);

        using var cts = new CancellationTokenSource();
        await handler.StartAsync(cts.Token);
        await Task.Delay(100);

        var reconnecting = registry.Get($"salesforce-streaming:{options.Channel}");
        reconnecting!.Status.Should().Be(ListenerStatus.Reconnecting);

        await handler.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void ListenerRegistry_NullRegistry_BoundaryHandledGracefully()
    {
        // N3: Invalid input / Boundary - passing null registry does not throw.
        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = "/data/Order__ChangeEvent",
            TargetQueue = "salesforce-queue",
            Authentication = new SalesforceStreamingAuthOptions
            {
                AuthType = SalesforceStreamingAuthType.SessionId,
                InstanceUrl = "https://test.salesforce.com",
                AccessToken = "tok123",
            },
        };

        var act = () => new SalesforceStreamingTriggerHandler(
            _mockScheduler.Object,
            _mockAuthService.Object,
            _mockBayeuxClient.Object,
            Microsoft.Extensions.Options.Options.Create(options),
            _nexJobOptions,
            NullLogger<SalesforceStreamingTriggerHandler>.Instance,
            _store,
            listenerRegistry: null);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task ExecuteAsync_EnqueueFailsWithNoStoredReplayId_ResubscribesFromEarliestSoTheEventIsRedelivered()
    {
        // Arrange: nothing was ever committed and the preset is Latest, so resubscribing with Latest would drop the failed event.
        var subscribed = new List<long>();
        var resubscribed = new TaskCompletionSource();
        var handler = CreatePoisonedEventHandler(
            maxAttempts: 5,
            subscribed,
            onSubscribe: () => { if (subscribed.Count >= 2) { resubscribed.TrySetResult(); } },
            redeliverWhile: _ => true);

        // Act
        await handler.StartAsync(CancellationToken.None);
        await resubscribed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await handler.StopAsync(CancellationToken.None);

        // Assert
        subscribed[0].Should().Be((long)SalesforceStreamingReplayPreset.Latest);
        subscribed[1].Should().Be((long)SalesforceStreamingReplayPreset.Earliest);
    }

    [Fact]
    public async Task ExecuteAsync_EnqueueFailsRepeatedlyWithoutDlq_SkipsTheEventAfterMaxAttemptsAndCommitsItsReplayId()
    {
        // Arrange: a poisoned event fails on every delivery; without a limit it would block the channel forever.
        var subscribed = new List<long>();
        var resumedPastEvent = new TaskCompletionSource();
        var handler = CreatePoisonedEventHandler(
            maxAttempts: 2,
            subscribed,
            onSubscribe: () => { if (subscribed[^1] == PoisonedReplayId) { resumedPastEvent.TrySetResult(); } },
            redeliverWhile: last => last < PoisonedReplayId);

        // Act
        await handler.StartAsync(CancellationToken.None);
        await resumedPastEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await handler.StopAsync(CancellationToken.None);

        // Assert
        (await _store.GetReplayIdAsync(PoisonedChannel)).Should().Be(PoisonedReplayId);
        _mockScheduler.Verify(s => s.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private SalesforceStreamingTriggerHandler CreatePoisonedEventHandler(
        int maxAttempts,
        List<long> subscribed,
        Action onSubscribe,
        Func<long, bool> redeliverWhile,
        Action<SalesforceStreamingTriggerOptions>? configure = null,
        ILogger<SalesforceStreamingTriggerHandler>? logger = null,
        IStreamingReplayIdStore? store = null,
        IListenerRegistry? registry = null)
    {
        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = PoisonedChannel,
            TargetQueue = "salesforce-queue",
            MaxEnqueueAttempts = maxAttempts,
            ReconnectDelay = TimeSpan.FromMilliseconds(10),
            MaxReconnectDelay = TimeSpan.FromMilliseconds(50),
            Authentication = new SalesforceStreamingAuthOptions
            {
                AuthType = SalesforceStreamingAuthType.SessionId,
                InstanceUrl = "https://test.salesforce.com",
                AccessToken = "tok123",
            },
        };

        configure?.Invoke(options);
        _mockAuthService.Setup(a => a.GetTokenAsync(It.IsAny<SalesforceStreamingAuthOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesforceStreamingTokenResult("tok123", "https://test.salesforce.com"));
        _mockBayeuxClient.Setup(b => b.HandshakeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("client-123");
        _mockBayeuxClient.Setup(b => b.SubscribeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string _, string _, string _, string _, long replayId, CancellationToken _) =>
            {
                subscribed.Add(replayId);
                onSubscribe();
                return Task.FromResult(true);
            });

        using var doc = JsonDocument.Parse("{}");
        var poisoned = new SalesforceStreamingEventInput(PoisonedReplayId, PoisonedChannel, doc.RootElement.Clone(), DateTimeOffset.UtcNow);
        _mockBayeuxClient.Setup(b => b.ConnectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, string _, string _, string _, CancellationToken ct) =>
            {
                if (redeliverWhile(subscribed[^1]))
                {
                    return (IReadOnlyList<SalesforceStreamingEventInput>)new[] { poisoned };
                }

                await Task.Delay(Timeout.Infinite, ct);
                return [];
            });
        _mockScheduler.Setup(s => s.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage unavailable"));

        return new SalesforceStreamingTriggerHandler(
            _mockScheduler.Object,
            _mockAuthService.Object,
            _mockBayeuxClient.Object,
            Microsoft.Extensions.Options.Options.Create(options),
            _nexJobOptions,
            logger ?? NullLogger<SalesforceStreamingTriggerHandler>.Instance,
            store ?? _store,
            registry);
    }

    [Fact]
    public async Task ExecuteAsync_EnqueueFailureRoutedToDeadLetter_LogsTheOriginalEnqueueException()
    {
        // Arrange
        var logger = new ListLogger<SalesforceStreamingTriggerHandler>();
        var delivered = 0;
        var handler = CreatePoisonedEventHandler(5, [], () => { }, _ => ++delivered == 1, o => o.DeadLetterQueue = "salesforce-dlq", logger);
        var dlqEnqueued = new TaskCompletionSource();
        _mockScheduler.Setup(s => s.EnqueueAsync(It.Is<JobRecord>(j => j.Queue == "salesforce-dlq"), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .Callback(() => dlqEnqueued.TrySetResult())
            .ReturnsAsync(JobId.New());

        // Act
        await handler.StartAsync(CancellationToken.None);
        await dlqEnqueued.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await handler.StopAsync(CancellationToken.None);

        // Assert: without the cause in the log an operator cannot tell why events land in the DLQ.
        logger.Entries.Should().Contain(e => e.Exception != null && e.Exception.Message == "Storage unavailable");
    }

    [Fact]
    public async Task ExecuteAsync_DeadLetterEnqueueTimeout_CountsAsAnEnqueueFailure_AndTheEventIsSkippedAfterMaxAttempts()
    {
        // Arrange: a timeout in the DLQ write is a TaskCanceledException while the host is not stopping.
        var subscribed = new List<long>();
        var resumedPastEvent = new TaskCompletionSource();
        var handler = CreatePoisonedEventHandler(
            maxAttempts: 2,
            subscribed,
            onSubscribe: () => { if (subscribed[^1] == PoisonedReplayId) { resumedPastEvent.TrySetResult(); } },
            redeliverWhile: last => last < PoisonedReplayId,
            configure: o => o.DeadLetterQueue = "salesforce-dlq");
        _mockScheduler.Setup(s => s.EnqueueAsync(It.Is<JobRecord>(j => j.Queue == "salesforce-dlq"), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("DLQ storage timeout"));

        // Act
        await handler.StartAsync(CancellationToken.None);
        await resumedPastEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await handler.StopAsync(CancellationToken.None);

        // Assert: without the count the same event would be redelivered forever.
        (await _store.GetReplayIdAsync(PoisonedChannel)).Should().Be(PoisonedReplayId);
    }

    [Fact]
    public async Task ExecuteAsync_DeadLetterEnqueueCanceledByShutdown_IsNotLoggedAsAnError()
    {
        // Arrange
        var logger = new ListLogger<SalesforceStreamingTriggerHandler>();
        var delivered = 0;
        var handler = CreatePoisonedEventHandler(5, [], () => { }, _ => ++delivered == 1, o => o.DeadLetterQueue = "salesforce-dlq", logger);
        var dlqStarted = new TaskCompletionSource();
        _mockScheduler.Setup(s => s.EnqueueAsync(It.Is<JobRecord>(j => j.Queue == "salesforce-dlq"), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .Returns(async (JobRecord _, DuplicatePolicy _, CancellationToken ct) =>
            {
                dlqStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return JobId.New();
            });

        // Act
        await handler.StartAsync(CancellationToken.None);
        await dlqStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await handler.StopAsync(CancellationToken.None);

        // Assert: a shutdown overlapping the DLQ write is not a DLQ failure.
        logger.Entries.Should().NotContain(e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task ExecuteAsync_ReplayIdCommittedAfterDeadLetter_IsNotTiedToTheShutdownToken()
    {
        // Arrange: the event is already in the DLQ, so a shutdown must not cancel the commit of its Replay ID.
        var store = new Mock<IStreamingReplayIdStore>();
        store.Setup(s => s.GetReplayIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((long?)null);
        CancellationToken saveToken = new(true);
        store.Setup(s => s.SaveReplayIdAsync(PoisonedChannel, PoisonedReplayId, It.IsAny<CancellationToken>()))
            .Callback((string _, long _, CancellationToken ct) => saveToken = ct)
            .Returns(Task.CompletedTask);
        var delivered = 0;
        var handler = CreatePoisonedEventHandler(5, [], () => { }, _ => ++delivered == 1, o => o.DeadLetterQueue = "salesforce-dlq", store: store.Object);
        var dlqStarted = new TaskCompletionSource();
        var releaseDlq = new TaskCompletionSource();
        _mockScheduler.Setup(s => s.EnqueueAsync(It.Is<JobRecord>(j => j.Queue == "salesforce-dlq"), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                dlqStarted.TrySetResult();
                await releaseDlq.Task;
                return JobId.New();
            });

        // Act
        await handler.StartAsync(CancellationToken.None);
        await dlqStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stopping = handler.StopAsync(CancellationToken.None);
        releaseDlq.SetResult();
        await stopping;

        // Assert
        store.Verify(s => s.SaveReplayIdAsync(PoisonedChannel, PoisonedReplayId, It.IsAny<CancellationToken>()), Times.Once);
        saveToken.CanBeCanceled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShutdownDuringTheRehandshakeDelay_StillMarksTheListenerStopped()
    {
        // Arrange
        var registry = new DefaultListenerRegistry();
        var connected = new TaskCompletionSource();
        var handler = CreatePoisonedEventHandler(5, [], () => { }, _ => false, o => o.ReconnectDelay = TimeSpan.FromMinutes(1), registry: registry);
        _mockBayeuxClient.Setup(b => b.ConnectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                connected.TrySetResult();
                throw new SalesforceBayeuxException("session expired", shouldRehandshake: true);
            });

        // Act
        await handler.StartAsync(CancellationToken.None);
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await handler.StopAsync(CancellationToken.None);

        // Assert
        registry.Get($"salesforce-streaming:{PoisonedChannel}")!.Status.Should().Be(ListenerStatus.Stopped);
    }
}
