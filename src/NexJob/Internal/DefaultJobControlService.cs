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

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultJobControlService"/> class.
    /// </summary>
    /// <param name="dashboardStorage">The dashboard storage.</param>
    /// <param name="runtimeStore">The runtime store.</param>
    public DefaultJobControlService(
        IDashboardStorage dashboardStorage,
        IRuntimeSettingsStore runtimeStore)
    {
        _dashboardStorage = dashboardStorage;
        _runtimeStore = runtimeStore;
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
}
