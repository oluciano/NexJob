using System.Text;
using System.Text.Json;
using Eventbus.V1;
using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexJob.Internal;

namespace NexJob.Trigger.Salesforce;

/// <summary>
/// Hosted service that consumes Salesforce events over a gRPC stream and enqueues them as NexJob jobs.
/// </summary>
public sealed class SalesforceTriggerHandler : BackgroundService
{
    private readonly SalesforceTriggerOptions _options;
    private readonly IScheduler _scheduler;
    private readonly IReplayIdStore _replayIdStore;
    private readonly ISalesforcePubSubClient _pubSubClient;
    private readonly ISalesforceSchemaService _schemaService;
    private readonly NexJobOptions _nexJobOptions;
    private readonly ILogger<SalesforceTriggerHandler> _logger;
    private readonly IListenerRegistry? _listenerRegistry;
    private readonly string _listenerId;

    /// <summary>
    /// Initializes a new instance of the <see cref="SalesforceTriggerHandler"/> class.
    /// </summary>
    /// <param name="options">Salesforce trigger options.</param>
    /// <param name="scheduler">NexJob scheduler for enqueueing jobs.</param>
    /// <param name="replayIdStore">Store for persisting Replay ID tokens.</param>
    /// <param name="pubSubClient">gRPC Pub/Sub client.</param>
    /// <param name="schemaService">Schema resolution and Avro decoding service.</param>
    /// <param name="nexJobOptions">NexJob global options.</param>
    /// <param name="logger">Diagnostic logger.</param>
    /// <param name="listenerRegistry">Optional listener registry for operational visibility.</param>
    public SalesforceTriggerHandler(
        IOptions<SalesforceTriggerOptions> options,
        IScheduler scheduler,
        IReplayIdStore replayIdStore,
        ISalesforcePubSubClient pubSubClient,
        ISalesforceSchemaService schemaService,
        NexJobOptions nexJobOptions,
        ILogger<SalesforceTriggerHandler> logger,
        IListenerRegistry? listenerRegistry = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _replayIdStore = replayIdStore ?? throw new ArgumentNullException(nameof(replayIdStore));
        _pubSubClient = pubSubClient ?? throw new ArgumentNullException(nameof(pubSubClient));
        _schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
        _nexJobOptions = nexJobOptions ?? throw new ArgumentNullException(nameof(nexJobOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _listenerRegistry = listenerRegistry;
        _listenerId = $"salesforce:{_options.Topic}";

        _listenerRegistry?.Register(new ListenerRegistration(
            Id: _listenerId,
            Broker: "Salesforce (Pub/Sub API)",
            Endpoint: _options.Topic,
            TargetJobType: _options.JobType ?? "Dynamic",
            JobTag: "trigger:salesforce"));
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        _logger.LogInformation(
            "Salesforce trigger starting for topic {Topic} (TargetQueue: {Queue}, FallbackPolicy: {Policy})",
            _options.Topic,
            _options.TargetQueue,
            _options.FallbackPolicy);

        _listenerRegistry?.UpdateStatus(_listenerId, ListenerStatus.Listening);

        var currentReplayId = await _replayIdStore.GetLastReplayIdAsync(_options.Topic, stoppingToken).ConfigureAwait(false);
        var currentPreset = _options.ReplayPreset;

        var backoffSeconds = 1;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var eventStream = _pubSubClient.SubscribeAsync(
                    _options.Topic,
                    currentReplayId,
                    currentPreset,
                    _options.BatchSize,
                    stoppingToken);

                await foreach (var consumerEvent in eventStream.WithCancellation(stoppingToken).ConfigureAwait(false))
                {
                    await ProcessEventAsync(consumerEvent, stoppingToken).ConfigureAwait(false);

                    // Update in-memory offset
                    currentReplayId = consumerEvent.ReplayId.ToByteArray();
                    backoffSeconds = 1; // reset backoff on successful event
                }

                if (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation(
                        "Salesforce Pub/Sub stream ended for topic {Topic}. Reconnecting in {Delay}s...",
                        _options.Topic,
                        backoffSeconds);
                    await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), stoppingToken).ConfigureAwait(false);
                    backoffSeconds = Math.Min(backoffSeconds * 2, 30);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (RpcException ex) when (IsExpiredOrInvalidReplayId(ex, currentReplayId))
            {
                if (_options.FallbackPolicy == ReplayFallbackPolicy.FailFast)
                {
                    _listenerRegistry?.UpdateStatus(_listenerId, ListenerStatus.Faulted, ex.Message);
                    _logger.LogCritical(
                        ex,
                        "FATAL: Expired or rejected Replay ID encountered for topic {Topic}. ReplayFallbackPolicy.FailFast active. Terminating service.",
                        _options.Topic);
                    throw;
                }

                _listenerRegistry?.UpdateStatus(_listenerId, ListenerStatus.Reconnecting, ex.Message);
                if (_options.FallbackPolicy == ReplayFallbackPolicy.ResetToLatest)
                {
                    _logger.LogWarning(
                        ex,
                        "Expired Replay ID for topic {Topic}. FallbackPolicy.ResetToLatest active: clearing saved offset and resuming from stream tip.",
                        _options.Topic);
                    currentReplayId = null;
                    currentPreset = SalesforceReplayPreset.Latest;
                }
                else if (_options.FallbackPolicy == ReplayFallbackPolicy.ResetToEarliest)
                {
                    _logger.LogWarning(
                        ex,
                        "Expired Replay ID for topic {Topic}. FallbackPolicy.ResetToEarliest active: clearing saved offset and resuming from earliest available event.",
                        _options.Topic);
                    currentReplayId = null;
                    currentPreset = SalesforceReplayPreset.Earliest;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _listenerRegistry?.UpdateStatus(_listenerId, ListenerStatus.Reconnecting, ex.Message);
                _logger.LogWarning(ex, "Salesforce Pub/Sub stream error on topic {Topic}. Reconnecting in {Delay}s...", _options.Topic, backoffSeconds);
                await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), stoppingToken).ConfigureAwait(false);

                // Exponential backoff up to 30s
                backoffSeconds = Math.Min(backoffSeconds * 2, 30);
            }
        }

        _listenerRegistry?.UpdateStatus(_listenerId, ListenerStatus.Stopped);
        _logger.LogInformation("Salesforce trigger stopped for topic {Topic}.", _options.Topic);
    }

    private static bool IsExpiredOrInvalidReplayId(RpcException ex, byte[]? currentReplayId)
    {
        if (currentReplayId is null || currentReplayId.Length == 0)
        {
            return false;
        }

        if (ex.StatusCode == StatusCode.InvalidArgument)
        {
            return true;
        }

        return ex.Message.Contains("replayId", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("INVALID_REPLAY_ID", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("too old", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ProcessEventAsync(ConsumerEvent consumerEvent, CancellationToken ct)
    {
        var replayBytes = consumerEvent.ReplayId.ToByteArray();
        var replayHex = Convert.ToHexString(replayBytes);
        var idempotencyKey = !string.IsNullOrEmpty(consumerEvent.Event?.Id) ? consumerEvent.Event.Id : replayHex;

        string? traceParent = null;
        var traceHeader = consumerEvent.Event?.Headers?.FirstOrDefault(h => string.Equals(h.Key, "traceparent", StringComparison.OrdinalIgnoreCase));
        if (traceHeader != null)
        {
            traceParent = Encoding.UTF8.GetString(traceHeader.Value.ToByteArray());
        }

        var schemaId = consumerEvent.Event?.SchemaId ?? string.Empty;
        var payloadBytes = consumerEvent.Event?.Payload?.ToByteArray() ?? [];

        var jsonPayload = await _schemaService.DecodePayloadToJsonAsync(schemaId, payloadBytes, ct).ConfigureAwait(false);

        var jobType = _options.JobType ?? typeof(SalesforceEventJob).AssemblyQualifiedName!;

        var job = JobRecordFactory.Build(
            jobType: jobType,
            inputType: typeof(string).AssemblyQualifiedName!,
            inputJson: jsonPayload,
            options: _nexJobOptions,
            queue: _options.TargetQueue,
            priority: _options.JobPriority,
            idempotencyKey: idempotencyKey,
            status: JobStatus.Enqueued,
            scheduledAt: null,
            tags: ["trigger:salesforce", $"topic:{_options.Topic}"],
            expiresAt: null,
            traceParent: traceParent);

        try
        {
            // GUARANTEE 4: Signal after enqueue handled internally by IScheduler
            await _scheduler.EnqueueAsync(job, DuplicatePolicy.AllowAfterFailed, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // GUARANTEE 1: Never silently drop — log warning and route to dead letter if configured
            _logger.LogWarning(ex, "Failed to enqueue Salesforce event {IdempotencyKey} on queue {Queue}.", idempotencyKey, _options.TargetQueue);

            if (!string.IsNullOrWhiteSpace(_options.DeadLetterQueue))
            {
                try
                {
                    var dlqJob = JobRecordFactory.Build(
                        jobType: jobType,
                        inputType: typeof(string).AssemblyQualifiedName!,
                        inputJson: jsonPayload,
                        options: _nexJobOptions,
                        queue: _options.DeadLetterQueue,
                        priority: JobPriority.Low,
                        idempotencyKey: $"dlq:{idempotencyKey}",
                        status: JobStatus.Enqueued,
                        tags: ["trigger:salesforce", "dead-letter", $"topic:{_options.Topic}"]);

                    await _scheduler.EnqueueAsync(dlqJob, DuplicatePolicy.AllowAfterFailed, ct).ConfigureAwait(false);
                    _logger.LogInformation("Moved failed Salesforce event {IdempotencyKey} to DeadLetterQueue {DLQ}.", idempotencyKey, _options.DeadLetterQueue);
                }
                catch (Exception dlqEx)
                {
                    _logger.LogError(dlqEx, "Failed to route Salesforce event {IdempotencyKey} to DeadLetterQueue {DLQ}.", idempotencyKey, _options.DeadLetterQueue);
                }
            }

            // GUARANTEE 5: Do NOT commit Replay ID if enqueue failed
            return;
        }

        // GUARANTEE 5: Commit Replay ID strictly after enqueue succeeds
        await _replayIdStore.SaveReplayIdAsync(_options.Topic, replayBytes, ct).ConfigureAwait(false);
        _logger.LogInformation("Salesforce event {IdempotencyKey} enqueued on queue {Queue} and Replay ID committed.", idempotencyKey, _options.TargetQueue);
    }
}
