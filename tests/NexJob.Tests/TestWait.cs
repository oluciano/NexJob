using Moq;
using NexJob.Internal;

namespace NexJob.Tests;

/// <summary>
/// Waits for an observed condition instead of sleeping a fixed time, which is not enough on a loaded machine.
/// </summary>
internal static class TestWait
{
    /// <summary>Polls <paramref name="condition"/> until it returns true, or fails after <paramref name="timeout"/>.</summary>
    /// <param name="condition">The state the test needs before it asserts.</param>
    /// <param name="timeout">How long to wait; defaults to 10 seconds.</param>
    /// <returns>A task that completes once the condition holds.</returns>
    public static async Task UntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (!await condition().ConfigureAwait(false))
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The awaited condition did not hold before the timeout.");
            }

            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    /// <summary>Waits until at least <paramref name="expected"/> jobs are Succeeded in the in-memory storage.</summary>
    /// <param name="storage">The storage to observe.</param>
    /// <param name="expected">The number of succeeded jobs to wait for.</param>
    /// <returns>A task that completes once the count is reached.</returns>
    public static Task SucceededAsync(InMemoryStorageProvider storage, int expected)
        => UntilAsync(async () => (await storage.GetMetricsAsync().ConfigureAwait(false)).Succeeded >= expected);

    /// <summary>Waits until <paramref name="method"/> was invoked at least <paramref name="times"/> times on the mock.</summary>
    /// <typeparam name="T">The mocked type.</typeparam>
    /// <param name="mock">The mock to observe.</param>
    /// <param name="method">The name of the invoked method.</param>
    /// <param name="times">The minimum number of invocations to wait for.</param>
    /// <returns>A task that completes once the invocation count is reached.</returns>
    public static Task InvokedAsync<T>(Mock<T> mock, string method, int times)
        where T : class
        => UntilAsync(() => Task.FromResult(mock.Invocations.Count(i => string.Equals(i.Method.Name, method, StringComparison.Ordinal)) >= times));
}
