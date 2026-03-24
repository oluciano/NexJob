using System.Collections.Concurrent;

namespace NexJob.Internal;

/// <summary>
/// Singleton registry of named semaphores used to enforce per-resource concurrency limits
/// declared via <see cref="ThrottleAttribute"/>.
/// </summary>
internal sealed class ThrottleRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the <see cref="SemaphoreSlim"/> for the given resource, creating it on first access.
    /// The initial capacity is set to <paramref name="maxConcurrent"/> on creation; subsequent calls
    /// with a different <paramref name="maxConcurrent"/> value for the same resource are ignored.
    /// </summary>
    /// <param name="resource">The logical resource name.</param>
    /// <param name="maxConcurrent">Maximum concurrent holders allowed.</param>
    public SemaphoreSlim GetOrCreate(string resource, int maxConcurrent) =>
        _semaphores.GetOrAdd(resource, _ => new SemaphoreSlim(maxConcurrent, maxConcurrent));
}
