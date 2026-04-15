using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace NexJob.Internal;

/// <summary>
/// Singleton registry of named semaphores used to enforce per-resource concurrency limits
/// declared via <see cref="ThrottleAttribute"/>.
/// Supports optional distributed throttling via <see cref="IDistributedThrottleStore"/>.
/// </summary>
internal sealed class ThrottleRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new(StringComparer.Ordinal);
    private readonly IDistributedThrottleStore? _distributedStore;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThrottleRegistry"/> class.
    /// </summary>
    /// <param name="distributedStore">The optional distributed throttle store.</param>
    /// <param name="logger">The optional logger.</param>
    public ThrottleRegistry(
        IDistributedThrottleStore? distributedStore = null,
        ILogger<ThrottleRegistry>? logger = null)
    {
        _distributedStore = distributedStore;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Attempts to acquire a throttle slot for the given resource.
    /// Returns true if acquired (both locally and distributed if enabled).
    /// If false, no slots were available and no locks are held.
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="maxConcurrent">The maximum concurrent.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task.</returns>
    public async Task<bool> TryAcquireAsync(string resource, int maxConcurrent, CancellationToken ct)
    {
        bool distributedAcquired = false;
        if (_distributedStore is not null)
        {
            try
            {
                var acquiredDistributed = await _distributedStore.TryAcquireAsync(resource, maxConcurrent, ct).ConfigureAwait(false);
                if (!acquiredDistributed)
                {
                    return false;
                }

                distributedAcquired = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Distributed throttle store failed for resource {Resource}. Degrading to local throttle.", resource);
            }
        }

        var sem = _semaphores.GetOrAdd(resource, _ => new SemaphoreSlim(maxConcurrent, maxConcurrent));

        bool acquiredLocal = false;
        try
        {
            acquiredLocal = await sem.WaitAsync(0, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (distributedAcquired)
            {
                await SafeReleaseDistributedAsync(resource).ConfigureAwait(false);
            }

            throw;
        }

        if (!acquiredLocal)
        {
            if (distributedAcquired)
            {
                await SafeReleaseDistributedAsync(resource).ConfigureAwait(false);
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Attempts to acquire a throttle slot for the given resource, waiting up to the specified timeout locally.
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="maxConcurrent">The maximum concurrent.</param>
    /// <param name="localWaitTimeout">The local wait timeout.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>True if acquired.</returns>
    public async Task<bool> TryAcquireWithWaitAsync(
        string resource,
        int maxConcurrent,
        TimeSpan localWaitTimeout,
        CancellationToken ct)
    {
        bool distributedAcquired = false;
        if (_distributedStore is not null)
        {
            try
            {
                // Distributed: tenta Redis primeiro (sem wait), depois local com timeout.
                var acquiredDistributed = await _distributedStore
                    .TryAcquireAsync(resource, maxConcurrent, ct)
                    .ConfigureAwait(false);
                if (!acquiredDistributed)
                {
                    return false;
                }

                distributedAcquired = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Distributed throttle store failed for resource {Resource}. Degrading to local throttle.", resource);
            }
        }

        var sem = _semaphores.GetOrAdd(resource, _ => new SemaphoreSlim(maxConcurrent, maxConcurrent));

        bool acquiredLocal;
        try
        {
            // DIFERENÇA: WaitAsync com timeout real em vez de WaitAsync(0)
            acquiredLocal = await sem.WaitAsync(localWaitTimeout, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (distributedAcquired)
            {
                await SafeReleaseDistributedAsync(resource).ConfigureAwait(false);
            }

            throw;
        }

        if (!acquiredLocal)
        {
            if (distributedAcquired)
            {
                await SafeReleaseDistributedAsync(resource).ConfigureAwait(false);
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Releases a previously acquired throttle slot.
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task.</returns>
    public async Task ReleaseAsync(string resource, CancellationToken ct)
    {
        if (_semaphores.TryGetValue(resource, out var sem))
        {
            sem.Release();
        }

        if (_distributedStore is not null)
        {
            await SafeReleaseDistributedAsync(resource, ct).ConfigureAwait(false);
        }
    }

    private async Task SafeReleaseDistributedAsync(string resource, CancellationToken ct = default)
    {
        if (_distributedStore is null)
        {
            return;
        }

        try
        {
            await _distributedStore.ReleaseAsync(resource, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release distributed throttle for resource {Resource}.", resource);
        }
    }
}
