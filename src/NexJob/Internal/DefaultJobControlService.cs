using NexJob.Configuration;
using NexJob.Storage;

namespace NexJob.Internal;

/// <summary>
/// Default implementation of <see cref="IJobControlService"/>.
/// </summary>
internal sealed class DefaultJobControlService : IJobControlService
{
    private readonly IDashboardStorage _dashboardStorage;
    private readonly IRuntimeSettingsStore _runtimeStore;
    private readonly IQueueCircuitBreakerManager? _circuitBreakerManager;
    private readonly NexJobOptions? _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultJobControlService"/> class.
    /// </summary>
    /// <param name="dashboardStorage">The dashboard storage.</param>
    /// <param name="runtimeStore">The runtime store.</param>
    /// <param name="circuitBreakerManager">Optional queue circuit breaker manager.</param>
    /// <param name="options">Optional NexJob options, used to map a queue name to the stored name.</param>
    public DefaultJobControlService(
        IDashboardStorage dashboardStorage,
        IRuntimeSettingsStore runtimeStore,
        IQueueCircuitBreakerManager? circuitBreakerManager = null,
        NexJobOptions? options = null)
    {
        _options = options;
        _dashboardStorage = dashboardStorage;
        _runtimeStore = runtimeStore;
        _circuitBreakerManager = circuitBreakerManager;
    }

    /// <inheritdoc/>
    public Task RequeueJobAsync(JobId id, CancellationToken ct = default)
    {
        return _dashboardStorage.RequeueJobAsync(id, ct);
    }

    /// <inheritdoc/>
    public Task DeleteJobAsync(JobId id, CancellationToken ct = default)
    {
        return _dashboardStorage.DeleteJobAsync(id, ct);
    }

    /// <inheritdoc/>
    public async Task PauseQueueAsync(string queue, CancellationToken ct = default)
    {
        var rt = await _runtimeStore.GetAsync(ct).ConfigureAwait(false);
        if (rt.PausedQueues.Add(queue))
        {
            await _runtimeStore.SaveAsync(rt, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task ResumeQueueAsync(string queue, CancellationToken ct = default)
    {
        var rt = await _runtimeStore.GetAsync(ct).ConfigureAwait(false);
        if (rt.PausedQueues.Remove(queue))
        {
            await _runtimeStore.SaveAsync(rt, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public Task ResetQueueCircuitAsync(string queue, CancellationToken ct = default)
    {
        _circuitBreakerManager?.Reset(_options?.ResolveQueue(queue) ?? queue);
        return Task.CompletedTask;
    }
}
