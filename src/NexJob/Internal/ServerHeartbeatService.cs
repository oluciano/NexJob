using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexJob.Storage;

namespace NexJob.Internal;

/// <summary>
/// Registers the current active worker node (server) with the storage provider
/// and periodically updates its global heartbeat so the dashboard knows it's alive.
/// </summary>
internal sealed class ServerHeartbeatService : IHostedService, IDisposable
{
    private readonly IStorageProvider _storage;
    private readonly NexJobOptions _options;
    private readonly ILogger<ServerHeartbeatService> _logger;
    private readonly string _serverId;
    private Timer? _timer;

    public ServerHeartbeatService(
        IStorageProvider storage,
        IOptions<NexJobOptions> options,
        ILogger<ServerHeartbeatService> logger)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Use custom ServerId from options or generate a unique composite ID based on MachineName + Guid
        _serverId = !string.IsNullOrWhiteSpace(_options.ServerId)
            ? _options.ServerId
            : $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Registering active server node in cluster...");

        var serverInfo = new ServerRecord
        {
            Id = _serverId,
            WorkerCount = _options.Workers,
            Queues = _options.Queues,
            StartedAt = DateTimeOffset.UtcNow,
            HeartbeatAt = DateTimeOffset.UtcNow,
        };

        try
        {
            await _storage.RegisterServerAsync(serverInfo, cancellationToken).ConfigureAwait(false);

            // Start the periodic heartbeat timer
            _timer = new Timer(
                callback: DoWork,
                state: null,
                dueTime: _options.ServerHeartbeatInterval,
                period: _options.ServerHeartbeatInterval);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register server node {ServerId} during startup.", _serverId);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deregistering active server node...");
        _timer?.Change(Timeout.Infinite, 0);

        try
        {
            // Give 5 seconds for deregistration out of the total shutdown timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            await _storage.DeregisterServerAsync(_serverId, cts.Token).ConfigureAwait(false);
            _logger.LogInformation("Server node {ServerId} gracefully deregistered.", _serverId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to gracefully deregister server {ServerId} on shutdown.", _serverId);
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }

    private void DoWork(object? state)
    {
        // Fire and forget the heartbeat as the timer runs synchronously
        _ = HeartbeatAsync();
    }

    private async Task HeartbeatAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _storage.HeartbeatServerAsync(_serverId, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update global heartbeat for server {ServerId}.", _serverId);
        }
    }
}
