using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexJob.Internal;

namespace NexJob.Trigger.SalesforceStreaming;

/// <summary>
/// Background service that connects to the Salesforce Streaming API via CometD/Bayeux,
/// receives events, and enqueues them into the NexJob execution pipeline.
/// </summary>
public sealed class SalesforceStreamingTriggerHandler : BackgroundService
{
    private readonly IScheduler _scheduler;
    private readonly ISalesforceStreamingAuthService _authService;
    private readonly ISalesforceBayeuxClient _bayeuxClient;
    private readonly SalesforceStreamingTriggerOptions _options;
    private readonly NexJobOptions _nexJobOptions;
    private readonly ILogger<SalesforceStreamingTriggerHandler> _logger;
    private readonly IStreamingReplayIdStore _replayIdStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="SalesforceStreamingTriggerHandler"/> class.
    /// </summary>
    /// <param name="scheduler">The NexJob scheduler instance.</param>
    /// <param name="authService">Authentication service for Salesforce.</param>
    /// <param name="bayeuxClient">Bayeux protocol client.</param>
    /// <param name="options">Salesforce Streaming trigger options.</param>
    /// <param name="nexJobOptions">Global NexJob options.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="customReplayIdStore">Optional custom Replay ID store injected via DI.</param>
    public SalesforceStreamingTriggerHandler(
        IScheduler scheduler,
        ISalesforceStreamingAuthService authService,
        ISalesforceBayeuxClient bayeuxClient,
        IOptions<SalesforceStreamingTriggerOptions> options,
        IOptions<NexJobOptions> nexJobOptions,
        ILogger<SalesforceStreamingTriggerHandler> logger,
        IStreamingReplayIdStore? customReplayIdStore = null)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(authService);
        ArgumentNullException.ThrowIfNull(bayeuxClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(nexJobOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _scheduler = scheduler;
        _authService = authService;
        _bayeuxClient = bayeuxClient;
        _options = options.Value;
        _nexJobOptions = nexJobOptions.Value;
        _logger = logger;
        _replayIdStore = customReplayIdStore ?? _options.ReplayIdStore ?? new FileStreamingReplayIdStore(_options.ReplayStoreDirectory);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Salesforce Streaming trigger starting for channel '{Channel}' on queue '{Queue}'",
            _options.Channel,
            _options.TargetQueue);

        var currentDelay = _options.ReconnectDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
            string? clientId = null;
            string? instanceUrl = null;
            string? accessToken = null;

            try
            {
                // 1. Authenticate with Salesforce
                var tokenResult = await _authService.GetTokenAsync(_options.Authentication, stoppingToken).ConfigureAwait(false);
                instanceUrl = tokenResult.InstanceUrl;
                accessToken = tokenResult.AccessToken;

                // 2. Perform Bayeux Handshake
                clientId = await _bayeuxClient.HandshakeAsync(
                    instanceUrl,
                    _options.CometdVersion,
                    accessToken,
                    stoppingToken).ConfigureAwait(false);

                // 3. Resolve starting Replay ID
                var startingReplayId = await ResolveStartingReplayIdAsync(stoppingToken).ConfigureAwait(false);

                // 4. Subscribe to the channel
                await _bayeuxClient.SubscribeAsync(
                    instanceUrl,
                    _options.CometdVersion,
                    accessToken,
                    clientId,
                    _options.Channel,
                    startingReplayId,
                    stoppingToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "Connected and listening to Salesforce Streaming channel '{Channel}' with starting Replay ID {ReplayId}",
                    _options.Channel,
                    startingReplayId);

                // 5. Connect long-polling loop
                while (!stoppingToken.IsCancellationRequested)
                {
                    var events = await _bayeuxClient.ConnectAsync(
                        instanceUrl,
                        _options.CometdVersion,
                        accessToken,
                        clientId,
                        stoppingToken).ConfigureAwait(false);

                    foreach (var evt in events)
                    {
                        await ProcessEventAsync(evt, stoppingToken).ConfigureAwait(false);
                        currentDelay = _options.ReconnectDelay;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (SalesforceBayeuxException ex) when (ex.ShouldRehandshake)
            {
                _logger.LogWarning(
                    ex,
                    "Salesforce Bayeux session expired or requires re-handshake on channel '{Channel}'. Invalidate token and reconnecting...",
                    _options.Channel);

                _authService.InvalidateToken();
                await Task.Delay(_options.ReconnectDelay, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Salesforce Streaming connection error on channel '{Channel}'. Reconnecting in {Delay}s...",
                    _options.Channel,
                    currentDelay.TotalSeconds);

                await Task.Delay(currentDelay, stoppingToken).ConfigureAwait(false);
                currentDelay = TimeSpan.FromTicks(Math.Min(currentDelay.Ticks * 2, _options.MaxReconnectDelay.Ticks));
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(instanceUrl) && !string.IsNullOrWhiteSpace(accessToken))
                {
                    await _bayeuxClient.DisconnectAsync(instanceUrl, _options.CometdVersion, accessToken, clientId, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        _logger.LogInformation("Salesforce Streaming trigger stopped for channel '{Channel}'", _options.Channel);
    }

    private async Task<long> ResolveStartingReplayIdAsync(CancellationToken cancellationToken)
    {
        var storedReplayId = await _replayIdStore.GetReplayIdAsync(_options.Channel, cancellationToken).ConfigureAwait(false);
        if (storedReplayId.HasValue)
        {
            _logger.LogInformation(
                "Resuming Salesforce channel '{Channel}' from stored Replay ID {ReplayId}",
                _options.Channel,
                storedReplayId.Value);

            return storedReplayId.Value;
        }

        if (_options.ReplayPreset == SalesforceStreamingReplayPreset.Custom)
        {
            return _options.CustomReplayId ?? (long)SalesforceStreamingReplayPreset.Latest;
        }

        return (long)_options.ReplayPreset;
    }

    private async Task ProcessEventAsync(SalesforceStreamingEventInput eventInput, CancellationToken cancellationToken)
    {
        var idempotencyKey = !string.IsNullOrWhiteSpace(eventInput.EventId)
            ? $"{eventInput.Channel}:{eventInput.EventId}"
            : $"{eventInput.Channel}:{eventInput.ReplayId}";

        var jobType = _options.JobType?.AssemblyQualifiedName ?? typeof(SalesforceStreamingEventJob).AssemblyQualifiedName!;
        var inputType = typeof(SalesforceStreamingEventInput).AssemblyQualifiedName!;
        var inputJson = JsonSerializer.Serialize(eventInput);

        var job = JobRecordFactory.Build(
            jobType: jobType,
            inputType: inputType,
            inputJson: inputJson,
            options: _nexJobOptions,
            queue: _options.TargetQueue,
            priority: _options.JobPriority,
            idempotencyKey: idempotencyKey,
            status: JobStatus.Enqueued,
            scheduledAt: null,
            tags: ["trigger:salesforce-streaming", $"channel:{_options.Channel}",],
            expiresAt: null,
            traceParent: Activity.Current?.Id);

        try
        {
            // GUARANTEE 4: Signal after enqueue is handled internally by IScheduler
            await _scheduler.EnqueueAsync(job, DuplicatePolicy.AllowAfterFailed, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // GUARANTEE 1: Never silently drop — log and route to dead letter if configured
            _logger.LogWarning(
                ex,
                "Failed to enqueue Salesforce Streaming event '{IdempotencyKey}' on queue '{Queue}'",
                idempotencyKey,
                _options.TargetQueue);

            if (!string.IsNullOrWhiteSpace(_options.DeadLetterQueue))
            {
                try
                {
                    var dlqJob = JobRecordFactory.Build(
                        jobType: jobType,
                        inputType: inputType,
                        inputJson: inputJson,
                        options: _nexJobOptions,
                        queue: _options.DeadLetterQueue,
                        priority: JobPriority.Low,
                        idempotencyKey: $"dlq:{idempotencyKey}",
                        status: JobStatus.Enqueued,
                        tags: ["trigger:salesforce-streaming", "dead-letter", $"channel:{_options.Channel}",]);

                    await _scheduler.EnqueueAsync(dlqJob, DuplicatePolicy.AllowAfterFailed, cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation(
                        "Moved failed Salesforce Streaming event '{IdempotencyKey}' to DeadLetterQueue '{DLQ}'",
                        idempotencyKey,
                        _options.DeadLetterQueue);
                }
                catch (Exception dlqEx)
                {
                    _logger.LogError(
                        dlqEx,
                        "Failed to route Salesforce Streaming event '{IdempotencyKey}' to DeadLetterQueue '{DLQ}'",
                        idempotencyKey,
                        _options.DeadLetterQueue);
                }
            }

            // GUARANTEE 5: Do NOT commit Replay ID if enqueue failed
            return;
        }

        // GUARANTEE 5: Commit Replay ID strictly after enqueue succeeds
        if (eventInput.ReplayId > 0)
        {
            await _replayIdStore.SaveReplayIdAsync(_options.Channel, eventInput.ReplayId, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Salesforce Streaming event '{IdempotencyKey}' enqueued on queue '{Queue}' and Replay ID {ReplayId} committed",
                idempotencyKey,
                _options.TargetQueue,
                eventInput.ReplayId);
        }
    }
}
