using System.Runtime.CompilerServices;

namespace NexJob;

/// <summary>
/// Extension methods for <see cref="IJobContext"/> to simplify common patterns
/// such as progress reporting during iteration.
/// </summary>
public static class JobContextExtensions
{
    /// <summary>
    /// Returns an async enumerable that reports progress via <paramref name="context"/>
    /// as each item is yielded. Progress is calculated as a percentage of total items.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="source">The source async enumerable.</param>
    /// <param name="context">The job context to report progress to.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async IAsyncEnumerable<T> WithProgress<T>(
        this IAsyncEnumerable<T> source,
        IJobContext context,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var items = new List<T>();
        await foreach (var item in source.WithCancellation(ct))
        {
            items.Add(item);
        }

        for (var i = 0; i < items.Count; i++)
        {
            var percent = (int)((i + 1) * 100.0 / items.Count);
            await context.ReportProgressAsync(percent, ct: ct);
            yield return items[i];
        }
    }

    /// <summary>
    /// Returns an enumerable that reports progress via <paramref name="context"/>
    /// as each item is yielded. Progress is calculated as a percentage of total items.
    /// </summary>
    /// <remarks>
    /// Progress is reported fire-and-forget (no <c>await</c>) so the iterator never blocks a
    /// thread-pool thread. For I/O-backed storage providers this means the write is not
    /// guaranteed to complete before the next item is yielded; use
    /// <see cref="WithProgress{T}(IAsyncEnumerable{T}, IJobContext, CancellationToken)"/>
    /// when you need back-pressure or guaranteed delivery.
    /// </remarks>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="source">The source enumerable.</param>
    /// <param name="context">The job context to report progress to.</param>
    public static IEnumerable<T> WithProgress<T>(
        this IEnumerable<T> source,
        IJobContext context)
    {
        var items = source.ToList();
        var count = items.Count;
        for (var i = 0; i < count; i++)
        {
            var percent = (i + 1) * 100 / count;

            // Fire-and-forget: avoids blocking the iterator thread when storage is I/O-backed.
            _ = context.ReportProgressAsync(percent);
            yield return items[i];
        }
    }
}
